using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL;

public class TextureManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Texture?> _textureMap;

    public Texture? this[string key]
    {
        get => _textureMap[key];
        set => _textureMap[key] = value;
    }

    public TextureManager(ILogger logger)
    {
        _logger = logger;
        _textureMap = new ConcurrentDictionary<string, Texture?>();
    }

    public void PurgeTexture(string key)
    {
        if (_textureMap.TryRemove(key, out var texture))
        {
            texture?.Dispose();
        }
    }

    public void PurgeTextures()
    {
        foreach (var key in _textureMap.Keys)
        {
            if (!_textureMap.TryRemove(key, out var texture))
                continue;

            try
            {
                texture?.Dispose();
            }
            catch (Exception _)
            {
                // ignored for now.
            }
        }

        _textureMap.Clear();
    }

    public void Dispose()
    {
        PurgeTextures();
    }
}
