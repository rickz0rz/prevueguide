using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core;
using PrevueGuide.Core.Data.ChannelsDVR;
using PrevueGuide.Core.Logging;
using PrevueGuide.Core.Model.Listings.Channel;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Esquire;
using PrevueGuide.Core.SDL.Wrappers;
using SDL3;

namespace PrevueGuide;

// Clean up the amount of texture generation going on. That stuff kills performance.
// Make multiple providers (guide logo, copyright, "sbs", now playing on plex, etc.)
// How do we orchestrate said providers?
// Mark textures dirty? (Time)
// How do I manage caching of guide entries? Do I just regenerate the textures every time the guide time will roll-over?
// If so, can we do something to make it so we can procedurally create the textures instead of doing them all in one-shot to make the delay less obvious?
// At any given time, we need enough guide elements to render the whole screen filled. The guide is presented by being shifted down (or up?) a specific amount.

public class Guide : IDisposable
{
    private readonly ILogger _logger;
    private readonly TextureManager _textureManager;
    private readonly FontManager _fontManager;
    private readonly IGuideThemeProvider _guideThemeProvider;

    private List<int> _frameTimes;
    private int _updateFPSCounter;

    private IntPtr _window;
    private IntPtr _renderer;
    private bool _running;
    private bool _showLogs;
    private bool _showFps;
    private bool _highDpi = true;
    private bool _vsync = true;
    private bool _fullscreen;
    private FullscreenMode _currentFullscreenMode;

    private ContainedLineLogger _containedLineLogger;
    private long _lastLinesLoggedCount = -1;

    public Guide(ILogger logger)
    {
        if (!SDL.Init(SDL.InitFlags.Video))
        {
            throw new Exception($"There was an issue initializing SDL. {SDL.GetError()}");
        }

        _ = TTF.Init();

        _logger = logger;
        _textureManager = new TextureManager(logger);
        _fontManager = new FontManager(logger);
        _guideThemeProvider = new EsquireGuideThemeProvider(_logger);
        _currentFullscreenMode = _guideThemeProvider.DefaultFullscreenMode;
        _frameTimes  = [];

        // For rendering on-screen logs.
        _containedLineLogger = ContainedLineLoggerProvider.Logger;
    }

    public void Run()
    {
        Setup();

        var stopWatch = new Stopwatch();
        while (_running)
        {
            stopWatch.Start();
            PollEvents();
            Render();
            stopWatch.Stop();

            while (_frameTimes.Count >= 10)
            {
                _frameTimes.RemoveAt(0);
            }
            _frameTimes.Add(stopWatch.Elapsed.Milliseconds);

            stopWatch.Reset();
        }
    }

    private void Setup()
    {
        // var scaledWindow = (int)(_guideThemeProvider.DefaultWindowHeight * _guideThemeProvider.ScaleRatio);

        _window = SDL.CreateWindow("Prevue Guide",
            _guideThemeProvider.DefaultWindowWidth,
            _guideThemeProvider.DefaultWindowHeight,
            _highDpi ? SDL.WindowFlags.HighPixelDensity | SDL.WindowFlags.Metal : SDL.WindowFlags.Metal);

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

        _guideThemeProvider.SetRenderer(_renderer);

        SetFullscreen();
        SetVSync();
        SetScaleFromWindowSize();

        _running = true;
    }

