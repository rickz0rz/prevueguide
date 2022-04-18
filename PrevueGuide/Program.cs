using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using PrevueGuide;
using PrevueGuide.SDLWrappers;
using SDL2;
using XmlTv.Model;
using static SDL2.SDL;
using static SDL2.SDL_image;
using static SDL2.SDL_ttf;

const int windowWidth = 716;
const int windowHeight = 436;

const int standardRowHeight = 56;

var frameTimeList = new List<long>();

var regenerateGridTextures = false;

var listings = new Dictionary<Channel, List<string>>();
listings.Add(new Channel { CallSign = "PREVUE", ChannelNumber = "1" },
    new List<string> { "Prevue Guide", "Before you view, Prevue!" });

IntPtr window;
IntPtr renderer;
IntPtr prevueGrid;

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

var scale = 2;
var running = true;

var gridTextYellow = new SDL_Color { a = 255, r = 203, g = 209, b = 0 };
var gridTextWhite = new SDL_Color { a = 255, r = 170, g = 170, b = 170 };
var clockBackgroundColor = new SDL_Color { a = 255, r = 34, g = 41, b = 141 };
var gridTestRed = new SDL_Color { a = 255, r = 192, g = 0, b = 0 };
var gridDefaultBlue = new SDL_Color { a = 255, r = 3, g = 0, b = 88 };

var gridOffset = 0;
var scrollingTest = 0;

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

    var firstListing = listings.First().Value;

    var firstLine = firstListing.Count >= 1 ? firstListing[0] : " ";
    Console.WriteLine($"//| {firstLine}");

    bigFrameText1 = new Texture(Generators.GenerateDropShadowText(renderer, prevueGrid, firstLine,
        gridTextWhite, scale));

    var secondLine = firstListing.Count >= 2 ? firstListing[1] : " ";
    Console.WriteLine($"\\\\| {secondLine}");

    bigFrameText2 = new Texture(Generators.GenerateDropShadowText(renderer, prevueGrid, secondLine,
        gridTextWhite, scale));
}

void ProcessXmlTvFile(string filename)
{
    try
    {
        Console.WriteLine($"Received file: {filename}");

        var xmlReaderSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        using var fileStream = new FileStream(filename, FileMode.Open);
        using var xmlReader = XmlReader.Create(fileStream, xmlReaderSettings);
        var tv = (Tv)new XmlSerializer(typeof(Tv)).Deserialize(xmlReader)!;

        var firstTvChannel = tv.Channel.First();
        var firstTvChannelProgram = tv.Programme.First(p => p.Channel == firstTvChannel.Id);

        listings.Clear();
        listings.Add(new Channel
            {
                CallSign = firstTvChannel.CallSign,
                ChannelNumber = firstTvChannel.ChannelNumber
            },
            new List<string> { firstTvChannelProgram.Title.First().Text });

        regenerateGridTextures = true;
        Console.WriteLine("Prepared for regeneration.");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Encountered exception in rendering of XMLTV: {e.Message} @ {e.StackTrace}");
    }
}

