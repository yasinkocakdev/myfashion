using System.Text;
using System.Text.Json;

namespace MyFashion.Core.Analysis;

/// <summary>
/// Turns the model's JSON answer into an <see cref="ImageAnalysis"/>. Model output is untrusted
/// (it can hallucinate, or be steered by text inside an image), so every field is validated:
/// enums fall back to safe defaults, text is cleaned and length-limited, numbers are clamped,
/// and brands without evidence are discarded.
/// </summary>
public static class ModelOutputParser
{
    public const int MaxGarments = 12;
    public const int MaxTextLength = 60;
    private const int MaxStyleTags = 5;

    private static readonly HashSet<string> PlaceholderBrands = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "none", "n/a", "na", "null", "no brand", "unbranded", "generic", "-", "?",
    };

    public static ImageAnalysis Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AnalysisFailedException("Model yanıtı bir JSON nesnesi değil.");
            }

            var hasOutfit = root.TryGetProperty("has_outfit", out var hasOutfitElement)
                ? hasOutfitElement.ValueKind == JsonValueKind.True
                : root.TryGetProperty("garments", out _);
            if (!hasOutfit)
            {
                return ImageAnalysis.Empty;
            }

            var garments = new List<Garment>();
            if (root.TryGetProperty("garments", out var garmentsElement))
            {
                if (garmentsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new AnalysisFailedException("Model yanıtında 'garments' bir liste değil.");
                }

                foreach (var item in garmentsElement.EnumerateArray().Take(MaxGarments))
                {
                    garments.Add(ParseGarment(item));
                }
            }

            var tags = new List<string>();
            if (root.TryGetProperty("style_tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagsElement.EnumerateArray())
                {
                    var normalized = tag.ValueKind == JsonValueKind.String ? StyleTags.Normalize(tag.GetString()) : null;
                    if (normalized is not null && !tags.Contains(normalized) && tags.Count < MaxStyleTags)
                    {
                        tags.Add(normalized);
                    }
                }
            }

            return new ImageAnalysis(true, garments, tags);
        }
        catch (JsonException ex)
        {
            throw new AnalysisFailedException("Model geçerli JSON döndürmedi.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new AnalysisFailedException("Model yanıtında beklenmeyen alan tipi var.", ex);
        }
    }

    private static Garment ParseGarment(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new AnalysisFailedException("Model yanıtındaki bir parça nesne değil.");
        }

        var evidence = ParseEnum(GetString(item, "brand_evidence"), BrandEvidence.None);
        var brand = CleanBrand(GetString(item, "brand"));
        if (brand is null || evidence == BrandEvidence.None)
        {
            brand = null;
            evidence = BrandEvidence.None;
        }

        var confidence = item.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
            ? Math.Clamp(c.GetDouble(), 0, 1)
            : 0.5;

        return new Garment(
            ParseEnum(GetString(item, "category"), GarmentCategory.Other),
            CleanText(GetString(item, "type"))?.ToLowerInvariant() ?? "item",
            StyleColors.Normalize(GetString(item, "color")),
            ParseEnum(GetString(item, "pattern"), GarmentPattern.Other),
            ParseEnum(GetString(item, "fit"), GarmentFit.Unknown),
            brand,
            evidence,
            confidence);
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new AnalysisFailedException($"Model yanıtında '{name}' metin değil.");
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(value)
        && Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
        && !int.TryParse(value, out _)
            ? parsed
            : fallback;

    private static string? CleanBrand(string? brand)
    {
        var cleaned = CleanText(brand);
        return cleaned is null || PlaceholderBrands.Contains(cleaned) ? null : cleaned;
    }

    /// <summary>Removes control characters, collapses whitespace and limits the length.</summary>
    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaxTextLength));
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (char.IsWhiteSpace(ch) && !lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
            if (builder.Length >= MaxTextLength)
            {
                break;
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