    private void SetScaleFromWindowSize()
    {
        SDL.GetWindowSizeInPixels(_window, out var windowWidthPixels, out var windowHeightPixels);
        Configuration.WindowWidth = windowWidthPixels;
        Configuration.WindowHeight = windowHeightPixels;
        _logger.LogInformation(@"[Window] Window Size: {Width} x {Height}", windowWidthPixels, windowHeightPixels);

        var rawWidthScale = (float)windowWidthPixels / _guideThemeProvider.DefaultWindowWidth;
        var rawHeightScale = (float)windowHeightPixels / _guideThemeProvider.DefaultWindowHeight;
        _logger.LogInformation(@"[Window] Scales: {Width} x {Height}", rawWidthScale, rawHeightScale);

        var smallerScale = rawWidthScale > rawHeightScale ? rawHeightScale : rawWidthScale;
        Configuration.Scale = (int)float.Floor(smallerScale);
        _logger.LogInformation(@"[Window] Scale: {Scale}x", Configuration.Scale);

        if (_currentFullscreenMode == FullscreenMode.Letterbox)
        {
            _logger.LogInformation($"Using fullscreen mode: {FullscreenMode.Letterbox}");

            Configuration.DrawableWidth = Configuration.Scale * _guideThemeProvider.DefaultWindowWidth;
            Configuration.DrawableHeight = Configuration.Scale * _guideThemeProvider.DefaultWindowHeight;
            _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", Configuration.DrawableWidth,
                Configuration.DrawableHeight);

            Configuration.X = (windowWidthPixels - Configuration.DrawableWidth) / 2;
            Configuration.Y = (windowHeightPixels - Configuration.DrawableHeight) / 2;
            _logger.LogInformation(@"[Window] Offset: {Width} x {Height}", Configuration.X, Configuration.Y);

            Configuration.RenderedWidth = Configuration.DrawableWidth;
            Configuration.RenderedHeight = Configuration.DrawableHeight;
        }
        else if (_currentFullscreenMode == FullscreenMode.ZoomedFill)
        {
            _logger.LogInformation($"Using fullscreen mode: {FullscreenMode.ZoomedFill}");

            Configuration.DrawableWidth = Configuration.Scale * _guideThemeProvider.DefaultWindowWidth;
            Configuration.DrawableHeight = Configuration.Scale * _guideThemeProvider.DefaultWindowHeight;
            _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", Configuration.DrawableWidth,
                Configuration.DrawableHeight);

            Configuration.RenderedWidth = (int)(smallerScale * _guideThemeProvider.DefaultWindowWidth);
            Configuration.RenderedHeight = (int)(smallerScale * _guideThemeProvider.DefaultWindowHeight);
            _logger.LogInformation(@"[Window] Rendered: {Width} x {Height}", Configuration.RenderedWidth, Configuration.RenderedHeight);

            Configuration.X = (windowWidthPixels - Configuration.RenderedWidth) / 2;
            Configuration.Y = (windowHeightPixels - Configuration.RenderedHeight) / 2;
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

            Configuration.RenderedWidth = Configuration.DrawableWidth;
            Configuration.RenderedHeight = Configuration.DrawableHeight;
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
                    case SDL.Keycode.L:
                        _showLogs = !_showLogs;
                        _logger.LogInformation("Show logs: {ShowLogs}", _showLogs);
                        break;
                    case SDL.Keycode.M:
                        _currentFullscreenMode = _currentFullscreenMode switch
                        {
                            FullscreenMode.ScaledFill => FullscreenMode.ZoomedFill,
                            FullscreenMode.ZoomedFill => FullscreenMode.Letterbox,
                            _ => FullscreenMode.ScaledFill
                        };

                        _logger.LogInformation("Fullscreen mode change: {FullscreenMode}", _currentFullscreenMode);

