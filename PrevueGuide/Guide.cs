using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using PrevueGuide.Core.Utilities;
using PrevueGuide.Model;
using static SDL2.SDL;
using static SDL2.SDL_image;
using static SDL2.SDL_ttf;
using XmlTv.Model;

namespace PrevueGuide;

// RANDOM NOTES:
// Create a render queue
// - List of objects along with pointer to current "top" object
// - Objects will be timebar, logo, advertisement, channel list, etc.
// - Channel list will be a special object with its own "current channel" pointer?
// - When an object would be rendered out of scope,
// Render queue will exist of objects (time bar, logo, advertisement, channel, etc.)
// Will also have a pointer as to what the current most visible object is and its position, other objects
//     will be rendered so long as the previous object doesn't cross over the visibility boundary
// Object rendering happens at the beginning of each frame (on demand), if the object to render to a texture isn't
//     already rendered and cached.
// If an object is no longer going to be visible (passes over the top threashold of the frame) it's evicted
//     from the cache.
// Re-generate the queue whenever the timebox changes.
// todo: press d to delete database
// Timebox appearance is what re-triggers timebox calculations

public class Guide
{
    private const int StandardRowHeight = 56;
    private const int StandardColumnWidth = 172;
    private const int FirstColumnWidth = StandardColumnWidth;
    private const int SecondColumnWidth = StandardColumnWidth;
    // For rendering the frame, it's this but when generating
    // contents in the 3rd column, use the standard column width.
    private const int ThirdColumnWidth = StandardColumnWidth + 36;
    private const int SingleArrowWidth = 16;
    private const int DoubleArrowWidth = 24;
    private const string DatabaseFilename = "listings.db";
    private const int NumberOfFrameTimesToCapture = 60;
    private const int MaximumChannelsToRender = 10;

    private bool _reloadGuideData = true;
    private bool _regenerateGridTextures = false;
    private int _channelsToRender = MaximumChannelsToRender;
    private int _channelsAdded = 0;
    private readonly List<long> frameTimeList = new();
    private int _rowsVisible = 3;
    private bool _fullscreen = false;
    private int _windowWidth = 716;
    private int _windowHeight = 436;

    // Set this to the beginning of computer time so we can force it to update.
    private DateTime _currentTimeToDisplay = DateTime.UnixEpoch;
    private DateTime _now = DateTime.Now;
    private DateTime _nowBlock;
    private DateTime _nowBlockEnd;

    private readonly Core.Data.SQLite.SQLiteListingsData _data;
    private readonly List<LineUpEntry> _channelLineUp = new();
    private readonly List<Listing> _channelListings = new();

    // These could use some serious love.
    private readonly Dictionary<string, (Texture? Line1, Texture? Line2)> _listingChannelTextureMap = new();
    private readonly Dictionary<string, List<((int ColumnNumber, int ColumnOffset) ColumnInfo, Texture? Frame, Texture? Line1,
        Texture? Line2, int Block, DateTime StartTime, DateTime EndTime)>> _listingTextTextureMap = new();

    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _openedTtfFont;

    private FontSizeManager _fontSizeManager;
    private TextureManager _staticTextureManager;

    private readonly FontConfiguration _selectedFont;

    private Texture? _timeTexture;
    private Texture? _channelFrameTexture, _clockFrameTexture, _timeboxFrameTexture, _timeboxLastFrameTexture;
    private Texture? _timeboxFrameOneTime, _timeboxFrameTwoTime, _timeboxFrameThreeTime;
    private Texture? _columnOneOrTwo, _columnThree, _columnOneAndTwo, _columnTwoAndThree, _columnOneTwoAndThree;

    private readonly ILogger _logger;

    private int _scale;
    private bool _running = true;
    private bool _showFrameRate = false;
    private bool _limitFps = false;

    private readonly SDL_Color _gridTextYellow = new() { a = 255, r = 203, g = 209, b = 0 };
    private readonly SDL_Color _gridTextWhite = new() { a = 255, r = 170, g = 170, b = 170 };
    private readonly SDL_Color _clockBackgroundColor = new() { a = 255, r = 34, g = 41, b = 141 };
    private readonly SDL_Color _gridDefaultBlue = new() { a = 255, r = 3, g = 0, b = 88 };
    // gridTestRed = { a = 255, r = 192, g = 0, b = 0 };

    private bool _recalculateRowPositions = true;
    private int _gridTarget = 0;
    private int _gridValue = 0;
    private int _scrollingTest = 0;

    public Guide(ILogger logger)
    {
        _logger = logger;
        _data = new Core.Data.SQLite.SQLiteListingsData(_logger, DatabaseFilename);

        var fontConfigurationMap =
            JsonSerializer.Deserialize<Dictionary<string, FontConfiguration>>(File.ReadAllText("assets/fonts/fonts.json"));
        _selectedFont = fontConfigurationMap?["PrevueGrid"];

        SetBlockTimes();
    }

    public void Run()
    {
        Setup();

        while (_running)
        {
            PollEvents();
            Render();
        }

        CleanUp();
    }

