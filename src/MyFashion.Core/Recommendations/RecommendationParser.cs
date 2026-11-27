using System.Text;
using System.Text.Json;
using MyFashion.Core.Analysis;

namespace MyFashion.Core.Recommendations;

/// <summary>Validates the model's recommendation JSON. Links are never taken from it; they are built by <see cref="StoreCatalog"/>.</summary>
public static class RecommendationParser
{
    public const int MaxShoppingItems = 10;
    public const int MaxOutfits = 6;
    private const int MaxPieces = 6;

    public static Recommendations Parse(string json, IReadOnlyList<string> stores)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AnalysisFailedException("Öneri yanıtı bir JSON nesnesi değil.");
            }

            var shopping = new List<ShoppingItem>();
            foreach (var item in Array(root, "shopping_list").Take(MaxShoppingItems))
            {
                var name = Clean(Text(item, "item"), 80);
                if (name is null)
                {
                    continue;
                }

                var query = StoreCatalog.SanitizeQuery(Text(item, "search_query"));
                if (query.Length == 0)
                {
                    query = StoreCatalog.SanitizeQuery(name);
                }

                var priority = item.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value)
                    ? Math.Clamp(value, 1, 3)
                    : 2;
                shopping.Add(new ShoppingItem(name, Clean(Text(item, "why"), 240) ?? string.Empty, priority, query, StoreCatalog.BuildLinks(query, stores)));
            }

            var outfits = new List<OutfitSuggestion>();
            foreach (var outfit in Array(root, "outfits").Take(MaxOutfits))
            {
                var pieces = Array(outfit, "pieces")
                    .Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => Clean(p.GetString(), 80))
                    .OfType<string>()
                    .Take(MaxPieces)
                    .ToList();
                var name = Clean(Text(outfit, "name"), 80);
                if (name is null || pieces.Count < 2)
                {
                    continue;
                }

                outfits.Add(new OutfitSuggestion(name, pieces, Clean(Text(outfit, "occasion"), 80) ?? string.Empty, Clean(Text(outfit, "why"), 240) ?? string.Empty));
            }

            return new Recommendations([.. shopping.OrderBy(s => s.Priority)], outfits);
        }
        catch (JsonException ex)
        {
            throw new AnalysisFailedException("Model geçerli JSON döndürmedi.", ex);
        }
    }

    private static readonly JsonElement EmptyArray = JsonSerializer.Deserialize<JsonElement>("[]");

    private static JsonElement.ArrayEnumerator Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : EmptyArray.EnumerateArray();

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsControl(ch))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(ch);
            if (builder.Length >= max)
            {
                break;
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
