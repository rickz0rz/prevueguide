
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using PrevueGuide;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using PrevueGuide.Core.Utilities;
using XmlTv.Model;
using static SDL2.SDL;
using static SDL2.SDL_image;
using static SDL2.SDL_ttf;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddFilter("System", LogLevel.Warning)
        .AddFilter("LoggingConsoleApp.Program", LogLevel.Debug)
        .AddConsole();
});

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

var logger = loggerFactory.CreateLogger<Program>();

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
    {
        logger.LogCritical("Unhandled exception encountered: {message} @ {stackTrace}",
            exception.Message, exception.StackTrace);
    }
    else
    {
        logger.LogCritical("Unhandled exception encountered: {exceptionObject}", eventArgs.ExceptionObject);
    }

    // Cheap but force the logger to flush.
    if (eventArgs.IsTerminating)
        // ReSharper disable once AccessToDisposedClosure
        loggerFactory?.Dispose();
};

int windowWidth = 716;
int windowHeight = 436;

const int standardRowHeight = 56;
const int standardColumnWidth = 172;
const int firstColumnWidth = standardColumnWidth;
const int secondColumnWidth = standardColumnWidth;
const int thirdColumnWidth = standardColumnWidth + 36; // For rendering the frame, it's this but when generating
                                                       // contents in the 3rd column, use the standard column width.
const int singleArrowWidth = 16;
const int doubleArrowWidth = 24;

var backgroundColor = new { Red = (byte)255, Green = (byte)0, Blue = (byte)255 };

const string databaseFilename = "listings.db";

const int numberOfFrameTimesToCapture = 60;
int standardGridOffset;

var frameTimeList = new List<long>();

var reloadGuideData = true;
var regenerateGridTextures = false;
var channelsToRender = 10;
var channelsAdded = 0;

var rowsVisible = 3;
var fullscreen = false;

// Set this to the beginning of computer time so we can force it to update.
var currentTimeToDisplay = DateTime.UnixEpoch;

DateTime now = DateTime.Now;
DateTime nowBlock;
DateTime nowBlockEnd;

SetBlockTimes();

var data = new PrevueGuide.Core.Data.SQLite.SQLiteListingsData(logger, databaseFilename);
var channelLineUp = new List<LineUpEntry>();
var channelListings = new List<Listing>();

var fontConfigurationMap =
    JsonSerializer.Deserialize<Dictionary<string, FontConfiguration>>(File.ReadAllText("assets/fonts/fonts.json"));
var selectedFont = fontConfigurationMap?["PrevueGrid"];

IntPtr window;
IntPtr renderer;
IntPtr openedTtfFont;

FontSizeManager fontSizeManager;
TextureManager staticTextureManager;

Texture? timeTexture = null;
Texture? channelFrameTexture, clockFrameTexture, timeboxFrameTexture, timeboxLastFrameTexture;
Texture? timeboxFrameOneTime, timeboxFrameTwoTime, timeboxFrameThreeTime;
Texture? columnOneOrTwo, columnThree, columnOneAndTwo, columnTwoAndThree, columnOneTwoAndThree;

// These could use some serious love.
var listingChannelTextureMap = new Dictionary<string, (Texture? Line1, Texture? Line2)>();
var listingTextTextureMap = new Dictionary<string, List<((int ColumnNumber, int ColumnOffset) ColumnInfo, Texture? Frame, Texture? Line1, Texture? Line2, int Block, DateTime StartTime, DateTime EndTime)>>();

int scale;
var running = true;
var showFrameRate = false;
var limitFps = false;

var gridTextYellow = new SDL_Color { a = 255, r = 203, g = 209, b = 0 };
var gridTextWhite = new SDL_Color { a = 255, r = 170, g = 170, b = 170 };
var clockBackgroundColor = new SDL_Color { a = 255, r = 34, g = 41, b = 141 };
// var gridTestRed = new SDL_Color { a = 255, r = 192, g = 0, b = 0 };
var gridDefaultBlue = new SDL_Color { a = 255, r = 3, g = 0, b = 88 };

var recalculateRowPositions = true;
var gridTarget = 0;
var gridValue = 0;
var scrollingTest = 0;

Setup();

while (running)
{
    PollEvents();
    Render();
}

CleanUp();

