using SDL2;

namespace PrevueGuide.Core.SDL;

public class FontSizeManager
{
    private IntPtr _font;
    private Dictionary<string, (int width, int height)> _map;

    public FontSizeManager(IntPtr font)
    {
        _font = font;
        _map = new Dictionary<string, (int width, int height)>();
    }

    public (int width, int height) this[char key] => this[$"{key}"];

    public (int width, int height) this[string key]
    {
        get
        {
            if (!_map.ContainsKey(key))
            {
                _ = SDL_ttf.TTF_SizeUNICODE(_font, key, out var w, out var h);
                _map[key] = (w, h);
            }

            return _map[key];
        }
    }
}

// get => _textureMap[key];
// set => _textureMap[key] = value;

// // Generate a font width map.
// for (var i = 0; i < 256; i++)
// {
//     var c = (char)i;
//     _ = TTF_SizeUNICODE(openedTtfFont, $"{c}", out var w, out _);
//     _ = TTF_SizeText(openedTtfFont, $"{c}", out var w, out _);
//     fontMap[c] = w;
// }
