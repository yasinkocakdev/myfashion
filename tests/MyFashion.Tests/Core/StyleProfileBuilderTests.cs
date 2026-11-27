using MyFashion.Core.Analysis;
using MyFashion.Core.Media;
using MyFashion.Core.Profile;
using Img = MyFashion.Core.Profile.AnalyzedImage;

namespace MyFashion.Tests.Core;

public sealed class StyleProfileBuilderTests
{
    [Fact]
    public void Empty_input_gives_an_empty_profile_without_dividing_by_zero()
    {
        var profile = StyleProfileBuilder.Build("hesap", []);

        Assert.True(profile.IsEmpty);
        Assert.Equal(0, profile.ImagesWithOutfit);
        Assert.Empty(profile.Palette);
        Assert.Empty(profile.Formulas);
    }

    [Fact]
    public void Palette_counts_garment_colors_in_descending_order()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(G(GarmentCategory.Top, "tee", "black"), G(GarmentCategory.Bottom, "jeans", "denim")),
            Img(G(GarmentCategory.Top, "shirt", "black"), G(GarmentCategory.Footwear, "sneakers", "white")),
            Img(G(GarmentCategory.Outerwear, "coat", "black")),
        ]);

        Assert.Equal(["black", "denim", "white"], profile.Palette.Select(c => c.Color));
        Assert.Equal(3, profile.Palette[0].Count);
        Assert.Equal(60.0, profile.Palette[0].Percent);
        Assert.Equal(StyleColors.Hex("black"), profile.Palette[0].Hex);
    }

    [Fact]
    public void Low_confidence_garments_are_ignored()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(G(GarmentCategory.Top, "tee", "black"), G(GarmentCategory.Top, "blur", "red", confidence: 0.1)),
        ]);

        Assert.Equal(1, profile.GarmentCount);
        Assert.DoesNotContain(profile.Palette, c => c.Color == "red");
    }

    [Fact]
    public void Brands_are_grouped_case_insensitively_with_their_evidence()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(G(GarmentCategory.Top, "tee", "white", brand: "Nike", evidence: BrandEvidence.Logo)),
            Img(G(GarmentCategory.Footwear, "sneakers", "white", brand: "nike", evidence: BrandEvidence.Text)),
            Img(G(GarmentCategory.Footwear, "runners", "black", brand: "Nike", evidence: BrandEvidence.Logo)),
            Img(G(GarmentCategory.Bag, "tote", "beige", brand: "Zara", evidence: BrandEvidence.Caption)),
        ]);

        var nike = profile.Brands[0];
        Assert.Equal("Nike", nike.Brand);
        Assert.Equal(3, nike.Count);
        Assert.Equal([BrandEvidence.Logo, BrandEvidence.Text], nike.Evidence);
        Assert.Equal("Zara", profile.Brands[1].Brand);
    }

    [Fact]
    public void Categories_list_their_most_common_types()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(G(GarmentCategory.Top, "Oversized Hoodie", "grey")),
            Img(G(GarmentCategory.Top, "oversized hoodie", "black")),
            Img(G(GarmentCategory.Top, "tee", "white")),
            Img(G(GarmentCategory.Bottom, "cargo pants", "olive")),
        ]);

        var tops = profile.Categories[0];
        Assert.Equal(GarmentCategory.Top, tops.Category);
        Assert.Equal(3, tops.Count);
        Assert.Equal(75.0, tops.Percent);
        Assert.Equal("oversized hoodie", tops.TopTypes[0].Name);
        Assert.Equal(2, tops.TopTypes[0].Count);
    }

    [Fact]
    public void Signature_pieces_need_at_least_two_appearances()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(G(GarmentCategory.Top, "hoodie", "black")),
            Img(G(GarmentCategory.Top, "hoodie", "black")),
            Img(G(GarmentCategory.Top, "hoodie", "black")),
            Img(G(GarmentCategory.Top, "hoodie", "grey")),
        ]);

        var piece = Assert.Single(profile.SignaturePieces);
        Assert.Equal("black hoodie", piece.Name);
        Assert.Equal(3, piece.Count);
    }

    [Fact]
    public void Outfit_formulas_are_repeated_color_combinations_of_main_pieces()
    {
        Img Outfit(string top, string bottom, string shoes) => Img(
            G(GarmentCategory.Top, "tee", top),
            G(GarmentCategory.Bottom, "jeans", bottom),
            G(GarmentCategory.Footwear, "sneakers", shoes),
            G(GarmentCategory.Accessory, "cap", "red"));

        var profile = StyleProfileBuilder.Build("hesap",
        [
            Outfit("black", "denim", "white"),
            Outfit("black", "denim", "white"),
            Outfit("white", "black", "black"),
        ]);

        var formula = Assert.Single(profile.Formulas);
        Assert.Equal(2, formula.Count);
        Assert.Equal(["black top", "denim bottom", "white footwear"], formula.Pieces);
    }

    [Fact]
    public void Style_tags_patterns_fits_and_kinds_are_summarized()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            Img(MediaKind.Post, ["minimal", "old money"], G(GarmentCategory.Top, "shirt", "white", fit: GarmentFit.Slim, pattern: GarmentPattern.Striped)),
            Img(MediaKind.Highlight, ["minimal"], G(GarmentCategory.Top, "shirt", "white", fit: GarmentFit.Unknown)),
            Img(MediaKind.Highlight, [], G(GarmentCategory.Bottom, "trousers", "beige", fit: GarmentFit.Regular)),
        ]);

        Assert.Equal("minimal", profile.StyleTags[0].Name);
        Assert.Equal(66.7, profile.StyleTags[0].Percent);
        Assert.Equal(2, profile.ImagesByKind[MediaKind.Highlight]);
        Assert.DoesNotContain(profile.Fits, f => f.Name == "unknown");
        Assert.Equal(2, profile.Fits.Sum(f => f.Count));
        Assert.Contains(profile.Patterns, p => p.Name == "striped" && p.Count == 1);
    }

    private static Img Img(params Garment[] garments) => Img(MediaKind.Post, [], garments);

    private static Img Img(MediaKind kind, string[] tags, params Garment[] garments) =>
        new(1, kind, "x.jpg", null, garments, tags);

    private static Garment G(
        GarmentCategory category,
        string type,
        string color,
        string? brand = null,
        BrandEvidence evidence = BrandEvidence.None,
        double confidence = 0.9,
        GarmentFit fit = GarmentFit.Regular,
        GarmentPattern pattern = GarmentPattern.Solid) =>
        new(category, type, color, pattern, fit, brand, brand is null ? BrandEvidence.None : evidence, confidence);
}
