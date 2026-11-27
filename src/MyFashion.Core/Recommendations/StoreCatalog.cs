using System.Text;

namespace MyFashion.Core.Recommendations;

/// <summary>
/// The only stores links can point to. URLs are built here from a fixed template and an
/// encoded query, so a model can never inject its own (possibly malicious) link.
/// </summary>
public static class StoreCatalog
{
    public const string GoogleShopping = "google";
    public const string Zara = "zara";
    public const string Trendyol = "trendyol";
    public const string HM = "hm";

    private const int MaxQueryLength = 80;

    private static readonly Dictionary<string, (string Name, string Template)> Stores = new(StringComparer.Ordinal)
    {
        [GoogleShopping] = ("Google Alışveriş", "https://www.google.com/search?udm=28&q={0}"),
        [Zara] = ("Zara", "https://www.zara.com/tr/tr/search?searchTerm={0}"),
        [Trendyol] = ("Trendyol", "https://www.trendyol.com/sr?q={0}"),
        [HM] = ("H&M", "https://www2.hm.com/tr_tr/search-results.html?q={0}"),
    };

    /// <summary>Store ids and display names, in a stable order.</summary>
    public static IReadOnlyList<(string Id, string Name)> All { get; } =
        [.. Stores.Select(kv => (kv.Key, kv.Value.Name))];

    public static bool IsKnown(string storeId) => Stores.ContainsKey(storeId);

    /// <summary>Keeps letters, digits, spaces and hyphens; collapses whitespace; limits length.</summary>
    public static string SanitizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }

            if (builder.Length >= MaxQueryLength)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Builds search links for the given (known) stores. Unknown store ids are ignored.</summary>
    public static IReadOnlyList<StoreLink> BuildLinks(string query, IEnumerable<string> storeIds)
    {
        var clean = SanitizeQuery(query);
        if (clean.Length == 0)
        {
            return [];
        }

        var encoded = Uri.EscapeDataString(clean);
        return [.. storeIds
            .Distinct(StringComparer.Ordinal)
            .Where(Stores.ContainsKey)
            .Select(id => new StoreLink(Stores[id].Name, new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, Stores[id].Template, encoded))))];
    }
}
