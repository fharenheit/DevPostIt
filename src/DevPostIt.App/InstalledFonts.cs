using System.Text.RegularExpressions;
using System.Windows.Media;

namespace DevPostIt.App;

// 페이지에 글꼴 조회 권한을 주지 않고 앱이 설치된 글꼴 이름만 넘깁니다.
internal static partial class InstalledFonts
{
    private const int MaxFonts = 1000;
    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,100}$")] private static partial Regex SafeName();

    public static (string[] Monospace, string[] All) Read()
    {
        var all = new List<string>();
        var monospace = new List<string>();
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(item => item.Source, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = family.Source;
            if (all.Count >= MaxFonts) break;
            if (name.StartsWith('@') || !SafeName().IsMatch(name)) continue;
            all.Add(name);
            if (IsMonospace(family)) monospace.Add(name);
        }
        return (monospace.ToArray(), all.ToArray());
    }
    private static bool IsMonospace(System.Windows.Media.FontFamily family)
    {
        try
        {
            var typeface = family.GetTypefaces().FirstOrDefault();
            if (typeface is null || !typeface.TryGetGlyphTypeface(out var glyphs)) return false;
            return glyphs.CharacterToGlyphMap.TryGetValue('i', out var narrow) && glyphs.CharacterToGlyphMap.TryGetValue('W', out var wide) &&
                Math.Abs(glyphs.AdvanceWidths[narrow] - glyphs.AdvanceWidths[wide]) < 0.001;
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
