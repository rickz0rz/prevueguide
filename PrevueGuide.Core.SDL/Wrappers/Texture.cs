using Microsoft.Extensions.Logging;

namespace PrevueGuide.Core.SDL.Wrappers;

public class Texture : IDisposable
{
    public IntPtr SdlTexture { get; }

    public Texture(IntPtr sdlTexture)
    {
        SdlTexture = sdlTexture;
    }

    public Texture(ILogger logger, IntPtr renderer, string filename)
    {
        using var surface = new Surface(SDL2.SDL_image.IMG_Load(filename));

        if (surface.SdlSurface == IntPtr.Zero)
        {
            logger.LogError("There was an issue opening image \"{filename}\": {sdlError}",
                filename, SDL2.SDL.SDL_GetError());
        }

        SdlTexture = SDL2.SDL.SDL_CreateTextureFromSurface(renderer, surface.SdlSurface);
        _ = SDL2.SDL.SDL_SetTextureBlendMode(SdlTexture, SDL2.SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
    }

    public void Dispose()
    {
        SDL2.SDL.SDL_DestroyTexture(SdlTexture);
    }
}
