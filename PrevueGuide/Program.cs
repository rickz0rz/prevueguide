
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using PrevueGuide;
using PrevueGuide.SDLWrappers;
using XmlTv.Model;
using static SDL2.SDL;
using static SDL2.SDL_image;
using static SDL2.SDL_ttf;

const int windowWidth = 716;
const int windowHeight = 436;

const int standardRowHeight = 56;
const int firstColumnWidth = 172;
const int secondColumnWidth = 172;
const int thirdColumnWidth = 208;

const string databaseFilename = "listings.db";

var frameTimeList = new List<long>();

var regenerateGridTextures = false;

var data = new PrevueGuide.Core.Data.SQLite.SQLiteListingsData(databaseFilename);

var channelLineUpStopwatch = Stopwatch.StartNew();
var channelLineUp = await data.GetChannelLineup();
Console.WriteLine($"Channel line-up loaded. {channelLineUp.Count()} channels found in {channelLineUpStopwatch.ElapsedMilliseconds} ms.");

/* Ultimately we want to find shows that have:
   - A start date before the beginning of the current 1/2 hour block AND an end date during or after the current visible times [left arrow]
   - A start date during the current visible times AND an end date after the current visible times [right arrow]
   - A combination of the above (before current 1/2 block AND after 1.5 visible hours) [both arrows]
   - A start time during the 1.5 hours visible and an end time during the 1.5 hours visible [no arrows]
   For start times, we will align on 15 minute increments. If a show starts after 4:15, count that as 4:30 but 4:15 and earlier is 4:00pm */

var channelListingsStopwatch = Stopwatch.StartNew();
var channelListings = await data.GetChannelListings(DateTime.Now.AddMinutes(-15), DateTime.Now.AddMinutes(105));
Console.WriteLine($"Channel listings loaded. {channelListings.Count()} listings found in {channelListingsStopwatch.ElapsedMilliseconds} ms.");

var fontConfigurationMap = new Dictionary<string, FontConfiguration>
{
    {
        "PrevueGrid", new FontConfiguration
        {
            Filename = "assets/PrevueGrid.ttf",
            PointSize = 25
        }
    },
    {
        "ab-preview", new FontConfiguration
        {
            Filename = "assets/ab-preview.ttf",
            PointSize = 23
        }
    },
    {
        "DIN Bold", new FontConfiguration // Hollywood
        {
            Filename = "/Users/rj/Library/Fonts/DINBd___.ttf",
            PointSize = 25
        }
    }
};

IntPtr window;
IntPtr renderer;
IntPtr openedTtfFont;

DateTime time = DateTime.UnixEpoch;

Texture? timeTexture = null;
Texture? channelFrameTexture = null;
Texture? clockFrameTexture = null;
Texture? timeboxFrameTexture = null;
Texture? timeboxLastFrameTexture = null;
Texture? guideDoubleArrowLeft = null;

Texture? bigFrameTexture = null;
Texture? bigFrameText1 = null;
Texture? bigFrameText2 = null;

const int numberOfFrameTimesToCapture = 60;

var scale = 1;
var running = true;
var showFrameRate = false;
var limitFps = false;

var gridTextYellow = new SDL_Color { a = 255, r = 203, g = 209, b = 0 };
var gridTextWhite = new SDL_Color { a = 255, r = 170, g = 170, b = 170 };
var clockBackgroundColor = new SDL_Color { a = 255, r = 34, g = 41, b = 141 };
var gridTestRed = new SDL_Color { a = 255, r = 192, g = 0, b = 0 };
var gridDefaultBlue = new SDL_Color { a = 255, r = 3, g = 0, b = 88 };

var gridOffset = 0;
var scrollingTest = 0;

var fontMap = new Dictionary<char, int>();

Setup();

while (running)
{
    PollEvents();
    Render();
}

CleanUp();

