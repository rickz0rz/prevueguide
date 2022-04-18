namespace PrevueGuide.SDLWrappers;

public class Surface : IDisposable
{
    public IntPtr SdlSurface { get; }

    public Surface(IntPtr sdlSurface)
    {
        SdlSurface = sdlSurface;
    }

    public void Dispose()
    {
        SDL2.SDL.SDL_FreeSurface(SdlSurface);
    }
}
