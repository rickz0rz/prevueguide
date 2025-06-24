namespace PrevueGuide.Core.Utilities;

public class Font
{
    // https://ehubsoft.herokuapp.com/fontviewer/
    private static Dictionary<string, string> TokenToStringMap = new()
    {
        { "CC", "\uf01c" },
        { "STEREO", "\uf01b" },
        { "VCRPLUS", "\uf01a" },
        { "DISNEY", "\uf01e" },
        { "G", "\uf009" },
        { "NR", "\uf008" },
        { "R", "\uf00d" },
        { "PG", "\uf00a" },
        { "PG13", "\uf00b" },
        { "TVY", "\uf00f" },
        { "TVY7", "\uf010" },
        { "NC17", "\uf00c" },
        { "TVG", "\uf011" },
        { "TV14", "\uf013" },
        { "TVPG", "\uf012" },
        { "TVM", "\uf014" },
        { "TVMA", "\uf015" },
        { "PREVUE", "\uf01d" }
    };

    public static string FormatWithFontTokens(string input)
    {
        var response = input;

        foreach (var token in TokenToStringMap.Keys)
        {
            var replacement = TokenToStringMap[token];
            response = response.Replace($"%{token}%", replacement);
        }

        response = response.Replace("%%", "%");
        return response;
    }
}
