using Microsoft.Extensions.Logging;
using PrevueGuide.Core.SDL.Wrappers;

namespace PrevueGuide.Core.SDL;

public class TextureManager : IDisposable
{
    private static readonly Dictionary<string, string> FallbackSizeMap = new()
    {
        { "2x_smooth", "2x"},
        { "2x", "1x"}
    };

    private readonly ILogger _logger;
    private readonly Dictionary<(string key, string size), Texture?> _textureMap;
    private readonly string _preferredSize;

    private string GetAvailableSize(string key)
    {
        if (_textureMap.ContainsKey((key, _preferredSize)))
            return _preferredSize;

        var targetSize = _preferredSize;

        while(FallbackSizeMap.ContainsKey(targetSize))
        {
            targetSize = FallbackSizeMap[targetSize];
            _logger.LogInformation($@"[Assets] Unable to find size for {key}, testing size {targetSize}");

            if (_textureMap.ContainsKey((key, targetSize)))
                return targetSize;
        }

        throw new Exception($"Asset {key} missing preferred size {_preferredSize} and fallbacks.");
    }

    public TextureManager(ILogger logger, string preferredSize)
    {
        _logger = logger;
        _preferredSize = preferredSize;

        _logger.LogInformation("[Assets] Using preferred size: {preferredSize}", _preferredSize);

        _textureMap = new Dictionary<(string key, string size), Texture?>();

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
