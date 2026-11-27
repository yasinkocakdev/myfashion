using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MyFashion.Core.Analysis;
using MyFashion.Core.Media;
using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;
using MyFashion.Infrastructure.Ollama;

namespace MyFashion.Tests.Infrastructure;

public sealed class OllamaRecommendationGeneratorTests
{
    private static readonly StyleProfile Profile = StyleProfileBuilder.Build("hesap",
    [
        new AnalyzedImage(1, MediaKind.Post, null, null,
            [new Garment(GarmentCategory.Top, "hoodie", "black", GarmentPattern.Solid, GarmentFit.Oversized, null, BrandEvidence.None, 0.9)],
            ["streetwear"]),
    ]);

    [Fact]
    public async Task Empty_profile_returns_no_suggestions_without_calling_the_model()
    {
        var calls = 0;
        var generator = Create(_ => { calls++; return Reply("{}"); });

        var result = await generator.GenerateAsync(StyleProfileBuilder.Build("hesap", []), UserPreferences.Default, TestContext.Current.CancellationToken);

        Assert.Same(Recommendations.Empty, result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Sends_the_profile_summary_and_parses_suggestions()
    {
        string? body = null;
        var generator = Create(request =>
        {
            body = request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return Reply("""{"shopping_list":[{"item":"Siyah oversize hoodie","why":"İmza parça.","priority":1,"search_query":"siyah oversize hoodie"}],"outfits":[]}""");
        });

        var result = await generator.GenerateAsync(Profile, UserPreferences.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Siyah oversize hoodie", Assert.Single(result.ShoppingList).Item);
        var request = JsonNode.Parse(body!)!;
        Assert.Contains("black 100%", (string)request["messages"]![1]!["content"]!, StringComparison.Ordinal);
        Assert.Null(request["messages"]![1]!["images"]);
        Assert.Equal("object", (string?)request["format"]!["type"]);
    }

    [Fact]
    public async Task Unusable_answers_fail_after_retrying()
    {
        var calls = 0;
        var generator = Create(_ => { calls++; return Reply("not json"); });

        await Assert.ThrowsAsync<AnalysisFailedException>(() =>
            generator.GenerateAsync(Profile, UserPreferences.Default, TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    private static OllamaRecommendationGenerator Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = new OllamaOptions();
        return new OllamaRecommendationGenerator(new OllamaChatClient(new HttpClient(new Handler(respond)), options), options);
    }

    private static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }.ToJsonString(),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
