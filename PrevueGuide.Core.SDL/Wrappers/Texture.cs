namespace PrevueGuide.Core.SDL.Wrappers;

public class Texture : IDisposable
{
    public IntPtr SdlTexture { get; }

    public Texture(IntPtr sdlTexture)
    {
        SdlTexture = sdlTexture;
    }

    public Texture(IntPtr renderer, string filename)
    {
        SdlTexture = LoadImageToTexture(renderer, filename);
    }

    public void Dispose()
    {
        SDL2.SDL.SDL_DestroyTexture(SdlTexture);
    }

    public static IntPtr LoadImageToTexture(IntPtr renderer, string filename)
    {
        // If I decide to pack everything into a zip or something...
        // var src = SDL_RWFromMem(...)
        // SDL_image.IMG_Load_RW(src, 1)

        using var surface = new Surface(SDL2.SDL_image.IMG_Load(filename));

        if (surface.SdlSurface == IntPtr.Zero)
        {
            Console.WriteLine($"There was an issue opening image \"{filename}\": {SDL2.SDL.SDL_GetError()}");
        }

        var texture = SDL2.SDL.SDL_CreateTextureFromSurface(renderer, surface.SdlSurface);
        _ = SDL2.SDL.SDL_SetTextureBlendMode(texture, SDL2.SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
        return texture;
    }
}
