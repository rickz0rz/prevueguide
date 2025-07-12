using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core;
using PrevueGuide.Core.Data.ChannelsDVR;
using PrevueGuide.Core.Logging;
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
// Don't rerender every element if they have not changed.
// Allow listings provider to cache listings if they're not updated and silently update them in the background
//     if they need to be...? Maybe have it on a timer and do it async.

public class Guide : IDisposable
{
    private readonly ILogger _logger;
    private readonly TextureManager _textureManager;
    private readonly FontManager _fontManager;
    private readonly IGuideThemeProvider _esquireGuideThemeProvider;

    private List<int> _frameTimes;
    private int _updateFPSFrameCounter;

    private IntPtr _window;
    private IntPtr _renderer;
    private bool _running;
    private bool _showLogs;
    private bool _showFps;
    private bool _highDpi = true;
    private bool _vsync = true;
    private bool _fullscreen;
    private bool _thirtyFpsLimit = false;
    private FullscreenMode _currentFullscreenMode;

    private ContainedLineLogger _containedLineLogger;
    private long _lastLinesLoggedCount = -1;
    private float _guideRowBaseY = 0f;
    private float _scrollSpeed = 0f;

    private IEnumerator<Texture> _rowsTextureSource;
    private Queue<Texture> _rowsTextureQueue = new();
    private ChannelsDVRListingsDataProvider provider;

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
        _esquireGuideThemeProvider = new EsquireGuideThemeProvider(_logger);
        _currentFullscreenMode = _esquireGuideThemeProvider.DefaultFullscreenMode;
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

            if (_thirtyFpsLimit)
            {
                var delay = 33 - stopWatch.Elapsed.Milliseconds;
                if (delay > 0)
                    Thread.Sleep(delay);
            }

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
            _esquireGuideThemeProvider.DefaultWindowWidth,
            _esquireGuideThemeProvider.DefaultWindowHeight,
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

        _esquireGuideThemeProvider.SetRenderer(_renderer);

        var now = DateTime.Now;
        provider = new ChannelsDVRListingsDataProvider(_logger, "http://192.168.0.195:8089");
        provider.PrevueChannelNumber = 1;

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

        var rawWidthScale = (float)windowWidthPixels / _esquireGuideThemeProvider.DefaultWindowWidth;
        var rawHeightScale = (float)windowHeightPixels / _esquireGuideThemeProvider.DefaultWindowHeight;
        _logger.LogInformation(@"[Window] Scales: {Width} x {Height}", rawWidthScale, rawHeightScale);

        var smallerScale = rawWidthScale > rawHeightScale ? rawHeightScale : rawWidthScale;
        Configuration.Scale = (int)float.Floor(smallerScale);
        _logger.LogInformation(@"[Window] Scale: {Scale}x", Configuration.Scale);

        if (_currentFullscreenMode == FullscreenMode.Letterbox)
        {
            _logger.LogInformation($"Using fullscreen mode: {FullscreenMode.Letterbox}");

            Configuration.DrawableWidth = Configuration.Scale * _esquireGuideThemeProvider.DefaultWindowWidth;
            Configuration.DrawableHeight = Configuration.Scale * _esquireGuideThemeProvider.DefaultWindowHeight;
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

            Configuration.DrawableWidth = Configuration.Scale * _esquireGuideThemeProvider.DefaultWindowWidth;
            Configuration.DrawableHeight = Configuration.Scale * _esquireGuideThemeProvider.DefaultWindowHeight;
            _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", Configuration.DrawableWidth,
                Configuration.DrawableHeight);

            Configuration.RenderedWidth = (int)(smallerScale * _esquireGuideThemeProvider.DefaultWindowWidth);
            Configuration.RenderedHeight = (int)(smallerScale * _esquireGuideThemeProvider.DefaultWindowHeight);
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

        // SetScaleFromWindowSize();
        // TestGenerateFrameTexture();
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
                    case SDL.Keycode.Equals:
                        _scrollSpeed += 0.125f;
                        break;
                    case SDL.Keycode.Minus:
                        _scrollSpeed -= 0.125f;
                        if (_scrollSpeed < 0)
                            _scrollSpeed = 0f;
                        break;
                    case SDL.Keycode.D:

                        for (var i = 0; i < 3; i++)
                        {
                            if (_rowsTextureQueue.Any())
                                _ = _rowsTextureQueue.Dequeue();
                        }
                        CheckIfQueueFilled();
                        break;
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
                            InitializeGuideTexture();
                        }
                        break;
                    case SDL.Keycode.T:
                        _thirtyFpsLimit = !_thirtyFpsLimit;
                        _logger.LogInformation("30 FPS Limit: {ThirtyFpsLimit}", _thirtyFpsLimit);
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
                InitializeGuideTexture();
            }
        }
    }

    private void InitializeGuideTexture()
    {
        try
        {
            _textureManager.PurgeTexture("guide");

            /*
            listings = listings.Where(listing => listing is ChannelListing)
                .Select(listing => listing as ChannelListing)
                .Where(listing => listing.Programs.First().IsMovie);
                */

            _textureManager["guide"] = new Texture(_renderer, Configuration.UnscaledDrawableWidth, Configuration.UnscaledDrawableHeight);

            // Purge the queue
            while (_rowsTextureQueue.Count > 0)
            {
                _rowsTextureQueue.Dequeue().Dispose();
            }

            var listings = provider.GetEntries().ToBlockingEnumerable();
            _rowsTextureSource = _esquireGuideThemeProvider.GenerateRows(listings).GetEnumerator();

            CheckIfQueueFilled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
    }

    private int GetQueueHeight()
    {
        var height = 0;

        foreach (var texture in _rowsTextureQueue)
        {
            SDL.GetTextureSize(texture.SdlTexture, out _, out var h);
            height += (int)h;
        }

        return height;

        /*
        return _rowsTextureQueue.Select(texture =>
        {
            SDL.GetTextureSize(texture.SdlTexture, out _, out var h);
            return (int)h;
        }).Sum();
        */
    }

    // Hacky thing: How do I add extra records to the queue?
    private void Render()
    {
        SDL.SetRenderTarget(_renderer, IntPtr.Zero);

        _ = InternalSDL3.SetRenderDrawColor(_renderer, Colors.Black0);
        _ = SDL.RenderClear(_renderer);

        if (_textureManager["guide"] != null)
        {
            using (_ = new RenderingTarget(_renderer, _textureManager["guide"]))
            {
                InternalSDL3.SetRenderDrawColor(_renderer, _esquireGuideThemeProvider.DefaultGuideBackground);
                SDL.RenderClear(_renderer);
                var y = 0 - _guideRowBaseY;

                foreach (var row in _rowsTextureQueue)
                {
                    _ = SDL.GetTextureSize(row.SdlTexture, out var width, out var height);

                    var dstFRect = new SDL.FRect
                    {
                        X = (Configuration.DrawableWidth - width) / 2,
                        Y = y,
                        W = width * _esquireGuideThemeProvider.ScaleRatio,
                        H = height
                    };

                    _ = SDL.RenderTexture(_renderer, row.SdlTexture, IntPtr.Zero, dstFRect);
                    y += height;

                    _guideRowBaseY += ((_thirtyFpsLimit ? 0.125f : 0.0625f) + _scrollSpeed) * Configuration.Scale;
                }

                // If the baseY position is the same as its height, dequeue it.
                SDL.GetTextureSize(_rowsTextureQueue.First().SdlTexture, out _, out var firstH);
                if (_guideRowBaseY >= firstH)
                {
                    _ = _rowsTextureQueue.Dequeue();
                    CheckIfQueueFilled();
                    _guideRowBaseY = 0f;
                }
            }
        }

        // Guide position = 175 from the bottom not including time bar.
        var guideFRect = new SDL.FRect
        {
            X = Configuration.X,
            Y = Configuration.RenderedHeight - (175 * Configuration.Scale),
            // Y = Configuration.Y,
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

    private void CheckIfQueueFilled()
    {
        while ((GetQueueHeight() - _guideRowBaseY) < Configuration.RenderedHeight)
        {
            if (_rowsTextureSource == null || !_rowsTextureSource.MoveNext())
            {
                _logger.LogInformation("Filling row queue");
                var listings = provider.GetEntries().ToBlockingEnumerable();
                _rowsTextureSource = _esquireGuideThemeProvider.GenerateRows(listings).GetEnumerator();
                _rowsTextureSource.MoveNext();
            }

            _rowsTextureQueue.Enqueue(_rowsTextureSource.Current);
        }
    }

    private void RenderFps()
    {
        if (_updateFPSFrameCounter == 0)
        {
            _updateFPSFrameCounter = 30;

            var fps = 1000 / _frameTimes.Average();
            using var surface = new Surface(TTF.RenderTextBlended(_fontManager["FiraCode"], $"{fps:0.00 FPS}", 0, Colors.Yellow));
            _textureManager["fps"] = new Texture(SDL.CreateTextureFromSurface(_renderer, surface.SdlSurface));
        }
        else
        {
            _updateFPSFrameCounter--;
        }

        if (_textureManager["fps"] != null)
        {
            var texture = _textureManager["fps"];

            var lineHeight = _fontManager.FontConfigurations["FiraCode"].PointSize;

            SDL.GetTextureSize(texture.SdlTexture, out var width, out var height);
            var rect = new SDL.FRect
            {
                W = width,
                H = height,
                X = 0,
                Y = Configuration.RenderedHeight - lineHeight * Configuration.Scale
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
        var y = Configuration.WindowHeight - (lineHeight * Configuration.Scale * 50);

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