void GenerateBigText()
{
    Console.WriteLine("Generating texture:");

    var textLength = CalculateLineWidths(channelListings.FirstOrDefault()?.Title, firstColumnWidth,
        new Dictionary<int, int>());

    var firstLine =  channelListings.FirstOrDefault()?.Title ?? " ";
    var secondLine = " ";

    bigFrameText1 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, firstLine,
        gridTextWhite, scale));
    bigFrameText2 = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, secondLine,
        gridTextWhite, scale));
}

async Task ProcessXmlTvFile(string filename)
{
    try
    {
        Console.WriteLine($"Received file: {filename}");

        var xmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        await using var fileStream = new FileStream(filename, FileMode.Open);
        using var xmlReader = XmlReader.Create(fileStream, xmlReaderSettings);
        var tv = (Tv)new XmlSerializer(typeof(Tv)).Deserialize(xmlReader)!;

        Console.WriteLine("Importing channel listing data...");

        var numberOfChannels = 0;
        if (tv.Channel != null)
        {
            foreach (var channel in tv.Channel)
            {
                await data.AddChannelToLineup(channel.SourceName, channel.ChannelNumber, channel.CallSign);
                numberOfChannels++;
            }
        }
        Console.WriteLine($"Imported {numberOfChannels} channels.");

        var numberOfPrograms = 0;
        if (tv.Programme != null)
        {
            foreach (var programme in tv.Programme)
            {
                var title = programme.Title.First().Text;
                var description = programme.Desc.FirstOrDefault()?.Text ?? "";
                await data.AddChannelListing(programme.SourceName, title, description,
                    DateTime.ParseExact(programme.Start, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo, DateTimeStyles.AssumeLocal).ToUniversalTime(),
                    DateTime.ParseExact(programme.Stop, "yyyyMMddHHmmss zzz", DateTimeFormatInfo.CurrentInfo, DateTimeStyles.AssumeLocal).ToUniversalTime());
                numberOfPrograms++;
            }
        }
        Console.WriteLine($"Imported {numberOfPrograms} programs.");
        Console.WriteLine("Channel list imported.");

        regenerateGridTextures = true;
        Console.WriteLine("Prepared for regeneration.");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Encountered exception in rendering of XMLTV: {e.Message} @ {e.StackTrace}");
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
        var componentLength = component.ToCharArray().Sum(c => fontMap[c]);
        var paddedComponentLength = (string.IsNullOrWhiteSpace(currentLine) ? 0 : fontMap[' ']) + componentLength;

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
                    var glyphWidth = fontMap[targetChar];
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

// Setup all of the SDL resources we'll need to display a window.
void Setup()
{
    // Initializes SDL.
    if (SDL_Init(SDL_INIT_VIDEO) < 0)
    {
        Console.WriteLine($"There was an issue initializing SDL. {SDL_GetError()}");
    }

    _ = TTF_Init();
    _ = IMG_Init(IMG_InitFlags.IMG_INIT_PNG);

    // Create a new window given a title, size, and passes it a flag indicating it should be shown.
    window = SDL_CreateWindow(
        "",
        SDL_WINDOWPOS_UNDEFINED,
        SDL_WINDOWPOS_UNDEFINED,
        windowWidth,
        windowHeight,
        SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI);

    SDL_GL_GetDrawableSize(window, out var windowSizeW, out var windowSizeH);
    Console.WriteLine($"Drawable Size: {windowSizeW} x {windowSizeH}");
    scale = windowSizeH / windowHeight;
    Console.WriteLine($"Scale: {scale}x");

    if (window == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
    }

    // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
    renderer = SDL_CreateRenderer(
        window,
        -1,
        SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

    if (renderer == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
    }

    _ = SDL_SetRenderDrawBlendMode(renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    var font = fontConfigurationMap["PrevueGrid"];
    openedTtfFont = TTF_OpenFont(font.Filename, font.PointSize * scale);

    // Generate a font width map.
    for (var i = 0; i < 256; i++)
    {
        var c = (char)i;
        _ = TTF_SizeText(openedTtfFont, $"{c}", out var w, out _);
        fontMap[c] = w;
    }

    clockFrameTexture = new Texture(Generators.GenerateFrame(renderer, 144, 34, clockBackgroundColor, scale));
    timeboxFrameTexture = new Texture(renderer, "assets/timebox_frame_2x_smooth.png");
    timeboxLastFrameTexture = new Texture(renderer, "assets/timebox_last_frame_2x_smooth.png");
    channelFrameTexture = new Texture(renderer, "assets/channel_frame_2x_smooth.png");
    bigFrameTexture = new Texture(Generators.GenerateFrame(renderer, 172 * 2, standardRowHeight,
        gridDefaultBlue, scale));
    guideDoubleArrowLeft = new Texture(renderer, "assets/guide_double_arrow_left_2x_smooth.png");

    GenerateBigText();
}

// Checks to see if there are any events to be processed.
void PollEvents()
{
    // Check to see if there are any events and continue to do so until the queue is empty.
    while (SDL_PollEvent(out var sdlEvent) == 1)
    {
        if (sdlEvent.type == SDL_EventType.SDL_QUIT)
            running = false;
        else if (sdlEvent.type == SDL_EventType.SDL_DROPFILE)
        {
            var filename = Marshal.PtrToStringAuto(sdlEvent.drop.file);
            Task.Run(() => ProcessXmlTvFile(filename).Wait());
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
                case SDL_Keycode.SDLK_UP:
                    gridOffset -= (2 * scale);
                    if (gridOffset < 0)
                        gridOffset = 0;
                    break;
                case SDL_Keycode.SDLK_DOWN:
                    gridOffset += (2 * scale);
                    break;
            }
        }
    }
}

IntPtr GenerateGridTexture()
{
    // Cheap and cheating.
    var now = DateTime.Now;
    if (time.Second != now.Second)
    {
        time = now;

        timeTexture?.Dispose();
        timeTexture = new Texture(Generators.GenerateDropShadowText(renderer, openedTtfFont, now.ToString("h:mm:ss"),
            gridTextWhite, scale));
    }

    if (regenerateGridTextures)
    {
        GenerateBigText();
        regenerateGridTextures = false;
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
        if (scrollingTest >= standardRowHeight * 4 * scale)
            scrollingTest = 0;
        var testingOffset = (scrollingTest / 2);

        _ = SDL_QueryTexture(bigFrameTexture.SdlTexture, out _, out _, out var bfWidth, out var bfHeight);
        var bfDstRect = new SDL_Rect
            { h = bfHeight, w = bfWidth, x = frameX * scale, y = ((frameY - testingOffset) * scale) };
        _ = SDL_RenderCopy(renderer, bigFrameTexture.SdlTexture, IntPtr.Zero, ref bfDstRect);

        var useDoubleArrowLeft = true;
        var textLeftMargin = 0;

        if (useDoubleArrowLeft)
        {
            _ = SDL_QueryTexture(guideDoubleArrowLeft.SdlTexture, out _, out _, out var gdalWidth, out var gdalHeight);
            var gdalDstRect = new SDL_Rect
                { h = gdalHeight, w = gdalWidth, x = (frameX + 5) * scale, y = (frameY + 5 - testingOffset) * scale };
            _ = SDL_RenderCopy(renderer, guideDoubleArrowLeft.SdlTexture, IntPtr.Zero, ref gdalDstRect);
            textLeftMargin = (gdalWidth / scale);
        }

        _ = SDL_QueryTexture(bigFrameText1.SdlTexture, out _, out _, out var bftWidth, out var bftHeight);
        var bftDstRect = new SDL_Rect
        {
            h = bftHeight, w = bftWidth, x = (frameX + 5 + textLeftMargin) * scale,
            y = (frameY + 5 - testingOffset) * scale
        };
        _ = SDL_RenderCopy(renderer, bigFrameText1.SdlTexture, IntPtr.Zero, ref bftDstRect);

        _ = SDL_QueryTexture(bigFrameText2.SdlTexture, out _, out _, out var bftWidth2, out var bftHeight2);
        var bftDstRect2 = new SDL_Rect
        {
            h = bftHeight2, w = bftWidth2, x = (frameX + 5 + textLeftMargin) * scale,
            y = (frameY + 5 + 24 - testingOffset) * scale
        };
        _ = SDL_RenderCopy(renderer, bigFrameText2.SdlTexture, IntPtr.Zero, ref bftDstRect2);

        // Draw the clock frame.
        _ = SDL_QueryTexture(clockFrameTexture.SdlTexture, out _, out _, out var clockFrameWidth, out var clockFrameHeight);
        var clockFrameDstRect = new SDL_Rect { h = clockFrameHeight, w = clockFrameWidth, x = 8 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, clockFrameTexture.SdlTexture, IntPtr.Zero, ref clockFrameDstRect);

        // First two time boxes.
        _ = SDL_QueryTexture(timeboxFrameTexture.SdlTexture, out uint _, out int _, out int tbw, out int tbh);
        var timeRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeRect1);
        var timeRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref timeRect2);

        // Last one.
        _ = SDL_QueryTexture(timeboxLastFrameTexture.SdlTexture, out _, out _, out var tblw, out var tblh);
        var timeRect3 = new SDL_Rect { h = tblh, w = tblw, x = 496 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, ref timeRect3);

        if (DateTime.Now.Hour is 00 or >= 10)
            horizontalOffset -= 12;

        // Draw 3 channel frames.
        {
            const int baseChannelY = 34;
            _ = SDL_QueryTexture(channelFrameTexture.SdlTexture, out _, out _, out var w, out var h);
            var dstRect1 = new SDL_Rect
                { h = h, w = w, x = 8 * scale, y = baseChannelY * scale };
            _ = SDL_RenderCopy(renderer, channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect1);
            var dstRect2 = new SDL_Rect
                { h = h, w = w, x = 8 * scale, y = (baseChannelY + standardRowHeight) * scale };
            _ = SDL_RenderCopy(renderer, channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect2);
            var dstRect3 = new SDL_Rect
                { h = h, w = w, x = 8 * scale, y = (baseChannelY + (standardRowHeight * 2)) * scale };
            _ = SDL_RenderCopy(renderer, channelFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect3);
        }

        _ = SDL_QueryTexture(timeTexture.SdlTexture, out _, out _, out var timeWidth, out var timeHeight);
        var timeDstRect = new SDL_Rect
            { h = timeHeight, w = timeWidth, x = (horizontalOffset - 1) * scale, y = (verticalOffset - 1) * scale };
        _ = SDL_RenderCopy(renderer, timeTexture.SdlTexture, IntPtr.Zero, ref timeDstRect);
    }

    return gridTexture;
}

// Renders to the window.
void Render()
{
    var frameDrawStopWatch = Stopwatch.StartNew();
    var frameDelayStopWatch = Stopwatch.StartNew();

    _ = SDL_SetRenderDrawColor(renderer, 255, 0, 255, 255);
    _ = SDL_RenderClear(renderer);

    // Generate the grid
    using var gridTexture = new Texture(GenerateGridTexture());

    // Render the grid.
    _ = SDL_QueryTexture(gridTexture.SdlTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
    var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = 227 * scale + gridOffset };
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
    timeTexture?.Dispose();
    channelFrameTexture?.Dispose();
    clockFrameTexture?.Dispose();
    timeboxFrameTexture?.Dispose();
    timeboxLastFrameTexture?.Dispose();
    guideDoubleArrowLeft?.Dispose();

    bigFrameTexture?.Dispose();
    bigFrameText1?.Dispose();
    bigFrameText2?.Dispose();

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
