using Guide.Core.Model;
using Guide.Core.Model.Listings;
using Guide.Core.SDL.Wrappers;

namespace Guide.Core.SDL;

// This interface will define methods for generating channel label textures, channel programming
// data textures, time textures, and basically anything else that is related to a program guide.
public interface IGuideThemeProvider
{
    SDL3.SDL.Color DefaultGuideBackground { get; } // Might remove this in favor of a "render background" method.
    int DefaultWindowWidth { get; }
    int DefaultWindowHeight { get; }
    float ScaleRatio { get; }
    FullscreenMode DefaultFullscreenMode { get; }

    void SetRenderer(nint renderer);

    IEnumerable<Texture> GenerateRows(IEnumerable<IListing> listings);
}
