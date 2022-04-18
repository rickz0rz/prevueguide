namespace PrevueGuide.SDLWrappers;

public class Texture : IDisposable
{
    public IntPtr SdlTexture { get; }

    public Texture(IntPtr sdlTexture)
    {
        SdlTexture = sdlTexture;
    }

    public Texture(IntPtr renderer, string filename)
    {
        SdlTexture = Generators.LoadImageToTexture(renderer, filename);
    }

    public void Dispose()
    {
        SDL2.SDL.SDL_DestroyTexture(SdlTexture);
    }
}
