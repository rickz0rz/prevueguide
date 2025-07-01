using Microsoft.Extensions.Logging;
using PrevueGuide.Core;
using PrevueGuide.Core.Data.ChannelsDVR;
using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Esquire;
using PrevueGuide.Core.SDL.Wrappers;
using SDL3;

namespace PrevueGuide;

public class Guide : IDisposable
{
    private readonly ILogger _logger;
    private readonly TextureManager _textureManager;

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

        _guideTextureProvider = new EsquireGuideTextureProvider(_logger);
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
            _guideTextureProvider.DefaultWindowWidth,
            _guideTextureProvider.DefaultWindowHeight,
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

        _guideTextureProvider.SetRenderer(_renderer);

        SetFullscreen();
        SetVSync();
        SetScaleFromWindowSize();

        _running = true;
    }

    private void SetScaleFromWindowSize()
    {
        SDL.GetWindowSizeInPixels(_window, out var windowWidthPixels, out var windowHeightPixels);
        _logger.LogInformation(@"[Window] Window Size: {Width} x {Height}", windowWidthPixels, windowHeightPixels);

        var rawWidthScale = (float)windowWidthPixels / _guideTextureProvider.DefaultWindowWidth;
        var rawHeightScale = (float)windowHeightPixels / _guideTextureProvider.DefaultWindowHeight;
        _logger.LogInformation(@"[Window] Scales: {Width} x {Height}", rawWidthScale, rawHeightScale);

        var smallerScale = rawWidthScale > rawHeightScale ? rawHeightScale : rawWidthScale;
        Configuration.Scale = (int)float.Floor(smallerScale);
        _logger.LogInformation(@"[Window] Scale: {Scale}x", Configuration.Scale);

        if (_guideTextureProvider.DefaultFullscreenMode == FullscreenMode.Letterbox)
        {
            _logger.LogInformation($"Using fullscreen mode: {FullscreenMode.Letterbox}");

            Configuration.DrawableWidth = Configuration.Scale * _guideTextureProvider.DefaultWindowWidth;
            Configuration.DrawableHeight = Configuration.Scale * _guideTextureProvider.DefaultWindowHeight;
            _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", Configuration.DrawableWidth,
                Configuration.DrawableHeight);

            Configuration.X = (windowWidthPixels - Configuration.DrawableWidth) / 2;
            Configuration.Y = (windowHeightPixels - Configuration.DrawableHeight) / 2;
            _logger.LogInformation(@"[Window] Offset: {Width} x {Height}", Configuration.X, Configuration.Y);
        }
        else // Default: ScaledFill
        {
            _logger.LogInformation($"Using fullscreen mode: {FullscreenMode.ScaledFill}");

            Configuration.DrawableWidth = windowWidthPixels;
            Configuration.DrawableHeight = windowHeightPixels;
            _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", Configuration.DrawableWidth,
                Configuration.DrawableHeight);

            Configuration.X = 0;
            Configuration.Y = 0;
            _logger.LogInformation(@"[Window] Offset: {Width} x {Height}", Configuration.X, Configuration.Y);
        }
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
            _textureManager.PurgeTexture("frame1");
            _textureManager.PurgeTexture("frame2");
            _textureManager.PurgeTexture("guide");

            var now = DateTime.Now;
            var provider = new ChannelsDVRListingsDataProvider(_logger, "http://192.168.0.195:8089");
            provider.PrevueChannelNumber = null;
            var startTime = Core.Utilities.Time.ClampToNextHalfHourIfTenMinutesAway(now);
            var endTime = startTime.AddMinutes(90);
            var listings = provider.GetChannelListings(startTime, endTime).Result;

            _textureManager["guide"] = new Texture(_renderer, Configuration.UnscaledDrawableWidth, Configuration.UnscaledDrawableHeight);
            _textureManager["frame1"] = _guideTextureProvider.GenerateListingTexture(listings.First(), now);
            _textureManager["frame2"] = _guideTextureProvider.GenerateListingTexture(listings.Skip(1).First(), now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
    }

    private void Render()
    {
        SDL.SetRenderTarget(_renderer, IntPtr.Zero);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, new SDL.Color { A = 255, R = 0, G = 0, B = 0 });
        _ = SDL.RenderClear(_renderer);

        using (_ = new RenderingTarget(_renderer, _textureManager["guide"]))
        {
            InternalSDL3.SetRenderDrawColor(_renderer, _guideTextureProvider.DefaultGuideBackground);
            SDL.RenderClear(_renderer);

            foreach (var t in new[] { ("frame1", 0), ("frame2", 56 * Configuration.Scale) })
            {
                var frame1 = _textureManager[t.Item1];

                if (frame1 != null)
                {
                    _ = SDL.GetTextureSize(frame1.SdlTexture, out var width, out var height);
                    var dstFRect = new SDL.FRect
                    {
                        X = 0,
                        Y = t.Item2,
                        W = width,
                        H = height
                    };

                    _ = SDL.RenderTexture(_renderer, frame1.SdlTexture, IntPtr.Zero, dstFRect);
                }
            }

        }

        var guideFRect = new SDL.FRect
        {
            X = Configuration.X,
            Y = Configuration.Y,
            W = Configuration.DrawableWidth,
            H = Configuration.DrawableHeight
        };
        _ = SDL.RenderTexture(_renderer, _textureManager["guide"].SdlTexture, IntPtr.Zero, guideFRect);

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
