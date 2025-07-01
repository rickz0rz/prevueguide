namespace PrevueGuide.Core.SDL.Wrappers;

public class RenderingTarget : IDisposable
{
    private readonly IntPtr _renderer;
    private readonly IntPtr _previousRenderTarget;

    public RenderingTarget(IntPtr renderer, IntPtr texture)
    {
        _renderer = renderer;
        _previousRenderTarget = SDL3.SDL.GetRenderTarget(_renderer);
        _ = SDL3.SDL.SetRenderTarget(_renderer, texture);
    }

    public RenderingTarget(IntPtr renderer, Texture texture)
    {
        _renderer = renderer;
        _previousRenderTarget = SDL3.SDL.GetRenderTarget(_renderer);
        _ = SDL3.SDL.SetRenderTarget(_renderer, texture.SdlTexture);
    }

    public void Dispose()
    {
        _ = SDL3.SDL.SetRenderTarget(_renderer, _previousRenderTarget);
    }
}
