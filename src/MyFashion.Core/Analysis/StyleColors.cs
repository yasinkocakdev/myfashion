namespace MyFashion.Core.Analysis;

/// <summary>
/// A fixed palette of canonical color names. Model output and user input are mapped onto it
/// so that "jet black", "Black" and "siyah" count as the same color in statistics.
/// </summary>
public static class StyleColors
{
    public const string Other = "other";

    // Canonical name -> display hex (used for palette swatches in the report).
    private static readonly Dictionary<string, string> Palette = new(StringComparer.Ordinal)
    {
        ["black"] = "#111111",
        ["white"] = "#F5F5F5",
        ["cream"] = "#F2E8D5",
        ["grey"] = "#8A8D91",
        ["beige"] = "#D2B48C",
        ["brown"] = "#6B4423",
        ["navy"] = "#1F2A44",
        ["blue"] = "#2F5DA8",
        ["light blue"] = "#9CC3E6",
        ["denim"] = "#4A6A8A",
        ["green"] = "#2E7D4F",
        ["olive"] = "#6B6B2E",
        ["khaki"] = "#BDB07A",
        ["red"] = "#C62828",
        ["burgundy"] = "#6D1A2A",
        ["pink"] = "#E8A0B4",
        ["purple"] = "#6A3D9A",
        ["yellow"] = "#E6C229",
        ["orange"] = "#E07B24",
        ["gold"] = "#C9A227",
        ["silver"] = "#B8BCC2",
        ["multicolor"] = "#9E9E9E",
        [Other] = "#5C5F66",
    };

    // Synonyms (English and Turkish) -> canonical name. Checked as whole phrase first, then by keyword.
    private static readonly (string Keyword, string Canonical)[] Synonyms =
    [
        ("off-white", "white"), ("off white", "white"), ("ivory", "cream"), ("ecru", "cream"), ("krem", "cream"),
        ("charcoal", "grey"), ("gray", "grey"), ("gri", "grey"), ("anthracite", "grey"), ("antrasit", "grey"),
        ("navy", "navy"), ("lacivert", "navy"), ("light blue", "light blue"), ("sky blue", "light blue"), ("açık mavi", "light blue"),
        ("baby blue", "light blue"), ("denim", "denim"), ("indigo", "denim"), ("kot", "denim"),
        ("camel", "beige"), ("tan", "beige"), ("sand", "beige"), ("taupe", "beige"), ("bej", "beige"), ("nude", "beige"),
        ("chocolate", "brown"), ("kahverengi", "brown"), ("kahve", "brown"), ("cognac", "brown"),
        ("olive", "olive"), ("haki", "khaki"), ("khaki", "khaki"),
        ("burgundy", "burgundy"), ("bordo", "burgundy"), ("maroon", "burgundy"), ("wine", "burgundy"),
        ("pembe", "pink"), ("blush", "pink"), ("mor", "purple"), ("lilac", "purple"), ("lavender", "purple"),
        ("sarı", "yellow"), ("mustard", "yellow"), ("turuncu", "orange"), ("rust", "orange"),
        ("siyah", "black"), ("beyaz", "white"), ("kırmızı", "red"), ("mavi", "blue"), ("yeşil", "green"),
        ("altın", "gold"), ("gümüş", "silver"), ("multi", "multicolor"), ("çok renkli", "multicolor"),
    ];

    /// <summary>All canonical color names, including <see cref="Other"/>.</summary>
    public static IReadOnlyCollection<string> All => Palette.Keys;

    /// <summary>Maps a free-text color to a canonical name, or <see cref="Other"/>.</summary>
    public static string Normalize(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return Other;
        }

        var text = color.Trim().ToLowerInvariant();
        if (Palette.ContainsKey(text))
        {
            return text;
        }

        foreach (var (keyword, canonical) in Synonyms)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
            {
                return canonical;
            }
        }

        // Last resort: a canonical single-word name inside a longer phrase ("jet black", "olive green").
        foreach (var name in Palette.Keys)
        {
            if (!name.Contains(' ', StringComparison.Ordinal) && name != Other
                && text.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries).Contains(name))
            {
                return name;
            }
        }

        return Other;
    }

    /// <summary>The display hex for a canonical color name (falls back to the "other" swatch).</summary>
    public static string Hex(string canonicalName) =>
        Palette.TryGetValue(canonicalName, out var hex) ? hex : Palette[Other];
}
