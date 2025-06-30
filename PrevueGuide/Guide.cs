using Microsoft.Extensions.Logging;
using PrevueGuide.Core;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Esquire;
using SDL3;

namespace PrevueGuide;

public class Guide : IDisposable
{
    private const int DefaultWindowWidth = 716;
    private const int DefaultWindowHeight = 436;

    private readonly TextureManager _textureManager;

    private readonly ILogger _logger;
    private IGuideTextureProvider _guideTextureProvider;
    private IntPtr _window;
    private IntPtr _renderer;
    private bool _running;
    private bool _highDpi = true;
    private bool _vsync = true;
    private bool _fullscreen;

    public Guide(ILogger logger)
    {
        _logger = logger;
        _textureManager = new TextureManager(logger);
    }

    public void Run()
    {
        Setup();

        while (_running)
        {
            PollEvents();
            Render();
        }
    }

    private void Setup()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
        {
            throw new Exception($"There was an issue initializing SDL. {SDL.GetError()}");
        }

        _ = TTF.Init();

        _window = SDL.CreateWindow("Prevue Guide",
            DefaultWindowWidth,
            DefaultWindowHeight,
            _highDpi ? SDL.WindowFlags.HighPixelDensity : 0);

        if (_window == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the window. {SDL.GetError()}");
        }

        _renderer = SDL.CreateRenderer(_window, "");
        _ = SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);

        if (_renderer == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the renderer. {SDL.GetError()}");
        }

        SetFullscreen();
        SetVSync();

        SetScaleFromWindowSize();

        _running = true;
    }

    private void SetScaleFromWindowSize()
    {
        SDL.GetWindowSizeInPixels(_window, out var windowWidthPixels, out var windowHeightPixels);
        _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", windowWidthPixels, windowHeightPixels);
        Configuration.Scale = windowWidthPixels / DefaultWindowWidth;
        _logger.LogInformation(@"[Window] Scale: {Scale}x", Configuration.Scale);

        _guideTextureProvider = new EsquireGuideTextureProvider(_renderer);
    }

    private void SetFullscreen()
    {
        // TODO: When in fullscreen, calculate a new "scale" from our requested resolution,
        // versus the actual resolution so we can keep the guide stuff rendering at the proper size.
        _logger.LogInformation($"SDL: Setting fullscreen to: {_fullscreen}");
        _ = SDL.SetWindowFullscreen(_window, _fullscreen);

        SetScaleFromWindowSize();
        TestGenerateFrameTexture();
    }

    private void SetVSync()
    {
        _logger.LogInformation($"SDL: Setting vsync to: {_vsync}");
        _ = SDL.SetRenderVSync(_renderer, _vsync ? 1 : 0);
    }

    private void PollEvents()
    {
        while (SDL.PollEvent(out var sdlEvent))
        {
            if (sdlEvent.Type == (uint)SDL.EventType.Quit)
            {
                _logger.LogInformation("SDL: Quit event encountered.");
                _running = false;
            }
            else if (sdlEvent.Type == (uint)SDL.EventType.KeyDown)
            {
                switch (sdlEvent.Key.Key)
                {
                    case SDL.Keycode.F:
                        _fullscreen = !_fullscreen;
                        SetFullscreen();
                        break;
                    case SDL.Keycode.V:
                        _vsync = !_vsync;
                        SetVSync();
                        break;
                }
            }
            else if (sdlEvent.Type == (uint)SDL.EventType.WindowPixelSizeChanged)
            {
                // Todo: On pixel size change, determine from the window resolution
                // if we can crop out the side or top/bottom margins to match the expected aspect ratio.
                SetScaleFromWindowSize();
                TestGenerateFrameTexture();
            }
        }
    }

    private void TestGenerateFrameTexture()
    {
        try
        {
            _textureManager.PurgeTexture("frame");

            // Create a fake listing. Set it 15 minutes in the past and 105 minutes into the future
            // so the listing gets single arrows on both sides.
            var now = DateTime.Now;
            var startTime = Core.Utilities.Time.ClampToNextHalfHourIfTenMinutesAway(now).AddMinutes(-15);
            var listing = new Listing
            {
                StartTime = startTime,
                Block = 0,
                EndTime = startTime.AddHours(2)
            };

            _textureManager["frame"] = _guideTextureProvider.GenerateListingTexture(listing, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
    }

    private void Render()
    {
        _ = InternalSDL3.SetRenderDrawColor(_renderer, _guideTextureProvider.DefaultGuideBackground);
        _ = SDL.RenderClear(_renderer);

        var frame = _textureManager["frame"];

        if (frame != null)
        {
            _ = SDL.GetTextureSize(frame.SdlTexture, out var width, out var height);
            var dstFRect = new SDL.FRect
            {
                X = 0,
                Y = 0,
                W = width,
                H = height
            };

            _ = SDL.RenderTexture(_renderer, frame.SdlTexture, IntPtr.Zero, dstFRect);
        }

        _ = SDL.RenderPresent(_renderer);
    }

    public void Dispose()
    {
        _textureManager.Dispose();

        TTF.Quit();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        SDL.Quit();
    }
}