    private void SetBlockTimes()
    {
        _nowBlock = Time.ClampToPreviousHalfHour(_now);
        _nowBlockEnd = _nowBlock.AddMinutes(90);
    }

    private async Task ReloadGuideData()
    {
        try
        {
            var channelLineUpStopwatch = Stopwatch.StartNew();
            var channels = await _data.GetChannelLineup();
            _channelLineUp.Clear();
            _channelLineUp.AddRange(channels);
            _logger.LogInformation("[Guide] Channel line-up loaded. {channelLineUpCount} channels found in " +
                                   "{loadTimeMilliseconds} ms.",
                _channelLineUp.Count,
                channelLineUpStopwatch.ElapsedMilliseconds);

            _channelsToRender = new[] { _channelLineUp.Count, MaximumChannelsToRender }.Min();

            var channelListingsStopwatch = Stopwatch.StartNew();
            var listings = await _data.GetChannelListings(_nowBlock, _nowBlockEnd);
            _channelListings.Clear();
            _channelListings.AddRange(listings);

            _logger.LogInformation($@"[Guide] Channel listings loaded. {_channelListings.Count()} listings found " +
                                   $"in {channelListingsStopwatch.ElapsedMilliseconds} ms.");

            _regenerateGridTextures = true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception when reloading guide data");
        }
    }