void SetBlockTimes()
{
    // logger.LogInformation("[Clock] Setting now to {time}", now);
    nowBlock = Time.ClampToPreviousHalfHour(now);
    nowBlockEnd = nowBlock.AddMinutes(90);
    // logger.LogInformation("[Clock] Time blocks set to {nowBlock} to {nowBlockEnd}", nowBlock, nowBlockEnd);
}

async Task ReloadGuideData()
{
    try
    {
        var channelLineUpStopwatch = Stopwatch.StartNew();
        var channels = await data.GetChannelLineup();
        channelLineUp.Clear();
        channelLineUp.AddRange(channels);
        logger.LogInformation("[Guide] Channel line-up loaded. {channelLineUpCount} channels found in " +
                              "{loadTimeMilliseconds} ms.",
            channelLineUp.Count,
            channelLineUpStopwatch.ElapsedMilliseconds);

        // hack
        channelsToRender = new[] { channelLineUp.Count, channelsToRender }.Min();

        var channelListingsStopwatch = Stopwatch.StartNew();
        var listings = await data.GetChannelListings(nowBlock, nowBlockEnd);
        channelListings.Clear();
        channelListings.AddRange(listings);

        logger.LogInformation($@"[Guide] Channel listings loaded. {channelListings.Count()} listings found " +
                              $"in {channelListingsStopwatch.ElapsedMilliseconds} ms.");

        regenerateGridTextures = true;
    }
    catch (Exception e)
    {
        logger.LogError(e, "Exception when reloading guide data");
    }
}

void GenerateListingTextures()
{
    logger.LogInformation("[Textures] Removing old listing textures");
    foreach (var k in listingTextTextureMap.Keys)
    {
        listingChannelTextureMap[k].Line1?.Dispose();
        listingChannelTextureMap[k].Line2?.Dispose();
        listingChannelTextureMap.Remove(k);
    }

    logger.LogInformation("[Textures] Generating new listing textures");
    for (var i = 0; i < channelsToRender + 5; i++)
    {
        var channel = channelLineUp.ElementAtOrDefault(i);
        if (channel != null)
        {
            var listings = channelListings.Where(cl => cl.ChannelId == channel.Id);

            var listingList = new List<((int ColumnNumber, int ColumnOffset) ColumnInfo, Texture? Frame, Texture? Line1,
                Texture? Line2, int Block, DateTime StartTime, DateTime EndTime)>();

            foreach (var listing in listings)
            {
                var columnInfo = UI.CalculateColumnDetails(listing.Block,
                    firstColumnWidth, secondColumnWidth);

                var remainingDuration = listing.StartTime > nowBlock
                    ? listing.EndTime - listing.StartTime
                    : listing.EndTime - nowBlock;

                Texture? frameTexture = null;

                frameTexture = remainingDuration.TotalMinutes switch
                {
                    > 60 => columnInfo.column switch
                    {
                        1 => columnOneTwoAndThree,
                        2 => columnTwoAndThree,
                        3 => columnThree,
                        _ => frameTexture
                    },
                    > 30 => columnInfo.column switch
                    {
                        1 => columnOneAndTwo,
                        2 => columnTwoAndThree,
                        3 => columnThree,
                        _ => frameTexture
                    },
                    _ => columnInfo.column switch
                    {
                        1 => columnOneOrTwo,
                        2 => columnOneOrTwo,
                        3 => columnThree,
                        _ => frameTexture
                    }
                };

                if (frameTexture == null)
                    continue;

                _ = SDL_QueryTexture(frameTexture.SdlTexture, out _, out _, out var frameWidth, out _);

                // hack: make all the columns align.
                frameWidth -= (frameWidth % standardColumnWidth);

                if (listing.StartTime < nowBlock)
                {
                    if ((nowBlock - listing.StartTime).TotalMinutes > 30)
                        frameWidth -= (doubleArrowWidth * scale);
                    else
                        frameWidth -= (singleArrowWidth * scale);
                }

                if (listing.EndTime > nowBlockEnd)
                {
                    if ((listing.EndTime - nowBlock).TotalMinutes > 30)
                        frameWidth -= (doubleArrowWidth * scale);
                    else
                        frameWidth -= (singleArrowWidth * scale);
                }

                // The frame has the bevel so take that into account.
                // The bevel is 4 pixels on each side, so that * 2, scaled.
                frameWidth -= (8 * scale);

                var prevueFontMap = new Dictionary<string, string>
                {
                    // { "G": "" }
                    { "NR", "\uF008"},
                    { "G", "\uF009"},
                    { "PG", "\uF00A"},
                    { "PG-13", "\uF00B"},
                    { "NC-17", "\uF00C"},
                    { "R", "\uF00D"},
                    { "ADULT", "\uF00E"},
                    { "TV-Y", "\uF00F"},
                    { "TV-Y7", "\uF010"},
                    { "TV-G", "\uF011"},
                    { "TV-PG", "\uF012"},
                    { "TV-14", "\uF013"},
                    { "TV-M", "\uF014"},
                    { "TV-MA", "\uF015"},
                    { "CC", "\uF01C"},
                };

                var isPrevueFont = true;

                var listingRating = "";
                var listingSubtitled = "";

                if (!string.IsNullOrWhiteSpace(listing.Rating))
                {
                    listingRating = isPrevueFont
                        ? $" {prevueFontMap[listing.Rating]}"
                        : $" {listing.Rating}";
                }

                if (!string.IsNullOrWhiteSpace(listing.Subtitled))
                {
                    listingSubtitled = isPrevueFont
                        ? $" {prevueFontMap[listing.Subtitled]}"
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
                var line1 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, firstLine,
                    gridTextWhite, scale));

                var secondLine = lines.ElementAtOrDefault(1);
                if (string.IsNullOrWhiteSpace(secondLine))
                    secondLine = " ";
                var line2 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, secondLine,
                    gridTextWhite, scale));

                listingList.Add((columnInfo, frameTexture, line1, line2, listing.Block, listing.StartTime,
                    listing.EndTime));
            }

            if (listingTextTextureMap.ContainsKey(channel.Id))
            {
                logger.LogWarning("Attempted to add channel {callSign} ({id}) but already exists, ignoring.",
                    channel.CallSign, channel.Id);
                continue;
            }

            listingTextTextureMap.Add(channel.Id, listingList);

            var channelLine1 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
                channel.ChannelNumber,
                gridTextYellow, scale));
            var channelLine2 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
                channel.CallSign,
                gridTextYellow, scale));

            listingChannelTextureMap.Add(channel.Id, (channelLine1, channelLine2));
            channelsAdded++;
        }
    }

    logger.LogInformation(("[Textures] Texture generation complete"));
}

