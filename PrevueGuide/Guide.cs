using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using SDL3;

namespace PrevueGuide;

public class Guide : IDisposable
{
    private const int DefaultWindowWidth = 716;
    private const int DefaultWindowHeight = 436;

    private int _windowWidth = DefaultWindowWidth;
    private int _windowHeight = DefaultWindowHeight;

    private IntPtr _window;
    private IntPtr _renderer;

    private readonly ILogger _logger;
    private bool _running;
    private int _scale;
    private int _vsync;

    private Texture frame;

    public Guide(ILogger logger)
    {
        _logger = logger;
        _vsync = 1;
    }

    public void Run()
    {
        Setup();
        _running = true;

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
            _windowWidth,
            _windowHeight,
            SDL.WindowFlags.HighPixelDensity /* | SDL.WindowFlags.Resizable */);

        SDL.GetWindowSizeInPixels(_window, out var windowWidthPixels, out var windowHeightPixels);
        _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", windowWidthPixels, windowHeightPixels);
        _scale = windowWidthPixels / _windowWidth;
        _logger.LogInformation(@"[Window] Scale: {Scale}x", _scale);

        if (_window == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the window. {SDL.GetError()}");
        }

        _renderer = SDL.CreateRenderer(_window, "");
        SDL.SetRenderVSync(_renderer, _vsync);

        if (_renderer == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the renderer. {SDL.GetError()}");
        }

        _ = SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);

        // testing

        frame = CreateScaledEmptyFrame(100, 100, new SDL.Color { A = 255, R = 0, G = 192, B = 0 });
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
                if (sdlEvent.Key.Key == SDL.Keycode.V)
                {
                    _vsync = _vsync == 0 ? 1 : 0;
                }

                _logger.LogInformation($"SDL: Setting vsync to: {_vsync}");
                SDL.SetRenderVSync(_renderer, _vsync);
            }
        }
    }

    private void Render()
    {
        _ = SDL.SetRenderDrawColor(_renderer, 0, 0, 128, 255);
        _ = SDL.RenderClear(_renderer);

        var dstRect = new ScaledFRect { H = 100, W = 100, X = 0, Y = 0, Scale = _scale };
        SDL.RenderTexture(_renderer, frame.SdlTexture, IntPtr.Zero, dstRect.ToFRect());

        SDL.RenderPresent(_renderer);
    }

    private Texture CreateScaledEmptyFrame(int width, int height, SDL.Color backgroundColor)
    {
        var oldRenderTarget = SDL.GetRenderTarget(_renderer);
        var texture = new Texture(_renderer, width * _scale, height * _scale);

        _ = SDL.SetRenderTarget(_renderer, texture.SdlTexture);

        _ = SDL.SetRenderDrawColor(_renderer, backgroundColor.R, backgroundColor.G, backgroundColor.B, backgroundColor.A);
        _ = SDL.RenderClear(_renderer);

        // Top and left edges.
        _ = SDL.SetRenderDrawColor(_renderer, 255, 255, 255, 255);
        foreach (var fRect in new[]
                 {
                     // top
                     new ScaledFRect { H = 4, W = width - 4, X = 0, Y = 0, Scale = _scale },
                     // left
                     new ScaledFRect { H = height - 8, W = 4, X = 0, Y = 4, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, fRect.ToFRect());
        }

        // bottom and right edges.
        _ = SDL.SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        foreach (var fRect in new[]
                 {
                     // bottom
                     new ScaledFRect { H = 4, W = width - 4, X = 4, Y = height - 4, Scale = _scale },
                     // right
                     new ScaledFRect { H = height - 8, W = 4, X = width - 4, Y = 4, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, fRect.ToFRect());
        }

        _ = SDL.SetRenderTarget(_renderer, oldRenderTarget);

        return texture;
    }

    public void Dispose()
    {
        TTF.Quit();
        SDL.DestroyRenderer(_renderer);
        SDL.DestroyWindow(_window);
        SDL.Quit();
    }
}
