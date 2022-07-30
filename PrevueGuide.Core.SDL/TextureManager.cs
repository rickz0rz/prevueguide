using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL;

public class TextureManager : IDisposable
{
    private static Dictionary<string, string> _fallBack = new Dictionary<string, string>
    {
        { "2x_smooth", "2x"},
        { "2x", "1x"}
    };

    private readonly Dictionary<(string key, string size), Texture?> _textureMap;
    private readonly string _preferredSize;

    private string GetAvailableSize(string key)
    {
        if (_textureMap.ContainsKey((key, _preferredSize)))
            return _preferredSize;

        var target = _preferredSize;

        while(_fallBack.ContainsKey(target))
        {
            target = _fallBack[target];
            if (_textureMap.ContainsKey((key, target)))
                return target;
        }

        throw new Exception($"Asset {key} missing preferred size {_preferredSize} and fallbacks.");
    }

    public TextureManager(ILogger logger, string preferredSize)
    {
        logger.LogInformation($@"[Assets] Using preferred size: {preferredSize}");

        _textureMap = new Dictionary<(string key, string size), Texture?>();
        _preferredSize = preferredSize;
    }

    public Texture? this[string key] => _textureMap[(key, GetAvailableSize(key))];

    public void Insert(string key, string size, Texture? value)
    {
        _textureMap[(key, size)] = value;
    }

    public void Dispose()
    {
        foreach (var k in _textureMap.Keys)
        {
            _textureMap[k]?.Dispose();
        }
    }
}