async Task ProcessXmlTvFile(string filename)
{
    try
    {
        logger.LogInformation("[Processor] Received file {filename}", filename);

        var xmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        await using var fileStream = new FileStream(filename, FileMode.Open);
        using var xmlReader = XmlReader.Create(fileStream, xmlReaderSettings);
        var tv = (Tv)new XmlSerializer(typeof(Tv)).Deserialize(xmlReader)!;

        logger.LogInformation("[Processor] Importing guide data...");

        var channelStopWatch = Stopwatch.StartNew();
        var numberOfChannels = 0;
        if (tv.Channel != null)
        {
            foreach (var channel in tv.Channel)
            {
                await data.AddChannelToLineup(channel.SourceName, channel.ChannelNumber, channel.CallSign);
                numberOfChannels++;
            }
        }
        logger.LogInformation("[Processor] Imported {numberOfChannels} channels in {elapsedTime}.",
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
                var subtitled = programme.Subtitles.Type ?? "";

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

                await data.AddChannelListing(list);
            }
        }

        logger.LogInformation("[Processor] Imported {numberOfPrograms} programs in {elapsedTime}.",
            numberOfPrograms, listingStopWatch.Elapsed);
        logger.LogInformation("[Processor] Guide data imported.");

        reloadGuideData = true;
        logger.LogInformation("[Processor] Prepared for regeneration.");
    }
    catch (Exception e)
    {
        logger.LogError("[Processor] Encountered exception in rendering of XMLTV: {message} @ {stackTrace}",
            e.Message, e.StackTrace);
    }
}

