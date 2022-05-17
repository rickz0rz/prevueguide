namespace PrevueGuide.Core.SDL.Wrappers;

public class RenderingTarget : IDisposable
{
    private readonly IntPtr _renderer;
    private readonly IntPtr _previousRenderTarget;

    public RenderingTarget(IntPtr renderer, IntPtr texture)
    {
        _renderer = renderer;
        _previousRenderTarget = SDL2.SDL.SDL_GetRenderTarget(_renderer);
        _ = SDL2.SDL.SDL_SetRenderTarget(_renderer, texture);
    }

    public void Dispose()
    {
        _ = SDL2.SDL.SDL_SetRenderTarget(_renderer, _previousRenderTarget);
    }
}
