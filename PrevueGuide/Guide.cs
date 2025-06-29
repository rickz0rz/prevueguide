using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;
using SDL3;

namespace PrevueGuide;

public class Guide : IDisposable
{
    private const int DefaultWindowWidth = 716;
    private const int DefaultWindowHeight = 436;

    private readonly int _windowWidth = DefaultWindowWidth;
    private readonly int _windowHeight = DefaultWindowHeight;

    private readonly SDL.Color _gridDefaultBlue = new() { A = 255, R = 3, G = 0, B = 88 };

    private IntPtr _window;
    private IntPtr _renderer;

    private readonly ILogger _logger;
    private bool _running;
    private int _scale;

    // private bool _useAntiAliasing = true;
    private bool _useHighDpi = true;
    private bool _useVSync = true;

    private Texture frame;

    public Guide(ILogger logger)
    {
        _logger = logger;
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

        // I guess there's no anti-aliasing with RenderGeometry?
        // SDL.SetHint(SDL.Hints.RenderLineMethod, "1");
        // SDL.GLSetAttribute(SDL.GLAttr.MultisampleSamples, 4);
        // SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "2");
        // SDL_SetHint(SDL_HINT_RENDER_LINE_METHOD, "3");

        _ = TTF.Init();

        _window = SDL.CreateWindow("Prevue Guide",
            _windowWidth,
            _windowHeight,
            _useHighDpi ? SDL.WindowFlags.HighPixelDensity : 0);

        SDL.GetWindowSizeInPixels(_window, out var windowWidthPixels, out var windowHeightPixels);
        _logger.LogInformation(@"[Window] Drawable Size: {Width} x {Height}", windowWidthPixels, windowHeightPixels);
        _scale = windowWidthPixels / _windowWidth;
        _logger.LogInformation(@"[Window] Scale: {Scale}x", _scale);

        if (_window == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the window. {SDL.GetError()}");
        }

        _renderer = SDL.CreateRenderer(_window, "");
        SDL.SetRenderVSync(_renderer, _useVSync ? 1 : 0);

        if (_renderer == IntPtr.Zero)
        {
            throw new Exception($"There was an issue creating the renderer. {SDL.GetError()}");
        }

        _ = SDL.SetRenderDrawBlendMode(_renderer, SDL.BlendMode.Blend);

        // testing

        frame = CreateEmptyFrame(100, 100, _gridDefaultBlue);
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
                    _useVSync = !_useVSync;
                }