    private void GenerateListingTextures()
    {
        _logger.LogInformation("[Textures] Removing old listing textures");
        foreach (var k in _listingTextTextureMap.Keys)
        {
            _listingChannelTextureMap[k].Line1?.Dispose();
            _listingChannelTextureMap[k].Line2?.Dispose();
            _listingChannelTextureMap.Remove(k);
        }

        _logger.LogInformation("[Textures] Generating new listing textures");
        for (var i = 0; i < _channelsToRender + 5; i++)
        {
            var channel = _channelLineUp.ElementAtOrDefault(i);

            if (channel == null)
                continue;

            var listings = _channelListings.Where(cl => cl.ChannelId == channel.Id);

            var listingList = new List<((int ColumnNumber, int ColumnOffset) ColumnInfo, Texture? Frame, Texture? Line1,
                Texture? Line2, int Block, DateTime StartTime, DateTime EndTime)>();

            foreach (var listing in listings)
            {
                var columnInfo = UI.CalculateColumnDetails(listing.Block,
                    FirstColumnWidth, SecondColumnWidth);

                var remainingDuration = listing.StartTime > _nowBlock
                    ? listing.EndTime - listing.StartTime
                    : listing.EndTime - _nowBlock;

                Texture? frameTexture = null;

                frameTexture = remainingDuration.TotalMinutes switch
                {
                    > 60 => columnInfo.column switch
                    {
                        1 => _columnOneTwoAndThree,
                        2 => _columnTwoAndThree,
                        3 => _columnThree,
                        _ => frameTexture
                    },
                    > 30 => columnInfo.column switch
                    {
                        1 => _columnOneAndTwo,
                        2 => _columnTwoAndThree,
                        3 => _columnThree,
                        _ => frameTexture
                    },
                    _ => columnInfo.column switch
                    {
                        1 => _columnOneOrTwo,
                        2 => _columnOneOrTwo,
                        3 => _columnThree,
                        _ => frameTexture
                    }
                };

                if (frameTexture == null)
                    continue;

                _ = SDL_QueryTexture(frameTexture.SdlTexture, out _, out _, out var frameWidth, out _);

                // hack: make all the columns align.
                frameWidth -= (frameWidth % StandardColumnWidth);

                if (listing.StartTime < _nowBlock)
                {
                    if ((_nowBlock - listing.StartTime).TotalMinutes > 30)
                        frameWidth -= (DoubleArrowWidth * _scale);
                    else
                        frameWidth -= (SingleArrowWidth * _scale);
                }

                if (listing.EndTime > _nowBlockEnd)
                {
                    if ((listing.EndTime - _nowBlock).TotalMinutes > 30)
                        frameWidth -= (DoubleArrowWidth * _scale);
                    else
                        frameWidth -= (SingleArrowWidth * _scale);
                }

                // The frame has the bevel so take that into account.
                // The bevel is 4 pixels on each side, so that * 2, scaled.
                frameWidth -= (8 * _scale);

                var listingRating = "";
                var listingSubtitled = "";

                if (!string.IsNullOrWhiteSpace(listing.Rating))
                {
                    listingRating = _selectedFont.IconMap.ContainsKey(listing.Rating)
                        ? $" {_selectedFont.IconMap[listing.Rating].Value}"
                        : $" {listing.Rating}";
                }

                if (!string.IsNullOrWhiteSpace(listing.Subtitled))
                {
                    listingSubtitled = _selectedFont.IconMap.ContainsKey(listing.Subtitled)
                        ? $" {_selectedFont.IconMap[listing.Subtitled].Value}"
                        : $" {listing.Subtitled}";
                }

                var listingText = listing.Category == "Movie"
                    ? $"\"{listing.Title}\" ({listing.Year}) {listing.Description}{listingRating}{listingSubtitled}"
                    : $"{listing.Title}{listingRating}{listingSubtitled}";

                var lines =
                    CalculateLineWidths(listingText, frameWidth, new Dictionary<int, int>()).ToList();

                var firstLine = lines.ElementAtOrDefault(0) ?? " ";
                if (string.IsNullOrWhiteSpace(firstLine))
                    firstLine = " ";
                var line1 = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont, firstLine,
                    _gridTextWhite, _scale));

                var secondLine = lines.ElementAtOrDefault(1);
                if (string.IsNullOrWhiteSpace(secondLine))
                    secondLine = " ";
                var line2 = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont, secondLine,
                    _gridTextWhite, _scale));

                listingList.Add((columnInfo, frameTexture, line1, line2, listing.Block, listing.StartTime,
                    listing.EndTime));
            }

            if (_listingTextTextureMap.ContainsKey(channel.Id))
            {
                _logger.LogWarning("Attempted to add channel {callSign} ({id}) but already exists, ignoring.",
                    channel.CallSign, channel.Id);
                continue;
            }

            _listingTextTextureMap.Add(channel.Id, listingList);

            var channelLine1 = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
                channel.ChannelNumber,
                _gridTextYellow, _scale));
            var channelLine2 = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
                channel.CallSign,
                _gridTextYellow, _scale));

            _listingChannelTextureMap.Add(channel.Id, (channelLine1, channelLine2));
            _channelsAdded++;
        }

        _logger.LogInformation(("[Textures] Texture generation complete"));
    }

    private async Task ProcessXmlTvFile(string filename)
    {
        try
        {
            _logger.LogInformation("[Processor] Received file {filename}", filename);

            var xmlReaderSettings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Ignore
            };

            await using var fileStream = new FileStream(filename, FileMode.Open);
            using var xmlReader = XmlReader.Create(fileStream, xmlReaderSettings);
            var tv = (Tv)new XmlSerializer(typeof(Tv)).Deserialize(xmlReader)!;

            _logger.LogInformation("[Processor] Importing guide data...");

            var channelStopWatch = Stopwatch.StartNew();
            var numberOfChannels = 0;
            if (tv.Channel != null)
            {
                foreach (var channel in tv.Channel)
                {
                    await _data.AddChannelToLineup(channel.SourceName, channel.ChannelNumber, channel.CallSign);
                    numberOfChannels++;
                }
            }
            _logger.LogInformation("[Processor] Imported {numberOfChannels} channels in {elapsedTime}.",
                numberOfChannels, channelStopWatch.Elapsed);

            var listingStopWatch = Stopwatch.StartNew();
            var numberOfPrograms = 0;
            if (tv.Programme != null)
            {
                var queue = new Queue<(string, string, string, string, string, string, string, DateTime, DateTime)>();

                foreach (var programme in tv.Programme)
                {
                    var title = programme.Title.First().Text;
                    var description = programme.Desc.FirstOrDefault()?.Text ?? "";
                    var category = programme.Category.FirstOrDefault()?.Text ?? "";
                    var year = programme.Date ?? "";
                    var rating = programme.Rating?.Value?.FirstOrDefault() ?? "";
                    var subtitled = programme.Subtitles?.FirstOrDefault(s => s.Type == "teletext")?.Type ?? "";

                    if (subtitled == "teletext")
                        subtitled = "CC";

                    queue.Enqueue((programme.SourceName, title, category, description, year, rating, subtitled,
                        DateTime.ParseExact(programme.Start, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo,
                            DateTimeStyles.AssumeLocal).ToUniversalTime(),
                        DateTime.ParseExact(programme.Stop, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo,
                            DateTimeStyles.AssumeLocal).ToUniversalTime()));
                    numberOfPrograms++;
                }

                while (queue.Any())
                {
                    var list = new List<(string, string, string, string, string, string, string, DateTime, DateTime)>();

                    for (var i = 0; i < 30; i++)
                    {
                        if (!queue.Any())
                            break;

                        list.Add(queue.Dequeue());
                    }

                    await _data.AddChannelListing(list);
                }
            }

            _logger.LogInformation("[Processor] Imported {numberOfPrograms} programs in {elapsedTime}.",
                numberOfPrograms, listingStopWatch.Elapsed);
            _logger.LogInformation("[Processor] Guide data imported.");

            _reloadGuideData = true;
            _logger.LogInformation("[Processor] Prepared for regeneration.");
        }
        catch (Exception e)
        {
            _logger.LogError("[Processor] Encountered exception in rendering of XMLTV: {message} @ {stackTrace}",
                e.Message, e.StackTrace);
        }
    }

    private IEnumerable<string> CalculateLineWidths(string targetString, int defaultLineWidth, Dictionary<int, int> specifiedLineWidths)
    {
        var currentLineLength = 0;
        var currentLineNumber = 1;
        var currentLine = string.Empty;
        var renderedLines = new List<string>();

        var lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
            ? specifiedLineWidths[currentLineNumber]
            : defaultLineWidth;

        foreach (var component in targetString.Split(' '))
        {
            var componentLength = component.ToCharArray().Select(c => _fontSizeManager[$"{c}"]).Sum(v => v.width);
            var paddedComponentLength = (string.IsNullOrWhiteSpace(currentLine) ? 0 : _fontSizeManager[' '].width) + componentLength;

            if (currentLineLength + paddedComponentLength > lineWidth)
            {
                if (!string.IsNullOrWhiteSpace(currentLine))
                {
                    renderedLines.Add(currentLine);
                    currentLine = component;
                    currentLineLength = componentLength;

                    currentLineNumber++;
                    lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
                        ? specifiedLineWidths[currentLineNumber]
                        : defaultLineWidth;
                }
                else
                {
                    // We have to split the line in the middle somewhere.
                    var chars = component.ToCharArray();
                    var componentSubLength = 0;
                    var chunk = string.Empty;

                    foreach (var targetChar in chars)
                    {
                        var glyphWidth = _fontSizeManager[targetChar].width;
                        var newSubLength = componentSubLength + glyphWidth;

                        if (newSubLength > lineWidth)
                        {
                            renderedLines.Add(chunk);
                            chunk = string.Empty;
                            componentSubLength = 0;

                            currentLineNumber++;
                            lineWidth = specifiedLineWidths.ContainsKey(currentLineNumber)
                                ? specifiedLineWidths[currentLineNumber]
                                : defaultLineWidth;
                        }

                        chunk = $"{chunk}{targetChar}";
                        componentSubLength += glyphWidth;
                    }

                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
                        currentLine = $"{currentLine}{padding}{chunk}";
                        currentLineLength += componentSubLength;
                    }
                }
            }
            else
            {
                var padding = string.IsNullOrWhiteSpace(currentLine) ? string.Empty : " ";
                currentLine = $"{currentLine}{padding}{component}";
                currentLineLength += paddedComponentLength;
            }
        }

        if (!string.IsNullOrWhiteSpace(currentLine))
        {
            renderedLines.Add(currentLine);
        }

        return renderedLines;
    }

    private int GenerateTargetHeight() => _windowHeight - (StandardRowHeight * _rowsVisible) - 41;

    private void SetWindowParameters()
    {
        SDL_GL_GetDrawableSize(_window, out var windowSizeW, out var windowSizeH);
        _logger.LogInformation($@"[Window] Drawable Size: {windowSizeW} x {windowSizeH}");
        _scale = windowSizeH / _windowHeight;
        _logger.LogInformation($@"[Window] Scale: {_scale}x");

        // Override things for a smooth transition.
        _gridTarget = _gridValue = GenerateTargetHeight();
    }

    // Setup all of the SDL resources we'll need to display a window.
    private void Setup()
    {
        if (SDL_Init(SDL_INIT_VIDEO) < 0)
        {
            throw new Exception($"There was an issue initializing SDL. {SDL_GetError()}");
        }

        _ = TTF_Init();
        _ = IMG_Init(IMG_InitFlags.IMG_INIT_PNG);

        _window = SDL_CreateWindow(
            "",
            SDL_WINDOWPOS_UNDEFINED,
            SDL_WINDOWPOS_UNDEFINED,
            _windowWidth,
            _windowHeight,
            SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI /* | SDL_WindowFlags.SDL_WINDOW_RESIZABLE */ );

        SetWindowParameters();

        if (_window == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the window. {SDL_GetError()}");
        }

        _renderer = SDL_CreateRenderer(
            _window,
            -1,
            SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

        if (_renderer == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the renderer. {SDL_GetError()}");
        }

        _ = SDL_SetRenderDrawBlendMode(_renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

        _openedTtfFont = TTF_OpenFont(_selectedFont.Filename, _selectedFont.PointSize * _scale);
        _fontSizeManager = new FontSizeManager(_openedTtfFont);

        var smoothing = _scale == 2 ? "_smooth" : string.Empty;
        var size = $"{_scale}x{smoothing}";

        // Load all the assets into the texture manager.
        _staticTextureManager = new TextureManager(_logger, size);

        var imageAssetDirectories = Directory.GetDirectories("assets/images");
        foreach (var imageAssetDirectory in imageAssetDirectories)
        {
            var assetSize = Path.GetFileName(imageAssetDirectory);
            foreach (var assetFile in Directory.GetFiles(imageAssetDirectory))
            {
                var noExtension = Path.GetFileNameWithoutExtension(assetFile);
                _staticTextureManager.Insert(noExtension, assetSize, new Texture(_logger, _renderer, assetFile));
            }
        }

        _timeboxFrameTexture = _staticTextureManager["timebox_frame"];
        _timeboxLastFrameTexture = _staticTextureManager["timebox_last_frame"];
        _channelFrameTexture = _staticTextureManager["channel_frame"];

        _timeboxFrameOneTime = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
        _nowBlock.ToString("h:mm tt"), _gridTextYellow, _scale));
        _timeboxFrameTwoTime = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
            _nowBlock.AddMinutes(30).ToString("h:mm tt"), _gridTextYellow, _scale));
        _timeboxFrameThreeTime = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
            _nowBlock.AddMinutes(60).ToString("h:mm tt"), _gridTextYellow, _scale));

        _clockFrameTexture = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, 144, 34, _clockBackgroundColor, _scale));
        _columnOneOrTwo = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, FirstColumnWidth, StandardRowHeight, _gridDefaultBlue, _scale));
        _columnThree = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, ThirdColumnWidth, StandardRowHeight, _gridDefaultBlue, _scale));
        _columnOneAndTwo = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, FirstColumnWidth * 2, StandardRowHeight, _gridDefaultBlue, _scale));
        _columnTwoAndThree = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, FirstColumnWidth + ThirdColumnWidth, StandardRowHeight, _gridDefaultBlue, _scale));
        _columnOneTwoAndThree = new Texture(Generators.GenerateFrame(_staticTextureManager, _renderer, (FirstColumnWidth * 2) + ThirdColumnWidth, StandardRowHeight, _gridDefaultBlue, _scale));
    }

    // Checks to see if there are any events to be processed.
    private void PollEvents()
    {
        if (_reloadGuideData)
        {
            _reloadGuideData = false;
            Task.Run(async () => await ReloadGuideData());
        }

        // Check to see if there are any events and continue to do so until the queue is empty.
        while (SDL_PollEvent(out var sdlEvent) == 1)
        {
            if (sdlEvent.type == SDL_EventType.SDL_QUIT)
                _running = false;
            else if (sdlEvent.type == SDL_EventType.SDL_WINDOWEVENT)
            {
                if (sdlEvent.window.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED)
                {
                    var resizedWindow = SDL_GetWindowFromID(sdlEvent.window.windowID);
                    SDL_GetWindowSize(resizedWindow, out _windowWidth, out _windowHeight);
                    SetWindowParameters();
                }

                // Interested in:
                // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST
                // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED
                // Would be nice to pause the renderer so it doesn't use 100% CPU when the window isn't focused
            }
            else if (sdlEvent.type == SDL_EventType.SDL_DROPFILE)
            {
                var filename = Marshal.PtrToStringAuto(sdlEvent.drop.file);

                if (!File.Exists(filename))
                    filename = Marshal.PtrToStringAnsi(sdlEvent.drop.file);

                if (File.Exists(filename))
                    Task.Run(() => ProcessXmlTvFile(filename).Wait());
                else
                    _logger.LogWarning("Unable to locate file {filename} for import", filename);
            }
            else if (sdlEvent.type == SDL_EventType.SDL_KEYDOWN)
            {
                switch (sdlEvent.key.keysym.sym)
                {
                    case SDL_Keycode.SDLK_f:
                        _showFrameRate = !_showFrameRate;
                        break;
                    case SDL_Keycode.SDLK_l:
                        _limitFps = !_limitFps;
                        break;
                    case SDL_Keycode.SDLK_q:
                        _running = false;
                        break;
                    case SDL_Keycode.SDLK_0:
                        _fullscreen = true;
                        _recalculateRowPositions = true;
                        break;
                    case SDL_Keycode.SDLK_1:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 1;
                        break;
                    case SDL_Keycode.SDLK_2:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 2;
                        break;
                    case SDL_Keycode.SDLK_3:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 3;
                        break;
                    case SDL_Keycode.SDLK_4:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 4;
                        break;
                    case SDL_Keycode.SDLK_5:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 5;
                        break;
                    case SDL_Keycode.SDLK_6:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 6;
                        break;
                    case SDL_Keycode.SDLK_7:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 7;
                        break;
                    case SDL_Keycode.SDLK_8:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 8;
                        break;
                    case SDL_Keycode.SDLK_9:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 9;
                        break;
                    case SDL_Keycode.SDLK_UP:
                        _recalculateRowPositions = true;
                        if (_fullscreen)
                            _fullscreen = false;
                        else
                            _rowsVisible++;
                        break;
                    case SDL_Keycode.SDLK_DOWN:
                        _recalculateRowPositions = true;
                        if (_fullscreen)
                            _fullscreen = false;
                        else
                        {
                            _rowsVisible--;
                            if (_rowsVisible < 1)
                                _rowsVisible = 1;
                        }
                        break;
                }
            }

            if (_recalculateRowPositions)
            {
                if (_fullscreen)
                {
                    _gridTarget = 0;
                }
                else
                {
                    _gridTarget = GenerateTargetHeight();
                }

                _recalculateRowPositions = false;
            }
        }
    }

    // Optimize the hell out of this.
    // - Call functions to generate rows as they're needed, and discard them when they're not.
    //   This means keeping track of the total size of the grid and our placement in it (i.e. what's being shown)
    // - Don't try to render everything in the channel list. Only render what's going to be visible.
    private IntPtr GenerateGridTexture()
    {
        _now = DateTime.Now;

        // Only update the time if the second has changed.
        if (_currentTimeToDisplay.Second != _now.Second)
        {
            SetBlockTimes();

            _currentTimeToDisplay = _now;

            _timeTexture?.Dispose();
            _timeTexture = new Texture(Generators.GenerateDropShadowText(_renderer, _openedTtfFont, _now.ToString("h:mm:ss"),
                _gridTextWhite, _scale));
        }

        if (_reloadGuideData)
        {
            _reloadGuideData = false;
            GenerateListingTextures();
        }

        var gridTexture = SDL_CreateTexture(_renderer, SDL_PIXELFORMAT_RGBA8888,
                       (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, _windowWidth * _scale,
                       _windowHeight * _scale);
        _ = SDL_SetTextureBlendMode(gridTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(_renderer, gridTexture))
        {
            // Blank out the grid texture with blue
            _ = SDL_SetRenderDrawColor(_renderer, 4, 0, 89, 255);
            _ = SDL_RenderClear(_renderer);

            const int frameX = 152; // Start first program column
            const int frameY = 206; // Start below frame.

            var horizontalOffset = 62;
            const int verticalOffset = 7;

            // Quick guess. 110 frames for a full grid push @ 112 (2x) / 56 (1x) height.
            // That means roughly 2 frames per pixel going up.
            _scrollingTest += 1;
            if (_scrollingTest >= StandardRowHeight * _channelsAdded * _scale)
                _scrollingTest = 0;
            var testingOffset = (_scrollingTest / 2);

            // Draw the channel frames.
            // foreach (var visibleGridRow in guide.GetVisibleGridRows())
            {
                for (var i = 0; i < _channelsToRender; i++)
                {
                    var channel = _channelLineUp.ElementAtOrDefault(i);
                    if (channel != null)
                    {
                        {
                            _ = SDL_QueryTexture(_channelFrameTexture.SdlTexture, out _, out _, out var w, out var h);
                            var dstRect1 = new SDL_Rect
                            {
                                h = h,
                                w = w,
                                x = 8 * _scale,
                                y = ((frameY - testingOffset + (i * StandardRowHeight)) * _scale)
                            };
                            _ = SDL_RenderCopy(_renderer, _channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect1);
                        }

                        if (_listingChannelTextureMap.ContainsKey(channel.Id))
                        {
                            var channelTextures = _listingChannelTextureMap[channel.Id];

                            _ = SDL_QueryTexture(channelTextures.Line1.SdlTexture, out _, out _, out var w1, out var h1);
                            var wOffset1 = ((90 - (w1 / _scale) / 2) + 8);
                            var dstRect1 = new SDL_Rect
                            {
                                h = h1,
                                w = w1,
                                x = (wOffset1 + _selectedFont.XOffset) * _scale,
                                y = ((frameY - testingOffset + (i * StandardRowHeight) + 5 + _selectedFont.YOffset) *
                                     _scale)
                            };
                            _ = SDL_RenderCopy(_renderer, channelTextures.Line1.SdlTexture, IntPtr.Zero, ref dstRect1);

                            _ = SDL_QueryTexture(channelTextures.Line2.SdlTexture, out _, out _, out var w2, out var h2);
                            var wOffset2 = ((90 - (w2 / _scale) / 2) + 8);
                            var dstRect2 = new SDL_Rect
                            {
                                h = h2,
                                w = w2,
                                x = (wOffset2 + _selectedFont.XOffset) * _scale,
                                y = ((frameY - testingOffset + (i * StandardRowHeight) + 29 + _selectedFont.YOffset) *
                                     _scale)
                            };
                            _ = SDL_RenderCopy(_renderer, channelTextures.Line2.SdlTexture, IntPtr.Zero, ref dstRect2);
                        }
                    }
                }
            }

            // Draw listings data.
            for (var i = 0; i < _channelsToRender; i++)
            {
                var channel = _channelLineUp.ElementAtOrDefault(i);
                if (channel != null)
                {
                    if (_listingTextTextureMap.ContainsKey(channel.Id))
                    {
                        var listingTextureMap = _listingTextTextureMap[channel.Id];

                        foreach (var listing in listingTextureMap)
                        {
                            Texture? frameTexture = listing.Frame;

                            var textLine1 = listing.Line1;
                            var textLine2 = listing.Line2;

                            _ = SDL_QueryTexture(frameTexture.SdlTexture, out _, out _, out var bfWidth, out var bfHeight);
                            var bfDstRect = new SDL_Rect
                            {
                                h = bfHeight, w = bfWidth, x = (frameX + listing.ColumnInfo.ColumnOffset) * _scale,
                                y = ((frameY - testingOffset + (i * StandardRowHeight)) * _scale)
                            };
                            _ = SDL_RenderCopy(_renderer, frameTexture.SdlTexture, IntPtr.Zero, ref bfDstRect);

                            var textLeftMargin = 0;

                            if (listing.StartTime < _nowBlock)
                            {
                                var arrowKey = (_nowBlock - listing.StartTime).TotalMinutes > 30
                                    ? Constants.GuideDoubleArrowLeft
                                    : Constants.GuideSingleArrowLeft;

                                var arrow = _staticTextureManager[arrowKey];

                                _ = SDL_QueryTexture(arrow.SdlTexture, out _, out _, out var arrowWidth,
                                    out var arrowHeight);
                                var arrowDstRect = new SDL_Rect
                                {
                                    h = arrowHeight, w = arrowWidth,
                                    x = (frameX + 5 + listing.ColumnInfo.ColumnOffset) * _scale,
                                    y = (frameY + 5 - testingOffset + (i * StandardRowHeight)) * _scale
                                };
                                _ = SDL_RenderCopy(_renderer, arrow.SdlTexture, IntPtr.Zero, ref arrowDstRect);

                                textLeftMargin = (arrowWidth / _scale);
                            }

                            if (listing.EndTime > _nowBlockEnd)
                            {
                                var arrowKey = (listing.EndTime - _nowBlockEnd).TotalMinutes > 30
                                    ? Constants.GuideDoubleArrowRight
                                    : Constants.GuideSingleArrowRight;

                                var arrow = _staticTextureManager[arrowKey];

                                _ = SDL_QueryTexture(arrow.SdlTexture, out _, out _, out var arrowWidth,
                                    out var arrowHeight);
                                var arrowDstRect = new SDL_Rect
                                {
                                    h = arrowHeight, w = arrowWidth,
                                    x = (frameX + 525) *
                                        _scale, // Calculate this from the frame width? I think I did the math wrong initially.
                                    y = (frameY + 5 - testingOffset + (i * StandardRowHeight)) * _scale
                                };
                                _ = SDL_RenderCopy(_renderer, arrow.SdlTexture, IntPtr.Zero, ref arrowDstRect);
                            }

                            _ = SDL_QueryTexture(textLine1.SdlTexture, out _, out _, out var bftWidth, out var bftHeight);
                            var bftDstRect = new SDL_Rect
                            {
                                h = bftHeight,
                                w = bftWidth,
                                x = (frameX + 5 + textLeftMargin + _selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                    _scale,
                                y = (frameY + 5 - testingOffset + (i * StandardRowHeight) + _selectedFont.YOffset) * _scale
                            };
                            _ = SDL_RenderCopy(_renderer, textLine1.SdlTexture, IntPtr.Zero, ref bftDstRect);

                            _ = SDL_QueryTexture(textLine2.SdlTexture, out _, out _, out var bftWidth2, out var bftHeight2);
                            var bftDstRect2 = new SDL_Rect
                            {
                                h = bftHeight2, w = bftWidth2,
                                x = (frameX + 5 + textLeftMargin + _selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                    _scale,
                                y = (frameY + 5 + 24 - testingOffset + (i * StandardRowHeight) + _selectedFont.YOffset) *
                                    _scale
                            };
                            _ = SDL_RenderCopy(_renderer, textLine2.SdlTexture, IntPtr.Zero, ref bftDstRect2);
                        }
                    }
                }
            }

            // Draw the clock frame.
            _ = SDL_QueryTexture(_clockFrameTexture.SdlTexture, out _, out _, out var clockFrameWidth, out var clockFrameHeight);
            var clockFrameDstRect = new SDL_Rect { h = clockFrameHeight, w = clockFrameWidth, x = 8 * _scale, y = 0 };
            _ = SDL_RenderCopy(_renderer, _clockFrameTexture.SdlTexture, IntPtr.Zero, ref clockFrameDstRect);

            // First two time boxes.
            {
                _ = SDL_QueryTexture(_timeboxFrameTexture.SdlTexture, out uint _, out int _, out int tbw, out int tbh);
                var timeFrameRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * _scale, y = 0 };
                _ = SDL_RenderCopy(_renderer, _timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect1);
                var timeFrameRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * _scale, y = 0 };
                _ = SDL_RenderCopy(_renderer, _timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect2);

                // Last one.
                _ = SDL_QueryTexture(_timeboxLastFrameTexture.SdlTexture, out _, out _, out var tblw, out var tblh);
                var timeFrameRect3 = new SDL_Rect { h = tblh, w = tblw, x = 496 * _scale, y = 0 };
                _ = SDL_RenderCopy(_renderer, _timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect3);
            }

            // Times.
            {
                _ = SDL_QueryTexture(_timeboxFrameOneTime.SdlTexture, out uint _, out int _, out int tw1, out int th1);
                var timeRect1 = new SDL_Rect
                {
                    h = th1,
                    w = tw1,
                    x = (192 + _selectedFont.XOffset) * _scale,
                    y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL_RenderCopy(_renderer, _timeboxFrameOneTime.SdlTexture, IntPtr.Zero, ref timeRect1);

                _ = SDL_QueryTexture(_timeboxFrameTwoTime.SdlTexture, out uint _, out int _, out int tw2, out int th2);
                var timeRect2 = new SDL_Rect
                {
                    h = th2,
                    w = tw2,
                    x = (364 + _selectedFont.XOffset) * _scale,
                    y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL_RenderCopy(_renderer, _timeboxFrameTwoTime.SdlTexture, IntPtr.Zero, ref timeRect2);

                _ = SDL_QueryTexture(_timeboxFrameThreeTime.SdlTexture, out uint _, out int _, out int tw3, out int th3);
                var timeRect3 = new SDL_Rect
                {
                    h = th3,
                    w = tw3,
                    x = (536 + _selectedFont.XOffset) * _scale,
                    y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL_RenderCopy(_renderer, _timeboxFrameThreeTime.SdlTexture, IntPtr.Zero, ref timeRect3);
            }

            if ((_now.Hour % 12) is 00 or >= 10)
                horizontalOffset -= 12;

            if (_timeTexture != null)
            {
                _ = SDL_QueryTexture(_timeTexture.SdlTexture, out _, out _, out var timeWidth, out var timeHeight);
                var timeDstRect = new SDL_Rect
                {
                    h = timeHeight,
                    w = timeWidth,
                    x = (horizontalOffset - 1 + _selectedFont.XOffset) * _scale,
                    y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL_RenderCopy(_renderer, _timeTexture.SdlTexture, IntPtr.Zero, ref timeDstRect);
            }
        }

        return gridTexture;
    }

    // Renders to the window.
    private void Render()
    {
        if (_regenerateGridTextures)
        {
            _regenerateGridTextures = false;
            GenerateListingTextures();
        }

        var frameDrawStopWatch = Stopwatch.StartNew();
        var frameDelayStopWatch = Stopwatch.StartNew();

        _ = SDL_SetRenderDrawColor(_renderer, 255, 0, 255, 255);
        _ = SDL_RenderClear(_renderer);

        // Generate the grid
        using var gridTexture = new Texture(GenerateGridTexture());

        if (_gridValue > _gridTarget)
            _gridValue -= 1;
        else if (_gridValue < _gridTarget)
            _gridValue += 1;

        // Render the grid.
        _ = SDL_QueryTexture(gridTexture.SdlTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
        var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = _gridValue * _scale};
        _ = SDL_RenderCopy(_renderer, gridTexture.SdlTexture, IntPtr.Zero, ref gridDstRect);

        // Draw FPS.
        if (_showFrameRate && frameTimeList.Any())
        {
            // Generate average FPS.
            var averageFrameTime = frameTimeList.Average();
            var averageFps = 1000 / averageFrameTime;

            var fpsTexture = Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
                $"FPS: {averageFps:F}", _gridTextYellow, _scale);

            _ = SDL_QueryTexture(fpsTexture, out _, out _, out var fpsTextureWidth, out var fpsTextureHeight);
            var fpsDstRect = new SDL_Rect { h = fpsTextureHeight, w = fpsTextureWidth, x = (_windowWidth - 180) * _scale, y = (6 * _scale) };
            _ = SDL_RenderCopy(_renderer, fpsTexture, IntPtr.Zero, ref fpsDstRect);
            SDL_DestroyTexture(fpsTexture);
        }

        // Switches out the currently presented render surface with the one we just did work on.
        SDL_RenderPresent(_renderer);

        frameDelayStopWatch.Stop();

        const int targetFps = 30;
        if (_limitFps)
        {
            const int targetDuration = 1000 / targetFps;
            var duration = (targetDuration - frameDelayStopWatch.ElapsedMilliseconds);

            if (duration > 0)
                SDL_Delay((uint)duration);
        }

        frameTimeList.Add(frameDrawStopWatch.ElapsedMilliseconds);

        while (frameTimeList.Count > NumberOfFrameTimesToCapture)
        {
            frameTimeList.RemoveAt(0);
        }
    }

    // Clean up the resources that were created.
    private void CleanUp()
    {
        _timeboxFrameOneTime?.Dispose();
        _timeboxFrameTwoTime?.Dispose();
        _timeboxFrameThreeTime?.Dispose();
        _timeTexture?.Dispose();
        _channelFrameTexture?.Dispose();
        _clockFrameTexture?.Dispose();
        _timeboxFrameTexture?.Dispose();
        _timeboxLastFrameTexture?.Dispose();

        _staticTextureManager.Dispose();

        foreach (var k in _listingTextTextureMap.Keys)
        {
            foreach (var subListing in _listingTextTextureMap[k])
            {
                subListing.Line1?.Dispose();
                subListing.Line2?.Dispose();
            }
        }

        foreach (var t in _listingChannelTextureMap.Keys)
        {
            _listingChannelTextureMap[t].Line1?.Dispose();
            _listingChannelTextureMap[t].Line2?.Dispose();
        }

        SDL_DestroyRenderer(_renderer);
        SDL_DestroyWindow(_window);

        _data.Dispose();

        TTF_CloseFont(_openedTtfFont);
        TTF_Quit();
        SDL_Quit();
    }
}
