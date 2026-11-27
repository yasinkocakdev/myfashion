using MyFashion.Core.Analysis;
using MyFashion.Core.Media;

namespace MyFashion.Core.Profile;

/// <summary>One analyzed image, as read back for profiling and reports.</summary>
public sealed record AnalyzedImage(
    int MediaId,
    MediaKind Kind,
    string? PreparedPath,
    DateTimeOffset? TakenAt,
    IReadOnlyList<Garment> Garments,
    IReadOnlyList<string> StyleTags);

/// <summary>A named count with its share in percent (one decimal).</summary>
public sealed record Share(string Name, int Count, double Percent);

public sealed record ColorShare(string Color, string Hex, int Count, double Percent);

public sealed record BrandShare(string Brand, int Count, IReadOnlyList<BrandEvidence> Evidence);

public sealed record CategoryShare(GarmentCategory Category, int Count, double Percent, IReadOnlyList<Share> TopTypes);

/// <summary>A repeated combination of main pieces, for example "black top + denim bottom + white footwear".</summary>
public sealed record OutfitFormula(IReadOnlyList<string> Pieces, int Count);

/// <summary>Everything we know about how an account dresses.</summary>
public sealed record StyleProfile(
    string Username,
    int ImagesWithOutfit,
    int GarmentCount,
    IReadOnlyDictionary<MediaKind, int> ImagesByKind,
    IReadOnlyList<ColorShare> Palette,
    IReadOnlyList<CategoryShare> Categories,
    IReadOnlyList<BrandShare> Brands,
    IReadOnlyList<Share> Patterns,
    IReadOnlyList<Share> Fits,
    IReadOnlyList<Share> StyleTags,
    IReadOnlyList<Share> SignaturePieces,
    IReadOnlyList<OutfitFormula> Formulas)
{
    public bool IsEmpty => ImagesWithOutfit == 0;
}