IEnumerable<string> CalculateLineWidths(string targetString, int defaultLineWidth, Dictionary<int, int> specifiedLineWidths)
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
        var componentLength = component.ToCharArray().Select(c => fontSizeManager[$"{c}"]).Sum(v => v.width);
        var paddedComponentLength = (string.IsNullOrWhiteSpace(currentLine) ? 0 : fontSizeManager[' '].width) + componentLength;

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
                    var glyphWidth = fontSizeManager[targetChar].width;
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

int GenerateTargetHeight() => windowHeight - (standardRowHeight * rowsVisible) - 41;

void SetWindowParameters()
{
    SDL_GL_GetDrawableSize(window, out var windowSizeW, out var windowSizeH);
    logger.LogInformation($@"[Window] Drawable Size: {windowSizeW} x {windowSizeH}");
    scale = windowSizeH / windowHeight;
    logger.LogInformation($@"[Window] Scale: {scale}x");

    // Override things for a smooth transition.
    gridTarget = gridValue = GenerateTargetHeight();
}

// Setup all of the SDL resources we'll need to display a window.
void Setup()
{
    if (SDL_Init(SDL_INIT_VIDEO) < 0)
    {
        throw new Exception($"There was an issue initializing SDL. {SDL_GetError()}");
    }

    _ = TTF_Init();
    _ = IMG_Init(IMG_InitFlags.IMG_INIT_PNG);

    window = SDL_CreateWindow(
        "",
        SDL_WINDOWPOS_UNDEFINED,
        SDL_WINDOWPOS_UNDEFINED,
        windowWidth,
        windowHeight,
        SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI /* | SDL_WindowFlags.SDL_WINDOW_RESIZABLE */ );

    SetWindowParameters();

    if (window == IntPtr.Zero)
    {
        throw new Exception($"There was an issue creating the window. {SDL_GetError()}");
    }

    renderer = SDL_CreateRenderer(
        window,
        -1,
        SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

    if (renderer == IntPtr.Zero)
    {
        throw new Exception($"There was an issue creating the renderer. {SDL_GetError()}");
    }

    _ = SDL_SetRenderDrawBlendMode(renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    openedTtfFont = TTF_OpenFont(selectedFont.Filename, selectedFont.PointSize * scale);
    fontSizeManager = new FontSizeManager(openedTtfFont);

    var smoothing = scale == 2 ? "_smooth" : string.Empty;
    var size = $"{scale}x{smoothing}";

    // Load all the assets into the texture manager.
    staticTextureManager = new TextureManager(logger, size);

    var imageAssetDirectories = Directory.GetDirectories("assets/images");
    foreach (var imageAssetDirectory in imageAssetDirectories)
    {
        var assetSize = Path.GetFileName(imageAssetDirectory);
        foreach (var assetFile in Directory.GetFiles(imageAssetDirectory))
        {
            var noExtension = Path.GetFileNameWithoutExtension(assetFile);
            staticTextureManager.Insert(noExtension, assetSize, new Texture(logger, renderer, assetFile));
        }
    }

    timeboxFrameTexture = staticTextureManager["timebox_frame"];
    timeboxLastFrameTexture = staticTextureManager["timebox_last_frame"];
    channelFrameTexture = staticTextureManager["channel_frame"];

    timeboxFrameOneTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
    nowBlock.ToString("h:mm tt"), gridTextYellow, scale));
    timeboxFrameTwoTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
        nowBlock.AddMinutes(30).ToString("h:mm tt"), gridTextYellow, scale));
    timeboxFrameThreeTime = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont,
        nowBlock.AddMinutes(60).ToString("h:mm tt"), gridTextYellow, scale));

    clockFrameTexture = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, 144, 34, clockBackgroundColor, scale));
    columnOneOrTwo = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, firstColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnThree = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnOneAndTwo = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, firstColumnWidth * 2, standardRowHeight, gridDefaultBlue, scale));
    columnTwoAndThree = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, firstColumnWidth + thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));
    columnOneTwoAndThree = new Texture(Generators.GenerateFrame(staticTextureManager, renderer, (firstColumnWidth * 2) + thirdColumnWidth, standardRowHeight, gridDefaultBlue, scale));
}

