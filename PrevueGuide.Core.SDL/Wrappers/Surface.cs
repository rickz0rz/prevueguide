namespace PrevueGuide.Core.SDL.Wrappers;

public class Surface : IDisposable
{
    public nint SdlSurface { get; }

    public Surface(nint sdlSurface)
    {
        SdlSurface = sdlSurface;
    }

    public void Dispose()
    {
        SDL3.SDL.DestroySurface(SdlSurface);
    }
}