                        if (_fullscreen)
                        {
                            SetScaleFromWindowSize();
                            TestGenerateFrameTexture();
                        }
                        break;
                    case SDL.Keycode.Q:
                        _running = false;
                        break;
                    case SDL.Keycode.P:
                        _showFps = !_showFps;
                        _logger.LogInformation("Show fps: {ShowFps}", _showFps);
                        break;
                    case SDL.Keycode.V:
                        _vsync = !_vsync;
                        SetVSync();
                        break;
                }
            }
            else if (sdlEvent.Type == (uint)SDL.EventType.WindowPixelSizeChanged)
            {
                SetScaleFromWindowSize();
                TestGenerateFrameTexture();
            }
        }
    }

    private void TestGenerateFrameTexture()
    {
        try
        {
            _textureManager.PurgeTexture("row1");
            // _textureManager.PurgeTexture("frame2");
            _textureManager.PurgeTexture("guide");

            var now = DateTime.Now;
            var provider = new ChannelsDVRListingsDataProvider(_logger, "http://192.168.0.195:8089");
            provider.PrevueChannelNumber = null;
            var listings = provider.GetEntries().ToBlockingEnumerable();

            listings = listings.Where(listing => listing is ChannelListing)
                .Select(listing => listing as ChannelListing)
                .Where(listing => listing.Programs.First().IsMovie);

            _textureManager["guide"] = new Texture(_renderer, Configuration.UnscaledDrawableWidth, Configuration.UnscaledDrawableHeight);
            _textureManager["row1"] = _guideThemeProvider.GenerateRows(listings).First();
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

        if (_textureManager["guide"] != null)
        {
            using (_ = new RenderingTarget(_renderer, _textureManager["guide"]))
            {
                InternalSDL3.SetRenderDrawColor(_renderer, _guideThemeProvider.DefaultGuideBackground);
                SDL.RenderClear(_renderer);

                foreach (var t in new[] { ("row1", 0) })
                {
                    var row1 = _textureManager[t.Item1];

                    if (row1 != null)
                    {
                        _ = SDL.GetTextureSize(row1.SdlTexture, out var width, out var height);
                        var dstFRect = new SDL.FRect
                        {
                            X = 0,
                            Y = t.Item2,
                            W = width * _guideThemeProvider.ScaleRatio,
                            H = height
                        };

                        _ = SDL.RenderTexture(_renderer, row1.SdlTexture, IntPtr.Zero, dstFRect);
                    }
                }
            }
        }

        var guideFRect = new SDL.FRect
        {
            X = Configuration.X,
            Y = Configuration.Y,
            W = Configuration.RenderedWidth,
            H = Configuration.RenderedHeight
        };
        _ = SDL.RenderTexture(_renderer, _textureManager["guide"].SdlTexture, IntPtr.Zero, guideFRect);

        if (_showLogs)
        {
            RenderLogs();
        }

        if (_showFps)
        {
            RenderFps();
        }

        _ = SDL.RenderPresent(_renderer);
    }

    private void RenderFps()
    {
        if (_updateFPSCounter == 0)
        {
            _updateFPSCounter = 30;

            var font = _fontManager["FiraCode"];
            var yellow = new SDL.Color { A = 255, R = 255, G = 255, B = 0 };

            var fps = 1000 / _frameTimes.Average();
            var l = $"{fps:0.00 FPS}";

            using var surface = new Surface(TTF.RenderTextBlended(font, l, 0, yellow));

            _textureManager["fps"] = new Texture(SDL.CreateTextureFromSurface(_renderer, surface.SdlSurface));
        }
        else
        {
            _updateFPSCounter--;
        }

        if (_textureManager["fps"] != null)
        {
            var texture = _textureManager["fps"];

            SDL.GetTextureSize(texture.SdlTexture, out var width, out var height);
            var rect = new SDL.FRect
            {
                W = width,
                H = height,
                X = Configuration.WindowWidth - width,
                Y = 0
            };

            _ = SDL.RenderTexture(_renderer, texture.SdlTexture, IntPtr.Zero, rect);
        }

    }

    private void RenderLogs()
    {
        const string firaCodeFontName = "FiraCode";
        if (_lastLinesLoggedCount != _containedLineLogger.LinesLogged)
        {
            _lastLinesLoggedCount = _containedLineLogger.LinesLogged;

            var font = _fontManager[firaCodeFontName];
            var yellow = new SDL.Color { A = 255, R = 255, G = 255, B = 0 };

            for (var i = 0; i < _containedLineLogger.Lines.Count; i++)
            {
                var line = _containedLineLogger.Lines[i];
                using var surface = new Surface(TTF.RenderTextBlended(font, line, 0, yellow));
                _textureManager[$"log_{i}"] = new Texture(SDL.CreateTextureFromSurface(_renderer, surface.SdlSurface));
            }
        }

        var lineHeight = _fontManager.FontConfigurations[firaCodeFontName].PointSize;
        var y = Configuration.WindowHeight - (lineHeight * Configuration.Scale * 5);

        for (var i = 0; i < _containedLineLogger.Lines.Count; i++)
        {
            var texture = _textureManager[$"log_{i}"];
            SDL.GetTextureSize(texture.SdlTexture, out var width, out var height);
            var rect = new SDL.FRect
            {
                W = width,
                H = height,
                X = Configuration.WindowWidth - width,
                Y = y
            };
            _ = SDL.RenderTexture(_renderer, texture.SdlTexture, IntPtr.Zero, rect);
            y += lineHeight * Configuration.Scale;
        }
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
