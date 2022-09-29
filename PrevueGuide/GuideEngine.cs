using System.Net.Mime;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide;

public class GuideEngine : IDisposable
{
    private IntPtr _renderer;
    private ILogger _logger;
    private TextureManager _staticTextureManager;
    private TextureManager _dynamicTextureManager;

    public GuideEngine(IntPtr renderer, ILogger logger, string preferredSize)
    {
        _renderer = renderer;
        _logger = logger;
        _staticTextureManager = new TextureManager(_logger, preferredSize);

        // Create guide cells for rows just before they're needed
        // Monitor their utilization, if they are outside the visible scope then remove them
        _dynamicTextureManager = new TextureManager(_logger, preferredSize);
    }

    public void LoadStaticTextures(string directory)
    {
        var imageAssetDirectories = Directory.GetDirectories(directory);
        foreach (var imageAssetDirectory in imageAssetDirectories)
        {
            var assetSize = Path.GetFileName(imageAssetDirectory);
            foreach (var assetFile in Directory.GetFiles(imageAssetDirectory))
            {
                var noExtension = Path.GetFileNameWithoutExtension(assetFile);
                _staticTextureManager.Insert(noExtension, assetSize, new Texture(_logger, _renderer, assetFile));
            }
        }
    }

    [Obsolete]
    public Texture? RetrieveTexture(string textureName)
    {
        return _staticTextureManager[textureName];
    }

    [Obsolete]
    public TextureManager GetTextureManager()
    {
        return _staticTextureManager;
    }

    public void Dispose()
    {
        _staticTextureManager.Dispose();
    }
}
