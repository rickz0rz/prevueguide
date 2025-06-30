using Microsoft.Extensions.Logging;

namespace PrevueGuide.Core.SDL.Wrappers;

public class Texture : IDisposable
{
    public IntPtr SdlTexture { get; }

    public Texture(IntPtr sdlTexture)
    {
        SdlTexture = sdlTexture;
    }

    public Texture(IntPtr renderer, int width, int height)
    {
        var w = (int)(width * Configuration.Scale);
        var h = (int)(height * Configuration.Scale);
        SdlTexture = SDL3.SDL.CreateTexture(renderer, SDL3.SDL.PixelFormat.RGBA8888, SDL3.SDL.TextureAccess.Target, w, h);
        _ = SDL3.SDL.SetTextureBlendMode(SdlTexture, SDL3.SDL.BlendMode.Blend);
    }

    public Texture(ILogger logger, IntPtr renderer, string filename)
    {
        using var surface = new Surface(SDL3.Image.Load(filename));

        if (surface.SdlSurface == IntPtr.Zero)
        {
            logger.LogError("There was an issue opening image \"{filename}\": {sdlError}",
                filename, SDL3.SDL.GetError());
        }

        SdlTexture = SDL3.SDL.CreateTextureFromSurface(renderer, surface.SdlSurface);

        _ = SDL3.SDL.SetTextureBlendMode(SdlTexture, SDL3.SDL.BlendMode.Blend);
    }

    public void Dispose()
    {
        SDL3.SDL.DestroyTexture(SdlTexture);
    }
}
