using System.Globalization;
using System.Text;
using MyFashion.Core.Profile;

namespace MyFashion.Core.Recommendations;

/// <summary>Writes a compact, model-friendly summary of a style profile and the user's preferences.</summary>
public static class ProfilePromptWriter
{
    public static string Write(StyleProfile profile, UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(preferences);
        var c = CultureInfo.InvariantCulture;
        var b = new StringBuilder();

        b.AppendLine(c, $"Analyzed images with outfits: {profile.ImagesWithOutfit}, garments: {profile.GarmentCount}.");
        b.AppendLine("Color palette: " + string.Join(", ", profile.Palette.Take(8).Select(p => $"{p.Color} {p.Percent.ToString(c)}%")));
        foreach (var category in profile.Categories.Take(7))
        {
            b.AppendLine(c, $"{category.Category} ({category.Percent.ToString(c)}%): {string.Join(", ", category.TopTypes.Select(t => $"{t.Name} x{t.Count}"))}");
        }

        if (profile.Brands.Count > 0)
        {
            b.AppendLine("Brands seen: " + string.Join(", ", profile.Brands.Take(10).Select(x => $"{x.Brand} x{x.Count}")));
        }

        b.AppendLine("Style tags: " + string.Join(", ", profile.StyleTags.Take(6).Select(t => $"{t.Name} {t.Percent.ToString(c)}%")));
        b.AppendLine("Fits: " + string.Join(", ", profile.Fits.Take(4).Select(f => $"{f.Name} {f.Percent.ToString(c)}%")));
        b.AppendLine("Patterns: " + string.Join(", ", profile.Patterns.Take(4).Select(f => $"{f.Name} {f.Percent.ToString(c)}%")));
        if (profile.SignaturePieces.Count > 0)
        {
            b.AppendLine("Signature pieces: " + string.Join(", ", profile.SignaturePieces.Select(s => $"{s.Name} x{s.Count}")));
        }

        if (profile.Formulas.Count > 0)
        {
            b.AppendLine("Repeated outfit formulas: " + string.Join("; ", profile.Formulas.Select(f => $"{string.Join(" + ", f.Pieces)} x{f.Count}")));
        }

        b.AppendLine();
        b.AppendLine("About the user who wants to adopt this style:");
        b.AppendLine("Gender: " + Or(preferences.Gender));
        b.AppendLine("Sizes: " + Or(preferences.Sizes));
        b.AppendLine("Budget: " + Or(preferences.Budget));
        b.AppendLine("Notes: " + Or(preferences.Notes));
        return b.ToString();
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "not specified" : value.Trim();
}