// Checks to see if there are any events to be processed.
void PollEvents()
{
    if (reloadGuideData)
    {
        reloadGuideData = false;
        Task.Run(async () => await ReloadGuideData());
    }

    // Check to see if there are any events and continue to do so until the queue is empty.
    while (SDL_PollEvent(out var sdlEvent) == 1)
    {
        if (sdlEvent.type == SDL_EventType.SDL_QUIT)
            running = false;
        else if (sdlEvent.type == SDL_EventType.SDL_WINDOWEVENT)
        {
            if (sdlEvent.window.windowEvent == SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED)
            {
                var resizedWindow = SDL_GetWindowFromID(sdlEvent.window.windowID);
                SDL_GetWindowSize(resizedWindow, out windowWidth, out windowHeight);
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
                logger.LogWarning("Unable to locate file {filename} for import", filename);
        }
        else if (sdlEvent.type == SDL_EventType.SDL_KEYDOWN)
        {
            switch (sdlEvent.key.keysym.sym)
            {
                case SDL_Keycode.SDLK_f:
                    showFrameRate = !showFrameRate;
                    break;
                case SDL_Keycode.SDLK_l:
                    limitFps = !limitFps;
                    break;
                case SDL_Keycode.SDLK_q:
                    running = false;
                    break;
                case SDL_Keycode.SDLK_0:
                    fullscreen = true;
                    recalculateRowPositions = true;
                    break;
                case SDL_Keycode.SDLK_1:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 1;
                    break;
                case SDL_Keycode.SDLK_2:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 2;
                    break;
                case SDL_Keycode.SDLK_3:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 3;
                    break;
                case SDL_Keycode.SDLK_4:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 4;
                    break;
                case SDL_Keycode.SDLK_5:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 5;
                    break;
                case SDL_Keycode.SDLK_6:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 6;
                    break;
                case SDL_Keycode.SDLK_7:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 7;
                    break;
                case SDL_Keycode.SDLK_8:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 8;
                    break;
                case SDL_Keycode.SDLK_9:
                    fullscreen = false;
                    recalculateRowPositions = true;
                    rowsVisible = 9;
                    break;
                case SDL_Keycode.SDLK_UP:
                    recalculateRowPositions = true;
                    if (fullscreen)
                        fullscreen = false;
                    else
                        rowsVisible++;
                    break;
                case SDL_Keycode.SDLK_DOWN:
                    recalculateRowPositions = true;
                    if (fullscreen)
                        fullscreen = false;
                    else
                    {
                        rowsVisible--;
                        if (rowsVisible < 1)
                            rowsVisible = 1;
                    }
                    break;
            }
        }

        if (recalculateRowPositions)
        {
            if (fullscreen)
            {
                gridTarget = 0;
            }
            else
            {
                gridTarget = GenerateTargetHeight();
            }

            recalculateRowPositions = false;
        }
    }
}

// Optimize the hell out of this.
// - Call functions to generate rows as they're needed, and discard them when they're not.
//   This means keeping track of the total size of the grid and our placement in it (i.e. what's being shown)
// - Don't try to render everything in the channel list. Only render what's going to be visible.
IntPtr GenerateGridTexture()
{
    now = DateTime.Now;

    // Only update the time if the second has changed.
    if (currentTimeToDisplay.Second != now.Second)
    {
        SetBlockTimes();

        currentTimeToDisplay = now;

        timeTexture?.Dispose();
        timeTexture = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, now.ToString("h:mm:ss"),
            gridTextWhite, scale));
    }

    if (reloadGuideData)
    {
        reloadGuideData = false;
        GenerateListingTextures();
    }

    var gridTexture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGBA8888,
                   (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, windowWidth * scale,
                   windowHeight * scale);
    _ = SDL_SetTextureBlendMode(gridTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    // Switch to the texture for rendering.
    using (_ = new RenderingTarget(renderer, gridTexture))
    {
        // Blank out the grid texture with blue
        _ = SDL_SetRenderDrawColor(renderer, 4, 0, 89, 255);
        _ = SDL_RenderClear(renderer);

        const int frameX = 152; // Start first program column
        const int frameY = 206; // Start below frame.

        var horizontalOffset = 62;
        const int verticalOffset = 7;

        // Quick guess. 110 frames for a full grid push @ 112 (2x) / 56 (1x) height.
        // That means roughly 2 frames per pixel going up.
        scrollingTest += 1;
        if (scrollingTest >= standardRowHeight * channelsAdded * scale)
            scrollingTest = 0;
        var testingOffset = (scrollingTest / 2);

        // Draw the channel frames.
        {
            for (var i = 0; i < channelsToRender; i++)
            {
                var channel = channelLineUp.ElementAtOrDefault(i);
                if (channel != null)
                {
                    {
                        _ = SDL_QueryTexture(channelFrameTexture.SdlTexture, out _, out _, out var w, out var h);
                        var dstRect1 = new SDL_Rect
                        {
                            h = h,
                            w = w,
                            x = 8 * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight)) * scale)
                        };
                        _ = SDL_RenderCopy(renderer, channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect1);
                    }

                    if (listingChannelTextureMap.ContainsKey(channel.Id))
                    {
                        var channelTextures = listingChannelTextureMap[channel.Id];

                        _ = SDL_QueryTexture(channelTextures.Line1.SdlTexture, out _, out _, out var w1, out var h1);
                        var wOffset1 = ((90 - (w1 / scale) / 2) + 8);
                        var dstRect1 = new SDL_Rect
                        {
                            h = h1,
                            w = w1,
                            x = (wOffset1 + selectedFont.XOffset) * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight) + 5 + selectedFont.YOffset) *
                                 scale)
                        };
                        _ = SDL_RenderCopy(renderer, channelTextures.Line1.SdlTexture, IntPtr.Zero, ref dstRect1);

                        _ = SDL_QueryTexture(channelTextures.Line2.SdlTexture, out _, out _, out var w2, out var h2);
                        var wOffset2 = ((90 - (w2 / scale) / 2) + 8);
                        var dstRect2 = new SDL_Rect
                        {
                            h = h2,
                            w = w2,
                            x = (wOffset2 + selectedFont.XOffset) * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight) + 29 + selectedFont.YOffset) *
                                 scale)
                        };
                        _ = SDL_RenderCopy(renderer, channelTextures.Line2.SdlTexture, IntPtr.Zero, ref dstRect2);
                    }
                }
            }
        }

        // Draw listings data.
        for (var i = 0; i < channelsToRender; i++)
        {
            var channel = channelLineUp.ElementAtOrDefault(i);
            if (channel != null)
            {
                if (listingTextTextureMap.ContainsKey(channel.Id))
                {
                    var listingTextureMap = listingTextTextureMap[channel.Id];

                    foreach (var listing in listingTextureMap)
                    {
                        Texture? frameTexture = listing.Frame;

                        var textLine1 = listing.Line1;
                        var textLine2 = listing.Line2;

                        _ = SDL_QueryTexture(frameTexture.SdlTexture, out _, out _, out var bfWidth, out var bfHeight);
                        var bfDstRect = new SDL_Rect
                        {
                            h = bfHeight, w = bfWidth, x = (frameX + listing.ColumnInfo.ColumnOffset) * scale,
                            y = ((frameY - testingOffset + (i * standardRowHeight)) * scale)
                        };
                        _ = SDL_RenderCopy(renderer, frameTexture.SdlTexture, IntPtr.Zero, ref bfDstRect);

                        var textLeftMargin = 0;

                        if (listing.StartTime < nowBlock)
                        {
                            var arrowKey = (nowBlock - listing.StartTime).TotalMinutes > 30
                                ? Constants.GuideDoubleArrowLeft
                                : Constants.GuideSingleArrowLeft;

                            var arrow = staticTextureManager[arrowKey];

                            _ = SDL_QueryTexture(arrow.SdlTexture, out _, out _, out var arrowWidth,
                                out var arrowHeight);
                            var arrowDstRect = new SDL_Rect
                            {
                                h = arrowHeight, w = arrowWidth,
                                x = (frameX + 5 + listing.ColumnInfo.ColumnOffset) * scale,
                                y = (frameY + 5 - testingOffset + (i * standardRowHeight)) * scale
                            };
                            _ = SDL_RenderCopy(renderer, arrow.SdlTexture, IntPtr.Zero, ref arrowDstRect);

                            textLeftMargin = (arrowWidth / scale);
                        }

                        if (listing.EndTime > nowBlockEnd)
                        {
                            var arrowKey = (listing.EndTime - nowBlockEnd).TotalMinutes > 30
                                ? Constants.GuideDoubleArrowRight
                                : Constants.GuideSingleArrowRight;

                            var arrow = staticTextureManager[arrowKey];

                            _ = SDL_QueryTexture(arrow.SdlTexture, out _, out _, out var arrowWidth,
                                out var arrowHeight);
                            var arrowDstRect = new SDL_Rect
                            {
                                h = arrowHeight, w = arrowWidth,
                                x = (frameX + 525) *
                                    scale, // Calculate this from the frame width? I think I did the math wrong initially.
                                y = (frameY + 5 - testingOffset + (i * standardRowHeight)) * scale
                            };
                            _ = SDL_RenderCopy(renderer, arrow.SdlTexture, IntPtr.Zero, ref arrowDstRect);
                        }

                        _ = SDL_QueryTexture(textLine1.SdlTexture, out _, out _, out var bftWidth, out var bftHeight);
                        var bftDstRect = new SDL_Rect
                        {
                            h = bftHeight,
                            w = bftWidth,
                            x = (frameX + 5 + textLeftMargin + selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                scale,
                            y = (frameY + 5 - testingOffset + (i * standardRowHeight) + selectedFont.YOffset) * scale
                        };
                        _ = SDL_RenderCopy(renderer, textLine1.SdlTexture, IntPtr.Zero, ref bftDstRect);

                        _ = SDL_QueryTexture(textLine2.SdlTexture, out _, out _, out var bftWidth2, out var bftHeight2);
                        var bftDstRect2 = new SDL_Rect
                        {
                            h = bftHeight2, w = bftWidth2,
                            x = (frameX + 5 + textLeftMargin + selectedFont.XOffset + listing.ColumnInfo.ColumnOffset) *
                                scale,
                            y = (frameY + 5 + 24 - testingOffset + (i * standardRowHeight) + selectedFont.YOffset) *
                                scale
                        };
                        _ = SDL_RenderCopy(renderer, textLine2.SdlTexture, IntPtr.Zero, ref bftDstRect2);
                    }
                }
            }
        }

        // Draw the clock frame.
        _ = SDL_QueryTexture(clockFrameTexture.SdlTexture, out _, out _, out var clockFrameWidth, out var clockFrameHeight);
        var clockFrameDstRect = new SDL_Rect { h = clockFrameHeight, w = clockFrameWidth, x = 8 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, clockFrameTexture.SdlTexture, IntPtr.Zero, ref clockFrameDstRect);

        // First two time boxes.
        {
            _ = SDL_QueryTexture(timeboxFrameTexture.SdlTexture, out uint _, out int _, out int tbw, out int tbh);
            var timeFrameRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect1);
            var timeFrameRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect2);

            // Last one.
            _ = SDL_QueryTexture(timeboxLastFrameTexture.SdlTexture, out _, out _, out var tblw, out var tblh);
            var timeFrameRect3 = new SDL_Rect { h = tblh, w = tblw, x = 496 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, ref timeFrameRect3);
        }

        // Times.
        {
            _ = SDL_QueryTexture(timeboxFrameOneTime.SdlTexture, out uint _, out int _, out int tw1, out int th1);
            var timeRect1 = new SDL_Rect
            {
                h = th1,
                w = tw1,
                x = (192 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameOneTime.SdlTexture, IntPtr.Zero, ref timeRect1);

            _ = SDL_QueryTexture(timeboxFrameTwoTime.SdlTexture, out uint _, out int _, out int tw2, out int th2);
            var timeRect2 = new SDL_Rect
            {
                h = th2,
                w = tw2,
                x = (364 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameTwoTime.SdlTexture, IntPtr.Zero, ref timeRect2);

            _ = SDL_QueryTexture(timeboxFrameThreeTime.SdlTexture, out uint _, out int _, out int tw3, out int th3);
            var timeRect3 = new SDL_Rect
            {
                h = th3,
                w = tw3,
                x = (536 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeboxFrameThreeTime.SdlTexture, IntPtr.Zero, ref timeRect3);
        }

        if ((now.Hour % 12) is 00 or >= 10)
            horizontalOffset -= 12;

        if (timeTexture != null)
        {
            _ = SDL_QueryTexture(timeTexture.SdlTexture, out _, out _, out var timeWidth, out var timeHeight);
            var timeDstRect = new SDL_Rect
            {
                h = timeHeight,
                w = timeWidth,
                x = (horizontalOffset - 1 + selectedFont.XOffset) * scale,
                y = (verticalOffset - 1 + selectedFont.YOffset) * scale
            };
            _ = SDL_RenderCopy(renderer, timeTexture.SdlTexture, IntPtr.Zero, ref timeDstRect);
        }
    }

    return gridTexture;
}

// Renders to the window.
void Render()
{
    if (regenerateGridTextures)
    {
        regenerateGridTextures = false;
        GenerateListingTextures();
    }

    var frameDrawStopWatch = Stopwatch.StartNew();
    var frameDelayStopWatch = Stopwatch.StartNew();

    _ = SDL_SetRenderDrawColor(renderer, backgroundColor.Red, backgroundColor.Green, backgroundColor.Blue, 255);
    _ = SDL_RenderClear(renderer);

    // Generate the grid
    using var gridTexture = new Texture(GenerateGridTexture());

    if (gridValue > gridTarget)
        gridValue -= 1;
    else if (gridValue < gridTarget)
        gridValue += 1;

    // Render the grid.
    _ = SDL_QueryTexture(gridTexture.SdlTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
    var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = gridValue * scale};
    _ = SDL_RenderCopy(renderer, gridTexture.SdlTexture, IntPtr.Zero, ref gridDstRect);

    // Draw FPS.
    if (showFrameRate && frameTimeList.Any())
    {
        // Generate average FPS.
        var averageFrameTime = frameTimeList.Average();
        var averageFps = 1000 / averageFrameTime;

        var fpsTexture = Generators.GenerateDropShadowText(renderer, openedTtfFont,
            $"FPS: {averageFps:F}", gridTextYellow, scale);

        _ = SDL_QueryTexture(fpsTexture, out _, out _, out var fpsTextureWidth, out var fpsTextureHeight);
        var fpsDstRect = new SDL_Rect { h = fpsTextureHeight, w = fpsTextureWidth, x = (windowWidth - 180) * scale, y = (6 * scale) };
        _ = SDL_RenderCopy(renderer, fpsTexture, IntPtr.Zero, ref fpsDstRect);
        SDL_DestroyTexture(fpsTexture);
    }

    // Switches out the currently presented render surface with the one we just did work on.
    SDL_RenderPresent(renderer);

    frameDelayStopWatch.Stop();

    const int targetFps = 30;
    if (limitFps)
    {
        const int targetDuration = 1000 / targetFps;
        var duration = (targetDuration - frameDelayStopWatch.ElapsedMilliseconds);

        if (duration > 0)
            SDL_Delay((uint)duration);
    }

    frameTimeList.Add(frameDrawStopWatch.ElapsedMilliseconds);

    while (frameTimeList.Count > numberOfFrameTimesToCapture)
    {
        frameTimeList.RemoveAt(0);
    }
}

// Clean up the resources that were created.
void CleanUp()
{
    timeboxFrameOneTime.Dispose();
    timeboxFrameTwoTime.Dispose();
    timeboxFrameThreeTime.Dispose();
    timeTexture?.Dispose();
    channelFrameTexture?.Dispose();
    clockFrameTexture?.Dispose();
    timeboxFrameTexture?.Dispose();
    timeboxLastFrameTexture?.Dispose();

    staticTextureManager.Dispose();

    foreach (var k in listingTextTextureMap.Keys)
    {
        foreach (var subListing in listingTextTextureMap[k])
        {
            subListing.Line1?.Dispose();
            subListing.Line2?.Dispose();
        }
    }

    foreach (var t in listingChannelTextureMap.Keys)
    {
        listingChannelTextureMap[t].Line1?.Dispose();
        listingChannelTextureMap[t].Line2?.Dispose();
    }

    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);

    data.Dispose();

    TTF_CloseFont(openedTtfFont);
    TTF_Quit();
    SDL_Quit();
}

record Channel
{
    public string ChannelNumber { get; init; }
    public string CallSign { get; init; }
}

record FontConfiguration
{
    public string Filename { get; init; }
    public int PointSize { get; init; }
    public int XOffset { get; init; } = 0;
    public int YOffset { get; init; } = 0;
}
