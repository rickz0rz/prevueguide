using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.Data;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using PrevueGuide.Core.Utilities;
using PrevueGuide.Model;
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
// If an object is no longer going to be visible (passes over the top threshold of the frame) it's evicted
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
    private const int NumberOfFrameTimesToCapture = 60;
    private const int MaximumChannelsToRender = Int32.MaxValue;

    private bool _reloadGuideData = true;
    private bool _regenerateGridTextures;
    private int _channelsToRender = MaximumChannelsToRender;
    private int _channelsAdded;
    private readonly List<long> _frameTimeList = [];
    private int _rowsVisible = 3;
    private bool _fullscreen;
    private int _windowWidth = 716;
    private int _windowHeight = 436;

    // Set this to the beginning of computer time so we can force it to update.
    private DateTime _currentTimeToDisplay = DateTime.UnixEpoch;
    private DateTime _now = DateTime.Now;
    private DateTime _nowBlock;
    private DateTime _nowBlockEnd;

    private readonly IListingsDataProvider _dataProvider;
    private readonly List<LineUpEntry> _channelLineUp = new();
    private readonly List<Listing> _channelListings = new();

    // These could use some serious love.
    private readonly Dictionary<string, (Texture? Line1, Texture? Line2)> _listingChannelTextureMap = new();
    private readonly Dictionary<string, List<((int ColumnNumber, int ColumnOffset) ColumnInfo, Texture? Frame, Texture? Line1,
        Texture? Line2, int Block, DateTime StartTime, DateTime EndTime)>> _listingTextTextureMap = new();

    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _openedTtfFont;

    private FontSizeManager? _fontSizeManager;
    private TextureManager? _staticTextureManager;
    private readonly FontConfiguration _selectedFont;

    private Texture? _timeTexture;
    private Texture? _channelFrameTexture, _clockFrameTexture, _timeboxFrameTexture, _timeboxLastFrameTexture;
    private Texture? _timeboxFrameOneTime, _timeboxFrameTwoTime, _timeboxFrameThreeTime;
    private Texture? _columnOneOrTwo, _columnThree, _columnOneAndTwo, _columnTwoAndThree, _columnOneTwoAndThree;

    private readonly ILogger _logger;

    private int _scale;
    private bool _running = true;
    private bool _showFrameRate;
    private bool _limitFps;

    private readonly SDL3.SDL.Color _gridTextYellow = new() { A = 255, R = 203, G = 209, B = 0 };
    private readonly SDL3.SDL.Color _gridTextWhite = new() { A = 255, R = 170, G = 170, B = 170 };
    private readonly SDL3.SDL.Color _clockBackgroundColor = new() { A = 255, R = 34, G = 41, B = 141 };
    private readonly SDL3.SDL.Color _gridDefaultBlue = new() { A = 255, R = 3, G = 0, B = 88 };
    // gridTestRed = { A = 255, R = 192, G = 0, B = 0 };

    private bool _recalculateRowPositions = true;
    private int _gridTarget;
    private int _gridValue;
    private int _scrollingTest;

    public Guide(ILogger logger)
    {
        _logger = logger;

        try
        {
            _dataProvider = new Core.Data.SQLite.SQLiteListingsDataProvider(_logger, "listings.db");
        }
        catch (Exception e)
        {
            logger.LogDebug(e.Message);
        }

        if (_dataProvider == null)
        {
            try
            {
                _dataProvider = new Core.Data.ChannelsDVR.ChannelsDVRListingsDataProvider("http://192.168.0.119:8089");
            }
            catch (Exception e)
            {
                logger.LogDebug(e.Message);
            }
        }

        if (_dataProvider == null)
        {
            logger.LogInformation("Unable to initialize data provider, falling back to in-memory data provider.");
            _dataProvider = new Core.Data.LocalMemory.LocalMemoryListingsDataProvider();
        }

        var fontConfigurationMap =
            JsonSerializer.Deserialize<Dictionary<string, FontConfiguration>>(File.ReadAllText("assets/fonts/fonts.json"));

        if (fontConfigurationMap == null || !fontConfigurationMap.ContainsKey("PrevueGrid"))
        {
            throw new Exception("Unable to find PrevueGrid font configuration.");
        }

        _selectedFont = fontConfigurationMap["PrevueGrid"];

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
            var channels = await _dataProvider.GetChannelLineup();
            _channelLineUp.Clear();
            _channelLineUp.AddRange(channels);
            _logger.LogInformation("[Guide] Channel line-up loaded. {channelLineUpCount} channels found in " +
                                   "{loadTimeMilliseconds} ms.",
                _channelLineUp.Count,
                channelLineUpStopwatch.ElapsedMilliseconds);

            _channelsToRender = new[] { _channelLineUp.Count, MaximumChannelsToRender }.Min();

            var channelListingsStopwatch = Stopwatch.StartNew();
            var listings = await _dataProvider.GetChannelListings(_nowBlock, _nowBlockEnd);
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

                _ = SDL3.SDL.GetTextureSize(frameTexture.SdlTexture, out var frameWidth, out _);

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
                Font.FormatWithFontTokens(channel.CallSign),
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
                    await _dataProvider.AddChannelToLineup(channel.SourceName, channel.ChannelNumber, channel.CallSign);
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
                    var title = programme.Title?.First().Text ?? "Missing Title";
                    var description = programme.Desc?.FirstOrDefault()?.Text ?? "Missing Description";
                    var category = programme.Category?.FirstOrDefault()?.Text ?? "Missing Category";
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

                    await _dataProvider.AddChannelListing(list);
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

    private IEnumerable<string> CalculateLineWidths(string targetString, float defaultLineWidth, Dictionary<int, int> specifiedLineWidths)
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
        SDL3.SDL.GetWindowSizeInPixels(_window, out var windowSizeW, out var windowSizeH);
        _logger.LogInformation($@"[Window] Drawable Size: {windowSizeW} x {windowSizeH}");
        _scale = windowSizeH / _windowHeight;
        _logger.LogInformation($@"[Window] Scale: {_scale}x");

        // Override things for a smooth transition.
        _gridTarget = _gridValue = GenerateTargetHeight();
    }

    // Setup all the SDL resources we'll need to display a window.
    private void Setup()
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
        {
            throw new Exception($"There was an issue initializing SDL. {SDL3.SDL.GetError()}");
        }

        _ = SDL3.TTF.Init();

        _window = SDL3.SDL.CreateWindow("Prevue Guide",
            _windowWidth,
            _windowHeight,
            SDL3.SDL.WindowFlags.HighPixelDensity /* | SDL3.SDL.WindowFlags.Resizable */);

        SetWindowParameters();

        if (_window == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the window. {SDL3.SDL.GetError()}");
        }

        _renderer = SDL3.SDL.CreateRenderer(
            _window,
            "");

        SDL3.SDL.SetRenderVSync(_renderer, 1);

        if (_renderer == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the renderer. {SDL3.SDL.GetError()}");
        }

        _ = SDL3.SDL.SetRenderDrawBlendMode(_renderer, SDL3.SDL.BlendMode.Blend);

        _openedTtfFont = SDL3.TTF.OpenFont(_selectedFont.Filename, _selectedFont.PointSize * _scale);
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
        while (SDL3.SDL.PollEvent(out var sdlEvent))
        {
            if (sdlEvent.Type == (uint)SDL3.SDL.EventType.Quit)
                _running = false;
            else if (sdlEvent.Type == (uint)SDL3.SDL.EventType.WindowResized)
            {
                var resizedWindow = SDL3.SDL.GetWindowFromID(sdlEvent.Window.WindowID);
                SDL3.SDL.GetWindowSizeInPixels(resizedWindow, out _windowWidth, out _windowHeight);
                SetWindowParameters();

                // Interested in:
                // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST
                // SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED
                // Would be nice to pause the renderer, so it doesn't use 100% CPU when the window isn't focused
            }
            else if (sdlEvent.Type == (uint)SDL3.SDL.EventType.DropFile)
            {
                if (!_dataProvider.RequiresManualUpdating)
                {
                    _logger.LogInformation("Provider doesn't require manual updating, ignoring.");
                    continue;
                }

                var filename = Marshal.PtrToStringAuto(sdlEvent.Drop.Data);

                if (!File.Exists(filename))
                    filename = Marshal.PtrToStringAnsi(sdlEvent.Drop.Data);

                if (File.Exists(filename))
                    Task.Run(() => ProcessXmlTvFile(filename).Wait());
                else
                    _logger.LogWarning("Unable to locate file {filename} for import", filename);
            }
            else if (sdlEvent.Type == (uint)SDL3.SDL.EventType.KeyDown)
            {
                switch (sdlEvent.Key.Key)
                {
                    case SDL3.SDL.Keycode.F:
                        _showFrameRate = !_showFrameRate;
                        break;
                    case SDL3.SDL.Keycode.L:
                        _limitFps = !_limitFps;
                        break;
                    case SDL3.SDL.Keycode.Q:
                        _running = false;
                        break;
                    case SDL3.SDL.Keycode.Alpha0:
                        _fullscreen = true;
                        _recalculateRowPositions = true;
                        break;
                    case SDL3.SDL.Keycode.Alpha1:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 1;
                        break;
                    case SDL3.SDL.Keycode.Alpha2:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 2;
                        break;
                    case SDL3.SDL.Keycode.Alpha3:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 3;
                        break;
                    case SDL3.SDL.Keycode.Alpha4:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 4;
                        break;
                    case SDL3.SDL.Keycode.Alpha5:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 5;
                        break;
                    case SDL3.SDL.Keycode.Alpha6:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 6;
                        break;
                    case SDL3.SDL.Keycode.Alpha7:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 7;
                        break;
                    case SDL3.SDL.Keycode.Alpha8:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 8;
                        break;
                    case SDL3.SDL.Keycode.Alpha9:
                        _fullscreen = false;
                        _recalculateRowPositions = true;
                        _rowsVisible = 9;
                        break;
                    case SDL3.SDL.Keycode.Up:
                        _recalculateRowPositions = true;
                        if (_fullscreen)
                            _fullscreen = false;
                        else
                            _rowsVisible++;
                        break;
                    case SDL3.SDL.Keycode.Down:
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

            if (!_recalculateRowPositions)
                continue;

            _gridTarget = _fullscreen ? 0 : GenerateTargetHeight();
            _recalculateRowPositions = false;
        }
    }

    // Optimize the hell out of this.
    // - Call functions to generate rows as they're needed, and discard them when they're not.
    //   This means keeping calculating the total size of the grid and our placement in it (i.e., what's being shown)
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

        var gridTexture = SDL3.SDL.CreateTexture(_renderer, SDL3.SDL.PixelFormat.RGBA8888,
            SDL3.SDL.TextureAccess.Target, _windowWidth * _scale, _windowHeight * _scale);
        _ = SDL3.SDL.SetTextureBlendMode(gridTexture, SDL3.SDL.BlendMode.Blend);

        // Switch to the texture for rendering.
        using (_ = new RenderingTarget(_renderer, gridTexture))
        {
            // Blank out the grid texture with blue
            _ = SDL3.SDL.SetRenderDrawColor(_renderer, 4, 0, 89, 255);
            _ = SDL3.SDL.RenderClear(_renderer);

            const int frameX = 152; // Start first program column
            const int frameY = 206; // Start below frame.

            var horizontalOffset = 62;
            const int verticalOffset = 7;

            // Quick guess. 110 frames for a full grid push @ 112 (2x) / 56 (1x) pixels in height.
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
                            _ = SDL3.SDL.GetTextureSize(_channelFrameTexture.SdlTexture, out var w, out var h);
                            var dstRect1 = new SDL3.SDL.FRect
                            {
                                H = h,
                                W = w,
                                X = 8 * _scale,
                                Y = ((frameY - testingOffset + (i * StandardRowHeight)) * _scale)
                            };
                            SDL3.SDL.RenderTexture(_renderer, _channelFrameTexture.SdlTexture, IntPtr.Zero, in dstRect1);
                        }

                        if (_listingChannelTextureMap.ContainsKey(channel.Id))
                        {
                            var channelTextures = _listingChannelTextureMap[channel.Id];

                            _ = SDL3.SDL.GetTextureSize(channelTextures.Line1.SdlTexture, out var w1, out var h1);
                            var wOffset1 = ((90 - (w1 / _scale) / 2) + 8);
                            var dstRect1 = new SDL3.SDL.FRect
                            {
                                H = h1,
                                W = w1,
                                X = (wOffset1 + _selectedFont.XOffset) * _scale,
                                Y = ((frameY - testingOffset + (i * StandardRowHeight) + 5 + _selectedFont.YOffset) *
                                     _scale)
                            };
                            _ = SDL3.SDL.RenderTexture(_renderer, channelTextures.Line1.SdlTexture, IntPtr.Zero, in dstRect1);

                            _ = SDL3.SDL.GetTextureSize(channelTextures.Line2.SdlTexture, out var w2, out var h2);
                            var wOffset2 = ((90 - (w2 / _scale) / 2) + 8);
                            var dstRect2 = new SDL3.SDL.FRect
                            {
                                H = h2,
                                W = w2,
                                X = (wOffset2 + _selectedFont.XOffset) * _scale,
                                Y = ((frameY - testingOffset + (i * StandardRowHeight) + 29 + _selectedFont.YOffset) *
                                     _scale)
                            };
                            _ = SDL3.SDL.RenderTexture(_renderer, channelTextures.Line2.SdlTexture, IntPtr.Zero, in dstRect2);
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

                            _ = SDL3.SDL.GetTextureSize(frameTexture.SdlTexture, out var bfWidth, out var bfHeight);
                            var bfDstRect = new SDL3.SDL.FRect
                            {
                                H = bfHeight, W = bfWidth, X = (frameX + listing.ColumnInfo.ColumnOffset) * _scale,
                                Y = ((frameY - testingOffset + (i * StandardRowHeight)) * _scale)
                            };
                            _ = SDL3.SDL.RenderTexture(_renderer, frameTexture.SdlTexture, IntPtr.Zero, in bfDstRect);

                            var textLeftMargin = 0f;

                            if (listing.StartTime < _nowBlock)
                            {
                                var arrowKey = (_nowBlock - listing.StartTime).TotalMinutes > 30
                                    ? Constants.GuideDoubleArrowLeft
                                    : Constants.GuideSingleArrowLeft;

                                var arrow = _staticTextureManager[arrowKey];

                                _ = SDL3.SDL.GetTextureSize(arrow.SdlTexture, out var arrowWidth,
                                    out var arrowHeight);
                                var arrowDstRect = new SDL3.SDL.FRect
                                {
                                    H = arrowHeight, W = arrowWidth,
                                    X = (frameX + 5 + listing.ColumnInfo.ColumnOffset) * _scale,
                                    Y = (frameY + 5 - testingOffset + (i * StandardRowHeight)) * _scale
                                };
                                _ = SDL3.SDL.RenderTexture(_renderer, arrow.SdlTexture, IntPtr.Zero, in arrowDstRect);

                                textLeftMargin = (arrowWidth / _scale);
                            }

                            if (listing.EndTime > _nowBlockEnd)
                            {
                                var arrowKey = (listing.EndTime - _nowBlockEnd).TotalMinutes > 30
                                    ? Constants.GuideDoubleArrowRight
                                    : Constants.GuideSingleArrowRight;

                                var arrow = _staticTextureManager[arrowKey];

                                _ = SDL3.SDL.GetTextureSize(arrow.SdlTexture, out var arrowWidth,
                                    out var arrowHeight);
                                var arrowDstRect = new SDL3.SDL.FRect
                                {
                                    H = arrowHeight, W = arrowWidth,
                                    X = (frameX + 525) *
                                        _scale, // Calculate this from the frame width? I think I did the math wrong initially.
                                    Y = (frameY + 5 - testingOffset + (i * StandardRowHeight)) * _scale
                                };
                                _ = SDL3.SDL.RenderTexture(_renderer, arrow.SdlTexture, IntPtr.Zero, in arrowDstRect);
                            }

                            _ = SDL3.SDL.GetTextureSize(textLine1.SdlTexture, out var bftWidth, out var bftHeight);
                            var bftDstRect = new SDL3.SDL.FRect
                            {
                                H = bftHeight,
                                W = bftWidth,
                                X = (frameX + 5 + textLeftMargin + _selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                    _scale,
                                Y = (frameY + 5 - testingOffset + (i * StandardRowHeight) + _selectedFont.YOffset) * _scale
                            };
                            _ = SDL3.SDL.RenderTexture(_renderer, textLine1.SdlTexture, IntPtr.Zero, in bftDstRect);

                            _ = SDL3.SDL.GetTextureSize(textLine2.SdlTexture, out var bftWidth2, out var bftHeight2);
                            var bftDstRect2 = new SDL3.SDL.FRect
                            {
                                H = bftHeight2, W = bftWidth2,
                                X = (frameX + 5 + textLeftMargin + _selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                    _scale,
                                Y = (frameY + 5 + 24 - testingOffset + (i * StandardRowHeight) + _selectedFont.YOffset) *
                                    _scale
                            };
                            _ = SDL3.SDL.RenderTexture(_renderer, textLine2.SdlTexture, IntPtr.Zero, in bftDstRect2);
                        }
                    }
                }
            }

            // Draw the clock frame.
            _ = SDL3.SDL.GetTextureSize(_clockFrameTexture.SdlTexture, out var clockFrameWidth, out var clockFrameHeight);
            var clockFrameDstRect = new SDL3.SDL.FRect { H = clockFrameHeight, W = clockFrameWidth, X = 8 * _scale, Y = 0 };
            _ = SDL3.SDL.RenderTexture(_renderer, _clockFrameTexture.SdlTexture, IntPtr.Zero, in clockFrameDstRect);

            // First two time boxes.
            {
                _ = SDL3.SDL.GetTextureSize(_timeboxFrameTexture.SdlTexture, out var tbw, out var tbh);
                var timeFrameRect1 = new SDL3.SDL.FRect { H = tbh, W = tbw, X = 152 * _scale, Y = 0 };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxFrameTexture.SdlTexture, IntPtr.Zero, in timeFrameRect1);
                var timeFrameRect2 = new SDL3.SDL.FRect { H = tbh, W = tbw, X = 324 * _scale, Y = 0 };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxFrameTexture.SdlTexture, IntPtr.Zero, in timeFrameRect2);

                // Last one.
                _ = SDL3.SDL.GetTextureSize(_timeboxLastFrameTexture.SdlTexture, out var tblw, out var tblh);
                var timeFrameRect3 = new SDL3.SDL.FRect { H = tblh, W = tblw, X = 496 * _scale, Y = 0 };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, in timeFrameRect3);
            }

            // Times.
            {
                _ = SDL3.SDL.GetTextureSize(_timeboxFrameOneTime.SdlTexture, out var tw1, out var th1);
                var timeRect1 = new SDL3.SDL.FRect
                {
                    H = th1,
                    W = tw1,
                    X = (192 + _selectedFont.XOffset) * _scale,
                    Y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxFrameOneTime.SdlTexture, IntPtr.Zero, in timeRect1);

                _ = SDL3.SDL.GetTextureSize(_timeboxFrameTwoTime.SdlTexture, out var tw2, out var th2);
                var timeRect2 = new SDL3.SDL.FRect
                {
                    H = th2,
                    W = tw2,
                    X = (364 + _selectedFont.XOffset) * _scale,
                    Y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxFrameTwoTime.SdlTexture, IntPtr.Zero, in timeRect2);

                _ = SDL3.SDL.GetTextureSize(_timeboxFrameThreeTime.SdlTexture, out var tw3, out var th3);
                var timeRect3 = new SDL3.SDL.FRect
                {
                    H = th3,
                    W = tw3,
                    X = (536 + _selectedFont.XOffset) * _scale,
                    Y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeboxFrameThreeTime.SdlTexture, IntPtr.Zero, in timeRect3);
            }

            if ((_now.Hour % 12) is 00 or >= 10)
                horizontalOffset -= 12;

            if (_timeTexture != null)
            {
                _ = SDL3.SDL.GetTextureSize(_timeTexture.SdlTexture, out var timeWidth, out var timeHeight);
                var timeDstRect = new SDL3.SDL.FRect
                {
                    H = timeHeight,
                    W = timeWidth,
                    X = (horizontalOffset - 1 + _selectedFont.XOffset) * _scale,
                    Y = (verticalOffset - 1 + _selectedFont.YOffset) * _scale
                };
                _ = SDL3.SDL.RenderTexture(_renderer, _timeTexture.SdlTexture, IntPtr.Zero, in timeDstRect);
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

        _ = SDL3.SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        _ = SDL3.SDL.RenderClear(_renderer);

        // Generate the grid
        using var gridTexture = new Texture(GenerateGridTexture());

        if (_gridValue > _gridTarget)
            _gridValue -= 1;
        else if (_gridValue < _gridTarget)
            _gridValue += 1;

        // Render the grid.
        _ = SDL3.SDL.GetTextureSize(gridTexture.SdlTexture, out var gridTextureWidth, out var gridTextureHeight);
        var gridDstRect = new SDL3.SDL.FRect { H = gridTextureHeight, W = gridTextureWidth, X = 0, Y = _gridValue * _scale};
        _ = SDL3.SDL.RenderTexture(_renderer, gridTexture.SdlTexture, IntPtr.Zero, in gridDstRect);

        // Draw FPS.
        if (_showFrameRate && _frameTimeList.Any())
        {
            // Generate average FPS.
            var averageFrameTime = _frameTimeList.Average();
            var averageFps = 1000 / averageFrameTime;

            var fpsTexture = Generators.GenerateDropShadowText(_renderer, _openedTtfFont,
                $"FPS: {averageFps:F}", _gridTextYellow, _scale);

            _ = SDL3.SDL.GetTextureSize(fpsTexture, out var fpsTextureWidth, out var fpsTextureHeight);
            var fpsDstRect = new SDL3.SDL.FRect { H = fpsTextureHeight, W = fpsTextureWidth, X = (_windowWidth - 180) * _scale, Y = (6 * _scale) };
            _ = SDL3.SDL.RenderTexture(_renderer, fpsTexture, IntPtr.Zero, in fpsDstRect);
            SDL3.SDL.DestroyTexture(fpsTexture);
        }

        // Switches out the currently presented render surface with the one we just did work on.
        SDL3.SDL.RenderPresent(_renderer);

        frameDelayStopWatch.Stop();

        const int targetFps = 30;
        if (_limitFps)
        {
            const int targetDuration = 1000 / targetFps;
            var duration = (targetDuration - frameDelayStopWatch.ElapsedMilliseconds);

            if (duration > 0)
                SDL3.SDL.Delay((uint)duration);
        }

        _frameTimeList.Add(frameDrawStopWatch.ElapsedMilliseconds);

        while (_frameTimeList.Count > NumberOfFrameTimesToCapture)
        {
            _frameTimeList.RemoveAt(0);
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

        SDL3.SDL.DestroyRenderer(_renderer);
        SDL3.SDL.DestroyWindow(_window);

        _dataProvider.Dispose();

        SDL3.TTF.CloseFont(_openedTtfFont);
        SDL3.TTF.Quit();
        SDL3.SDL.Quit();
    }
}
