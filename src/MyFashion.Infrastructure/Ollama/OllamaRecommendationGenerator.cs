using System.Text.Json.Nodes;
using MyFashion.Core.Analysis;
using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;

namespace MyFashion.Infrastructure.Ollama;

/// <summary>Asks the local model for a shopping list and outfit ideas based on a style profile.</summary>
public sealed class OllamaRecommendationGenerator(OllamaChatClient client, OllamaOptions options) : IRecommendationGenerator
{
    internal const string SystemPrompt =
        """
        You are a personal stylist. You get a statistical summary of how someone dresses and a few facts about the user
        who wants to adopt that style. Suggest what the user should buy first and how to combine pieces.
        Rules:
        - Write every text field in Turkish.
        - shopping_list: at most 10 concrete items, most impactful first, priority 1 (buy first) to 3 (nice to have).
          Base them on the palette, signature pieces and formulas. "why" is one short sentence.
          search_query is 2-6 plain Turkish shopping words (no brand unless it appears in the summary, no URLs).
        - outfits: at most 6 complete outfits, each with 3-6 pieces described with color and type, an occasion and one short "why".
        - Never invent brands, prices, links or facts about the person.
        Answer only with JSON that matches the schema.
        """;

    internal static readonly JsonNode Schema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["shopping_list"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = RecommendationParser.MaxShoppingItems,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["item"] = new JsonObject { ["type"] = "string" },
                        ["why"] = new JsonObject { ["type"] = "string" },
                        ["priority"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 3 },
                        ["search_query"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("item", "why", "priority", "search_query"),
                },
            },
            ["outfits"] = new JsonObject
            {
                ["type"] = "array",
                ["maxItems"] = RecommendationParser.MaxOutfits,
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["pieces"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                        ["occasion"] = new JsonObject { ["type"] = "string" },
                        ["why"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("name", "pieces", "occasion", "why"),
                },
            },
        },
        ["required"] = new JsonArray("shopping_list", "outfits"),
    };

    public async Task<Recommendations> GenerateAsync(StyleProfile profile, UserPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(preferences);
        if (profile.IsEmpty)
        {
            return Recommendations.Empty;
        }

        OllamaMessage[] messages =
        [
            new("system", SystemPrompt),
            new("user", ProfilePromptWriter.Write(profile, preferences)),
        ];

        Exception? last = null;
        for (var attempt = 1; attempt <= Math.Max(1, options.MaxAttempts); attempt++)
        {
            try
            {
                var content = await client.ChatAsync(options.VisionModel, messages, Schema, cancellationToken).ConfigureAwait(false);
                return RecommendationParser.Parse(content, preferences.Stores);
            }
            catch (OllamaException ex) when (ex.IsTransient)
            {
                last = ex;
            }
            catch (AnalysisFailedException ex)
            {
                last = ex;
            }
            catch (OllamaException ex)
            {
                throw new AnalysisFailedException(ex.Message, ex);
            }
        }

        throw new AnalysisFailedException(last?.Message ?? "Öneriler oluşturulamadı.", last!);
    }
}
