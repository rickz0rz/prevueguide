namespace PrevueGuide.Core.SDL;

public class FontSizeManager : IDisposable
{
    private nint _engine;
    private nint _font;
    private Dictionary<string, (int width, int height)> _map;

    public FontSizeManager(nint font)
    {
        _engine = SDL3.TTF.CreateSurfaceTextEngine();
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
                var text = SDL3.TTF.CreateText(_engine, _font, key, 0);
                var didThing = SDL3.TTF.GetTextSize(text, out var w, out var h);
                _map[key] = (w, h);
                SDL3.TTF.DestroyText(text);
            }

            return _map[key];
        }
    }

    public void Dispose()
    {
        SDL3.TTF.DestroySurfaceTextEngine(_engine);
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
