using MyFashion.Core.Analysis;

namespace MyFashion.Tests.Core;

public sealed class ModelOutputParserTests
{
    [Fact]
    public void Parses_a_valid_response()
    {
        const string json = """
            {
              "has_outfit": true,
              "garments": [
                { "category": "outerwear", "type": "Oversized wool coat", "color": "camel", "pattern": "solid",
                  "fit": "oversized", "brand": "Zara", "brand_evidence": "text", "confidence": 0.82 },
                { "category": "footwear", "type": "loafers", "color": "jet black", "pattern": "solid",
                  "fit": "unknown", "brand": "", "brand_evidence": "none", "confidence": 0.9 }
              ],
              "style_tags": ["Old-Money", "minimal"]
            }
            """;

        var result = ModelOutputParser.Parse(json);

        Assert.True(result.HasOutfit);
        Assert.Equal(2, result.Garments.Count);
        var coat = result.Garments[0];
        Assert.Equal(GarmentCategory.Outerwear, coat.Category);
        Assert.Equal("oversized wool coat", coat.Type);
        Assert.Equal("beige", coat.Color);
        Assert.Equal(GarmentFit.Oversized, coat.Fit);
        Assert.Equal("Zara", coat.Brand);
        Assert.Equal(BrandEvidence.Text, coat.BrandEvidence);
        Assert.Equal(0.82, coat.Confidence, 3);
        Assert.Equal("black", result.Garments[1].Color);
        Assert.Null(result.Garments[1].Brand);
        Assert.Equal(["old money", "minimal"], result.StyleTags);
    }

    [Fact]
    public void Drops_brands_without_evidence_and_placeholder_brands()
    {
        const string json = """
            { "has_outfit": true, "style_tags": [], "garments": [
              { "category": "top", "type": "t-shirt", "color": "white", "pattern": "solid", "fit": "regular",
                "brand": "Gucci", "brand_evidence": "none", "confidence": 0.5 },
              { "category": "top", "type": "shirt", "color": "white", "pattern": "solid", "fit": "regular",
                "brand": "unknown", "brand_evidence": "logo", "confidence": 0.5 },
              { "category": "top", "type": "polo", "color": "white", "pattern": "solid", "fit": "regular",
                "brand": "N/A", "brand_evidence": "text", "confidence": 0.5 }
            ] }
            """;

        var result = ModelOutputParser.Parse(json);

        Assert.All(result.Garments, g => Assert.Null(g.Brand));
        Assert.All(result.Garments, g => Assert.Equal(BrandEvidence.None, g.BrandEvidence));
    }

    [Fact]
    public void Maps_unknown_values_to_safe_defaults_and_clamps_numbers()
    {
        const string json = """
            { "has_outfit": true, "style_tags": ["galactic", "streetwear", "streetwear"], "garments": [
              { "category": "spacesuit", "type": "thing", "color": "ultraviolet", "pattern": "plaid-ish",
                "fit": "baggy?", "brand": "", "brand_evidence": "vibes", "confidence": 7 },
              { "category": "bag", "type": "tote", "color": "red", "pattern": "solid", "fit": "regular",
                "brand": "", "brand_evidence": "none", "confidence": -1 }
            ] }
            """;

        var result = ModelOutputParser.Parse(json);

        var first = result.Garments[0];
        Assert.Equal(GarmentCategory.Other, first.Category);
        Assert.Equal(StyleColors.Other, first.Color);
        Assert.Equal(GarmentPattern.Other, first.Pattern);
        Assert.Equal(GarmentFit.Unknown, first.Fit);
        Assert.Equal(1.0, first.Confidence);
        Assert.Equal(0.0, result.Garments[1].Confidence);
        Assert.Equal(["streetwear"], result.StyleTags);
    }

    [Fact]
    public void Sanitizes_free_text_and_limits_sizes()
    {
        var longType = new string('x', 500);
        var garments = string.Join(',', Enumerable.Range(0, 30).Select(_ =>
            $$"""{ "category": "top", "type": "{{longType}}\u0007\n<script>", "color": "red", "pattern": "solid", "fit": "regular", "brand": "Br\u0000and<b>", "brand_evidence": "logo", "confidence": 0.5 }"""));
        var json = $$"""{ "has_outfit": true, "style_tags": [], "garments": [{{garments}}] }""";

        var result = ModelOutputParser.Parse(json);

        Assert.Equal(ModelOutputParser.MaxGarments, result.Garments.Count);
        var g = result.Garments[0];
        Assert.True(g.Type.Length <= ModelOutputParser.MaxTextLength);
        Assert.DoesNotContain('\u0007', g.Type);
        Assert.DoesNotContain('\n', g.Type);
        Assert.Equal("Brand<b>", g.Brand);
    }

    [Fact]
    public void No_outfit_means_no_garments()
    {
        const string json = """
            { "has_outfit": false, "style_tags": ["minimal"], "garments": [
              { "category": "top", "type": "tee", "color": "red", "pattern": "solid", "fit": "regular",
                "brand": "", "brand_evidence": "none", "confidence": 0.5 } ] }
            """;

        var result = ModelOutputParser.Parse(json);

        Assert.False(result.HasOutfit);
        Assert.Empty(result.Garments);
        Assert.Empty(result.StyleTags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"garments\": \"nope\"}")]
    [InlineData("{\"has_outfit\": true, \"garments\": [ { \"category\": 5 } ]}")]
    public void Unusable_output_throws(string json)
    {
        Assert.Throws<AnalysisFailedException>(() => ModelOutputParser.Parse(json));
    }

    [Fact]
    public void Missing_optional_fields_are_tolerated()
    {
        var result = ModelOutputParser.Parse("""{ "has_outfit": true, "garments": [ { "category": "dress", "type": "midi dress", "color": "green" } ] }""");

        var dress = Assert.Single(result.Garments);
        Assert.Equal(GarmentCategory.Dress, dress.Category);
        Assert.Equal(GarmentFit.Unknown, dress.Fit);
        Assert.Equal(GarmentPattern.Other, dress.Pattern);
        Assert.Equal(0.5, dress.Confidence);
    }
}
