using SDL2;

namespace PrevueGuide.SDLWrappers;

public class RenderingTarget : IDisposable
{
    private readonly IntPtr _renderer;
    private readonly IntPtr _previousRenderTarget;

    public RenderingTarget(IntPtr renderer, IntPtr texture)
    {
        _renderer = renderer;
        _previousRenderTarget = SDL.SDL_GetRenderTarget(_renderer);
        _ = SDL.SDL_SetRenderTarget(_renderer, texture);
    }

    public void Dispose()
    {
        _ = SDL.SDL_SetRenderTarget(_renderer, _previousRenderTarget);
    }
}