                _logger.LogInformation($"SDL: Setting vsync to: {_useVSync}");
                SDL.SetRenderVSync(_renderer, _useVSync ? 1 : 0);
            }
        }
    }

    private void Render()
    {
        _ = SDL3Temp.SetRenderDrawColor(_renderer, _gridDefaultBlue);
        _ = SDL.RenderClear(_renderer);

        var dstRect = new ScaledFRect { H = 100, W = 100, X = 0, Y = 0, Scale = _scale };
        _ = SDL.RenderTexture(_renderer, frame.SdlTexture, IntPtr.Zero, dstRect.ToFRect());

        _ = SDL.RenderPresent(_renderer);
    }

    // Maybe split this into a function that takes a texture and draws a frame from a rect?
    // Might make it easier when drawing the clock where we have to draw 3 separate frames...

    private void CreateBevelOnTexture(Texture texture, SDL.Rect rect, int bevelSize = 4)
    {
        var frameBevelHighlight = new SDL.Color { A = 255, B = 170, G = 170, R = 170 };
        var frameBevelShadow = new SDL.Color { A = 255, B = 0, G = 0, R = 0 };
        var frameBevelShadowCorner = new SDL.Color { A = 255, B = 85, G = 85, R = 85 };

        var oldRenderTarget = SDL.GetRenderTarget(_renderer);
        _ = SDL.SetRenderTarget(_renderer, texture.SdlTexture);

        // Highlight: Top and left edges.
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelHighlight);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize, W = rect.W - bevelSize, X = rect.X, Y = rect.Y, Scale = _scale },
                     new ScaledFRect { H = rect.H - (bevelSize * 2), W = bevelSize, X = rect.X, Y = rect.Y + bevelSize, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, scaledFRect.ToFRect());
        }

        // Shadow: bottom and right edges.
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelShadow);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize, W = rect.W, X = rect.X, Y = rect.Y + rect.H - bevelSize, Scale = _scale },
                     new ScaledFRect { H = rect.H, W = bevelSize, X = rect.X + rect.W - bevelSize, Y = rect.Y, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, scaledFRect.ToFRect());
        }

        // Draw white triangle, bottom left.
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelHighlight);
        var vertexListBottomLeft = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = rect.H + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = rect.H - bevelSize + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListBottomLeft.ToArray(), vertexListBottomLeft.Count, IntPtr.Zero, 0);

        // Draw white triangle, upper right.
        var vertexListUpperRight = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = bevelSize + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListUpperRight.ToArray(), vertexListUpperRight.Count, IntPtr.Zero, 0);

        // Render a block on the top-left that's going to be black to render triangles on
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelShadow);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize + 1, W = bevelSize, X = rect.X, Y = rect.Y, Scale = _scale },
                     new ScaledFRect { H = bevelSize, W = bevelSize + 1, X = rect.X, Y = rect.Y, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, scaledFRect.ToFRect());
        }

        // Draw white triangles, upper left
        var vertexListUpperLeftA = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = 1 + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = rect.X, Y = bevelSize + 1 + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + rect.X, Y = bevelSize + 1 + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListUpperLeftA.ToArray(), vertexListUpperLeftA.Count, IntPtr.Zero, 0);

        var vertexListUpperLeftB = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = 1 + rect.X, Y = rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelHighlight.ToFColor(),
                Position = new ScaledFPoint { X = bevelSize + 1 + rect.X, Y = bevelSize + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListUpperLeftB.ToArray(), vertexListUpperLeftB.Count, IntPtr.Zero, 0);

        // Render a block on the bottom-right that's going to be gray to render triangles on
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelShadowCorner);
        foreach (var scaledFRect in new[]
                 {
                     new ScaledFRect { H = bevelSize + 1, W = bevelSize, X = rect.W - bevelSize + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = _scale },
                     new ScaledFRect { H = bevelSize, W = bevelSize + 1, X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = _scale }
                 })
        {
            SDL3Temp.RenderFillRect(_renderer, scaledFRect.ToFRect());
        }

        // Draw black triangles, lower right
        _ = SDL3Temp.SetRenderDrawColor(_renderer, frameBevelShadow);
        var vertexListLowerRightA = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - 1 + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - bevelSize + rect.X, Y = rect.H - (bevelSize + 1) + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListLowerRightA.ToArray(), vertexListLowerRightA.Count, IntPtr.Zero, 0);

        var vertexListLowerRightB = new List<SDL.Vertex>
        {
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - 1 + rect.X, Y = rect.H + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H + rect.Y, Scale = _scale }.ToFPoint()
            },
            new()
            {
                Color = frameBevelShadow.ToFColor(),
                Position = new ScaledFPoint { X = rect.W - (bevelSize + 1) + rect.X, Y = rect.H - bevelSize + rect.Y, Scale = _scale }.ToFPoint()
            }
        };
        _ = SDL.RenderGeometry(_renderer, IntPtr.Zero, vertexListLowerRightB.ToArray(), vertexListLowerRightB.Count, IntPtr.Zero, 0);

        _ = SDL.SetRenderTarget(_renderer, oldRenderTarget);
    }

    private Texture CreateEmptyFrame(int width, int height, SDL.Color backgroundColor)
    {
        var texture = new Texture(_renderer, width * _scale, height * _scale);
        var oldRenderTarget = SDL.GetRenderTarget(_renderer);

        _ = SDL.SetRenderTarget(_renderer, texture.SdlTexture);
        _ = SDL3Temp.SetRenderDrawColor(_renderer, backgroundColor);
        _ = SDL.RenderClear(_renderer);

        // The rect is scaled already. Maybe make HighDPI (scale) aware render fns?
        CreateBevelOnTexture(texture, new SDL.Rect { W = width - 5, H = height - 5, X = 5, Y = 5});

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
