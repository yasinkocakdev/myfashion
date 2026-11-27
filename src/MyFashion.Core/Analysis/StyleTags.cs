namespace MyFashion.Core.Analysis;

/// <summary>The fixed vocabulary of style tags the model may use.</summary>
public static class StyleTags
{
    public static IReadOnlyList<string> All { get; } =
    [
        "minimal", "streetwear", "old money", "casual", "smart casual", "sporty", "athleisure", "techwear",
        "vintage", "preppy", "y2k", "workwear", "formal", "bohemian", "edgy", "classic", "romantic", "grunge",
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    /// <summary>Returns the canonical tag, or null if it is not in the vocabulary.</summary>
    public static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var text = tag.Trim().ToLowerInvariant().Replace('-', ' ').Replace('_', ' ');
        return Known.Contains(text) ? text : null;
    }
}
