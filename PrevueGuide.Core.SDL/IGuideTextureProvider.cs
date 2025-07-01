using PrevueGuide.Core.Model;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL;

// This interface will define methods for generating channel label textures, channel programming
// data textures, time textures, and basically anything else that is related to a program guide.
public interface IGuideTextureProvider
{
    SDL3.SDL.Color DefaultGuideBackground { get; }
    int DefaultWindowWidth { get; }
    int DefaultWindowHeight { get; }
    FullscreenMode DefaultFullscreenMode { get; }

    void SetRenderer(nint renderer);

    Texture GenerateListingTexture(Listing listing, DateTime firstColumnStartTime);
}
