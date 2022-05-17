using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL;

public class TextureManager : IDisposable
{
    private readonly Dictionary<string, Texture?> _textureMap;

    public TextureManager()
    {
        _textureMap = new Dictionary<string, Texture?>();
    }

    public Texture? this[string key]
    {
        get => _textureMap[key];
        set => _textureMap[key] = value;
    }

    public void Dispose()
    {
        foreach (var k in _textureMap.Keys)
        {
            _textureMap[k]?.Dispose();
        }
    }
}
