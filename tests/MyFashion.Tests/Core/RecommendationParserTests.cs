using MyFashion.Core.Analysis;
using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;

namespace MyFashion.Tests.Core;

public sealed class RecommendationParserTests
{
    private static readonly string[] Stores = [StoreCatalog.Zara, StoreCatalog.GoogleShopping];

    [Fact]
    public void Parses_items_and_outfits_and_builds_links_from_the_query()
    {
        const string json = """
            {
              "shopping_list": [
                { "item": "Bej yün kaban", "why": "Paletin ana rengi.", "priority": 2, "search_query": "bej yün kaban" },
                { "item": "Siyah loafer", "why": "Her kombinde var.", "priority": 1, "search_query": "siyah loafer ayakkabı https://evil.example/x" }
              ],
              "outfits": [
                { "name": "Ofis günü", "pieces": ["bej kaban", "beyaz gömlek", "siyah pantolon"], "occasion": "İş", "why": "Sade ve şık." }
              ]
            }
            """;

        var result = RecommendationParser.Parse(json, Stores);

        Assert.Equal(["Siyah loafer", "Bej yün kaban"], result.ShoppingList.Select(i => i.Item));
        var loafer = result.ShoppingList[0];
        Assert.Equal(2, loafer.Links.Count);
        Assert.All(loafer.Links, l => Assert.DoesNotContain("evil.example/", l.Url.AbsoluteUri, StringComparison.Ordinal));
        Assert.All(loafer.Links, l => Assert.True(l.Url.Host is "www.zara.com" or "www.google.com"));
        Assert.Equal("Ofis günü", Assert.Single(result.Outfits).Name);
    }

    [Fact]
    public void Ignores_links_the_model_tries_to_provide()
    {
        const string json = """{ "shopping_list": [ { "item": "Ceket", "why": "x", "priority": 1, "search_query": "ceket", "url": "https://phish.example" } ], "outfits": [] }""";

        var item = Assert.Single(RecommendationParser.Parse(json, Stores).ShoppingList);

        Assert.All(item.Links, l => Assert.NotEqual("phish.example", l.Url.Host));
    }

    [Fact]
    public void Drops_invalid_entries_and_clamps_values()
    {
        const string json = """
            {
              "shopping_list": [
                { "item": "", "why": "boş", "priority": 1, "search_query": "x" },
                { "item": "Gömlek", "why": "x", "priority": 99, "search_query": "" },
                { "item": 5 }
              ],
              "outfits": [
                { "name": "Tek parça", "pieces": ["ceket"], "occasion": "", "why": "" },
                { "name": "", "pieces": ["a", "b"], "occasion": "", "why": "" }
              ]
            }
            """;

        var result = RecommendationParser.Parse(json, Stores);

        var shirt = Assert.Single(result.ShoppingList);
        Assert.Equal(3, shirt.Priority);
        Assert.Equal("Gömlek", shirt.SearchQuery);
        Assert.Empty(result.Outfits);
    }

    [Fact]
    public void Limits_list_sizes()
    {
        var items = string.Join(',', Enumerable.Range(0, 30).Select(i => $$"""{ "item": "Parça {{i}}", "why": "x", "priority": 2, "search_query": "parça" }"""));
        var json = $$"""{ "shopping_list": [{{items}}], "outfits": [] }""";

        Assert.Equal(RecommendationParser.MaxShoppingItems, RecommendationParser.Parse(json, Stores).ShoppingList.Count);
    }

    [Fact]
    public void Invalid_json_throws()
    {
        Assert.Throws<AnalysisFailedException>(() => RecommendationParser.Parse("nope", Stores));
        Assert.Throws<AnalysisFailedException>(() => RecommendationParser.Parse("[]", Stores));
    }

    [Fact]
    public void Prompt_summary_contains_profile_facts_and_preferences()
    {
        var profile = StyleProfileBuilder.Build("hesap",
        [
            new AnalyzedImage(1, MyFashion.Core.Media.MediaKind.Post, null, null,
                [new Garment(GarmentCategory.Outerwear, "wool coat", "beige", GarmentPattern.Solid, GarmentFit.Oversized, "Zara", BrandEvidence.Text, 0.9)],
                ["old money"]),
        ]);

        var text = ProfilePromptWriter.Write(profile, UserPreferences.Default with { Budget = "orta" });

        Assert.Contains("beige 100%", text, StringComparison.Ordinal);
        Assert.Contains("Zara x1", text, StringComparison.Ordinal);
        Assert.Contains("Budget: orta", text, StringComparison.Ordinal);
        Assert.Contains("Gender: not specified", text, StringComparison.Ordinal);
    }
}
