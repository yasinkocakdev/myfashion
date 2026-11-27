using MyFashion.Core.Analysis;
using MyFashion.Core.Media;

namespace MyFashion.Core.Profile;

/// <summary>Turns per-image analyses into a style profile. Pure function: same input, same output.</summary>
public static class StyleProfileBuilder
{
    /// <summary>Garments below this confidence are treated as noise.</summary>
    public const double MinConfidence = 0.3;

    private const int TopTypesPerCategory = 5;
    private const int MaxSignaturePieces = 10;
    private const int MaxFormulas = 8;

    // Pieces that define an outfit's silhouette; accessories and bags are left out of formulas.
    private static readonly GarmentCategory[] FormulaCategories =
        [GarmentCategory.Outerwear, GarmentCategory.Top, GarmentCategory.Dress, GarmentCategory.Bottom, GarmentCategory.Footwear];

    public static StyleProfile Build(string username, IReadOnlyCollection<AnalyzedImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        var withOutfit = images
            .Select(i => i with { Garments = [.. i.Garments.Where(g => g.Confidence >= MinConfidence)] })
            .Where(i => i.Garments.Count > 0)
            .ToList();
        var garments = withOutfit.SelectMany(i => i.Garments).ToList();

        return new StyleProfile(
            username,
            withOutfit.Count,
            garments.Count,
            withOutfit.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Count()),
            Palette(garments),
            Categories(garments),
            Brands(garments),
            Shares(garments.Where(g => g.Pattern != GarmentPattern.Other).Select(g => Lower(g.Pattern))),
            Shares(garments.Where(g => g.Fit != GarmentFit.Unknown).Select(g => Lower(g.Fit))),
            Shares(withOutfit.SelectMany(i => i.StyleTags.Distinct()), total: withOutfit.Count),
            SignaturePieces(garments),
            Formulas(withOutfit));
    }

    private static List<ColorShare> Palette(List<Garment> garments) =>
        [.. Shares(garments.Select(g => g.Color)).Select(s => new ColorShare(s.Name, StyleColors.Hex(s.Name), s.Count, s.Percent))];

    private static List<CategoryShare> Categories(List<Garment> garments) =>
        [.. garments
            .GroupBy(g => g.Category)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => new CategoryShare(
                g.Key,
                g.Count(),
                Percent(g.Count(), garments.Count),
                [.. Shares(g.Select(x => x.Type.ToLowerInvariant())).Take(TopTypesPerCategory)]))];

    private static List<BrandShare> Brands(List<Garment> garments) =>
        [.. garments
            .Where(g => g.Brand is not null)
            .GroupBy(g => g.Brand!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BrandShare(
                // Show the spelling used most often ("Nike" rather than "nike").
                g.GroupBy(x => x.Brand!, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key,
                g.Count(),
                [.. g.Select(x => x.BrandEvidence).Distinct().Order()]))
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Brand, StringComparer.OrdinalIgnoreCase)];

    private static List<Share> SignaturePieces(List<Garment> garments) =>
        [.. Shares(garments.Select(g => $"{g.Color} {g.Type.ToLowerInvariant()}"))
            .Where(s => s.Count >= 2)
            .Take(MaxSignaturePieces)];

    private static List<OutfitFormula> Formulas(List<AnalyzedImage> images)
    {
        var keys = images
            .Select(image => FormulaCategories
                .Select(category => image.Garments.FirstOrDefault(g => g.Category == category))
                .OfType<Garment>()
                .Select(g => $"{g.Color} {Lower(g.Category)}")
                .ToList())
            .Where(pieces => pieces.Count >= 2)
            .Select(pieces => string.Join(" + ", pieces));

        return [.. keys
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(MaxFormulas)
            .Select(g => new OutfitFormula(g.Key.Split(" + "), g.Count()))];
    }

    private static List<Share> Shares(IEnumerable<string> values, int? total = null)
    {
        var list = values.ToList();
        var denominator = total ?? list.Count;
        return [.. list
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Share(g.Key, g.Count(), Percent(g.Count(), denominator)))];
    }

    private static double Percent(int count, int total) => total == 0 ? 0 : Math.Round(count * 100.0 / total, 1);

    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}