// Setup all of the SDL resources we'll need to display a window.
void Setup()
{
    // Initializes SDL.
    if (SDL_Init(SDL_INIT_VIDEO) < 0)
    {
        Console.WriteLine($"There was an issue initializing SDL. {SDL_GetError()}");
    }

    // SDL_SetHint(SDL_HINT_RENDER_DRIVER, "opengl");

    TTF_Init();
    IMG_Init(IMG_InitFlags.IMG_INIT_PNG);

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

    prevueGrid = TTF_OpenFont("assets/PrevueGrid.ttf", 25 * scale);

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
            Task.Run(() => ProcessXmlTvFile(filename));
        }
        else if (sdlEvent.type == SDL_EventType.SDL_KEYDOWN)
        {
            switch (sdlEvent.key.keysym.sym)
            {
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
        timeTexture = new Texture(Generators.GenerateDropShadowText(renderer, prevueGrid, now.ToString("h:mm:ss"),
            gridTextWhite, scale));
    }

    if (regenerateGridTextures)
    {
        Console.WriteLine("Regenerating grid textures.");
        GenerateBigText();
        regenerateGridTextures = false;
        Console.WriteLine("Regenerating grid textures completed.");
    }

    // Render the entirety of the grid to its own texture,
    // that way we can properly cut off content from the upper panel.
    // Also allows us to shift the grid up and down by simply changing the texture Y
    // by a few rows down.

    var gridTexture = SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGBA8888,
                   (int)SDL_TextureAccess.SDL_TEXTUREACCESS_TARGET, windowWidth * scale,
                   windowHeight * scale);

    _ = SDL_SetTextureBlendMode(gridTexture, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);

    // Switch to the texture for rendering.
    using (_ = new RenderingTarget(renderer, gridTexture))
    {
        // Blank out the grid texture with blue
        _ = SDL_SetRenderDrawColor(renderer, 4, 0, 89, 255);
        _ = SDL_RenderClear(renderer);

        const int frameX = 152; // Start first program column
        const int frameY = 206; // Start below frame.

        var horizontalOffset = 62;
        var verticalOffset = 7;

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

        // Draw the time boxes.
        {
            // First two time boxes.
            _ = SDL_QueryTexture(timeboxFrameTexture.SdlTexture, out uint _, out int _, out int tbw, out int tbh);
            var dstRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect1);
            var dstRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect2);

            // Last one.
            _ = SDL_QueryTexture(timeboxLastFrameTexture.SdlTexture, out _, out _, out var tblw, out var tblh);
            var dstRect = new SDL_Rect { h = tblh, w = tblw, x = 496 * scale, y = 0 };
            _ = SDL_RenderCopy(renderer, timeboxLastFrameTexture.SdlTexture, IntPtr.Zero, ref dstRect);
        }

        if (DateTime.Now.Hour is 00 or >= 10)
            horizontalOffset -= 12;

        // Draw 3 channel frames.
        {
            var baseChannelY = 34;
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
    var stopWatch = Stopwatch.StartNew();

    _ = SDL_SetRenderDrawColor(renderer, 255, 0, 255, 255);
    _ = SDL_RenderClear(renderer);

    // Generate the grid
    using var gridTexture = new Texture(GenerateGridTexture());

    // Render the grid.
    _ = SDL_QueryTexture(gridTexture.SdlTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
    var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = 227 * scale + gridOffset };
    _ = SDL_RenderCopy(renderer, gridTexture.SdlTexture, IntPtr.Zero, ref gridDstRect);

    // Draw FPS.
    var showFps = true;
    if (showFps && frameTimeList.Any())
    {
        // Generate average FPS.
        var averageFrameTime = frameTimeList.Average();
        var averageFps = 1000 / averageFrameTime;

        var fpsTexture = Generators.GenerateDropShadowText(renderer, prevueGrid,
            $"FPS: {averageFps:F}", gridTextYellow, scale);

        _ = SDL_QueryTexture(fpsTexture, out _, out _, out var fpsTextureWidth, out var fpsTextureHeight);
        var fpsDstRect = new SDL_Rect { h = fpsTextureHeight, w = fpsTextureWidth, x = (windowWidth - 180) * scale, y = (6 * scale) };
        _ = SDL_RenderCopy(renderer, fpsTexture, IntPtr.Zero, ref fpsDstRect);
        SDL_DestroyTexture(fpsTexture);
    }

    // Switches out the currently presented render surface with the one we just did work on.
    SDL_RenderPresent(renderer);

    stopWatch.Stop();

    const bool limitFps = false;
    const int targetFps = 30;

    if (limitFps)
    {
        const int targetDuration = 1000 / targetFps;
        var duration = (targetDuration - stopWatch.ElapsedMilliseconds);

        if (duration > 0)
            SDL_Delay((uint)duration);
    }

    frameTimeList.Add(stopWatch.ElapsedMilliseconds);

    while (frameTimeList.Count > 5)
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

    TTF_CloseFont(prevueGrid);
    TTF_Quit();
    SDL_Quit();
}

record Channel
{
    public string ChannelNumber { get; init; }
    public string CallSign { get; init; }
}
