using System.Diagnostics;
using PrevueGuide;
using SDL2;
using static SDL2.SDL;

const int windowWidth = 716;
const int windowHeight = 436;

IntPtr window;
IntPtr renderer;
IntPtr prevueGrid;

DateTime time = DateTime.UnixEpoch;

IntPtr timeTexture = IntPtr.Zero;
IntPtr channelFrameTexture = IntPtr.Zero;
IntPtr clockFrameTexture = IntPtr.Zero;
IntPtr timeboxFrameTexture = IntPtr.Zero;
IntPtr timeboxLastFrameTexture = IntPtr.Zero;

IntPtr bigFrameTexture;
IntPtr bigFrameText;

var scale = 2;
var running = true;

var clockBackgroundColor = new SDL_Color { a = 255, r = 34, g = 41, b = 141 };

var scrollingTest = 0;

Setup();

while (running)
{
    PollEvents();
    Render();
}

CleanUp();

// Setup all of the SDL resources we'll need to display a window.
void Setup()
{
    // Initializes SDL.
    if (SDL_Init(SDL_INIT_VIDEO) < 0)
    {
        Console.WriteLine($"There was an issue initializing SDL. {SDL_GetError()}");
    }

    // Create a new window given a title, size, and passes it a flag indicating it should be shown.
    window = SDL_CreateWindow(
        "",
        SDL_WINDOWPOS_UNDEFINED,
        SDL_WINDOWPOS_UNDEFINED,
        windowWidth,
        windowHeight,
        SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI);

    if (window == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
    }

    // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
    renderer = SDL_CreateRenderer(
        window,
        -1,
        SDL_RendererFlags.SDL_RENDERER_ACCELERATED |
        SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

    if (renderer == IntPtr.Zero)
    {
        Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
    }

    _ = SDL_SetRenderDrawBlendMode(renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

    SDL_ttf.TTF_Init();
    prevueGrid = SDL_ttf.TTF_OpenFont("assets/PrevueGrid.ttf", 25 * scale); // Maybe 50? Test.

    SDL_image.IMG_Init(SDL_image.IMG_InitFlags.IMG_INIT_PNG);

    clockFrameTexture = Generators.GenerateFrame(renderer, 144, 34, clockBackgroundColor, scale);
    timeboxFrameTexture = Generators.LoadImageToTexture(renderer, "assets/timebox_frame_2x_smooth.png");
    timeboxLastFrameTexture = Generators.LoadImageToTexture(renderer, "assets/timebox_last_frame_2x_smooth.png");
    channelFrameTexture = Generators.LoadImageToTexture(renderer, "assets/channel_frame_2x_smooth.png");
    bigFrameTexture = Generators.GenerateFrame(renderer, 300, 80,
        new SDL_Color { a = 255, r = 192, g = 0, b = 0 }, scale);
    bigFrameText = Generators.GenerateDropShadowText(renderer, prevueGrid, "PrrRRrreeVuuEEE",
        new SDL_Color { a = 255, r = 203, g = 209, b = 0 }, scale);
}

// Checks to see if there are any events to be processed.
void PollEvents()
{
    // Check to see if there are any events and continue to do so until the queue is empty.
    while (SDL_PollEvent(out var sdlEvent) == 1)
    {
        running = sdlEvent.type switch
        {
            SDL_EventType.SDL_QUIT => false,
            _ => running
        };
    }
}

// Renders to the window.
void Render()
{
    var stopWatch = Stopwatch.StartNew();

    _ = SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
    _ = SDL_RenderClear(renderer);

    // Cheap and cheating.
    var now = DateTime.Now;
    if (time.Second != now.Second)
    {
        time = now;
        timeTexture = Generators.GenerateDropShadowText(renderer, prevueGrid, now.ToString("h:mm:ss"),
            new SDL_Color { a = 255, r = 170, g = 170, b = 170 }, scale);
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
    _ = SDL_SetRenderTarget(renderer, gridTexture);

    _ = SDL_SetRenderDrawColor(renderer, 4, 0, 89, 255);
    _ = SDL_RenderClear(renderer);

    // Quick guess. 110 frames for a full grid push @ 112 (2x) / 56 (1x) height.
    // That means roughly 2 frames per pixel going up.
    scrollingTest += 1;
    if (scrollingTest >= 370)
        scrollingTest = 0;
    var testingOffset = (scrollingTest / 2);

    var frameX = 152;
    var frameY = 150;

    _ = SDL_QueryTexture(bigFrameTexture, out _, out _, out var bfWidth, out var bfHeight);
    var bfDstRect = new SDL_Rect
        { h = bfHeight, w = bfWidth, x = frameX * scale, y = ((frameY - testingOffset) * scale) };
    _ = SDL_RenderCopy(renderer, bigFrameTexture, IntPtr.Zero, ref bfDstRect);

    _ = SDL_QueryTexture(bigFrameText, out _, out _, out var bftWidth, out var bftHeight);
    var bftDstRect = new SDL_Rect
        { h = bftHeight, w = bftWidth, x = (frameX + 16) * scale, y = (frameY + 16 - testingOffset) * scale };
    _ = SDL_RenderCopy(renderer, bigFrameText, IntPtr.Zero, ref bftDstRect);

    // Draw the clock frame.
    _ = SDL_QueryTexture(clockFrameTexture, out _, out _, out var clockFrameWidth, out var clockFrameHeight);
    var clockFrameDstRect = new SDL_Rect { h = clockFrameHeight, w = clockFrameWidth, x = 8 * scale, y = 0 };
    _ = SDL_RenderCopy(renderer, clockFrameTexture, IntPtr.Zero, ref clockFrameDstRect);

    // Draw the time boxes.
    {
        // First two time boxes.
        _ = SDL_QueryTexture(timeboxFrameTexture, out uint _, out int _, out int tbw, out int tbh);
        var dstRect1 = new SDL_Rect { h = tbh, w = tbw, x = 152 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxFrameTexture, IntPtr.Zero, ref dstRect1);
        var dstRect2 = new SDL_Rect { h = tbh, w = tbw, x = 324 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxFrameTexture, IntPtr.Zero, ref dstRect2);

        // Last one.
        _ = SDL_QueryTexture(timeboxLastFrameTexture, out uint _, out int _, out int tblw, out int tblh);
        var dstRect = new SDL_Rect { h = tblh, w = tblw, x = 496 * scale, y = 0 };
        _ = SDL_RenderCopy(renderer, timeboxLastFrameTexture, IntPtr.Zero, ref dstRect);
    }

    var horizontalOffset = 62;
    var verticalOffset = 7;

    if (now.Hour is 00 or >= 10)
        horizontalOffset -= 12;

    // Draw 3 channel frames.
    {
        var baseChannelY = 34;
        _ = SDL_QueryTexture(channelFrameTexture, out _, out _, out var w, out var h);
        var dstRect1 = new SDL_Rect
            { h = h, w = w, x = 8 * scale, y = baseChannelY * scale };
        _ = SDL_RenderCopy(renderer, channelFrameTexture, IntPtr.Zero, ref dstRect1);
        var dstRect2 = new SDL_Rect
            { h = h, w = w, x = 8 * scale, y = (baseChannelY + 56) * scale };
        _ = SDL_RenderCopy(renderer, channelFrameTexture, IntPtr.Zero, ref dstRect2);
        var dstRect3 = new SDL_Rect
            { h = h, w = w, x = 8 * scale, y = (baseChannelY + 112) * scale };
        _ = SDL_RenderCopy(renderer, channelFrameTexture, IntPtr.Zero, ref dstRect3);
    }

    _ = SDL_QueryTexture(timeTexture, out _, out _, out var timeWidth, out var timeHeight);
    var timeDstRect = new SDL_Rect
        { h = timeHeight, w = timeWidth, x = (horizontalOffset - 1) * scale, y = (verticalOffset - 1) * scale };
    _ = SDL_RenderCopy(renderer, timeTexture, IntPtr.Zero, ref timeDstRect);

    // Switch to the primary surface/target
    _ = SDL_SetRenderTarget(renderer, IntPtr.Zero);

    // Draw the grid
    _ = SDL_QueryTexture(gridTexture, out _, out _, out var gridTextureWidth, out var gridTextureHeight);
    var gridDstRect = new SDL_Rect { h = gridTextureHeight, w = gridTextureWidth, x = 0, y = (227 * scale) };
    _ = SDL_RenderCopy(renderer, gridTexture, IntPtr.Zero, ref gridDstRect);
    SDL_DestroyTexture(gridTexture);

    // Switches out the currently presented render surface with the one we just did work on.
    SDL_RenderPresent(renderer);

    // Simulate 30 fps.
    stopWatch.Stop();
    var duration = (33 - stopWatch.ElapsedMilliseconds);
    if (duration > 0)
        Thread.Sleep((int)duration);
}

// Clean up the resources that were created.
void CleanUp()
{
    SDL_DestroyTexture(timeTexture);

    SDL_DestroyTexture(clockFrameTexture);
    SDL_DestroyTexture(timeboxFrameTexture);
    SDL_DestroyTexture(timeboxLastFrameTexture);

    SDL_DestroyTexture(bigFrameTexture);
    SDL_DestroyTexture(bigFrameText);

    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);

    SDL_ttf.TTF_CloseFont(prevueGrid);
    SDL_ttf.TTF_Quit();

    SDL_Quit();
}
