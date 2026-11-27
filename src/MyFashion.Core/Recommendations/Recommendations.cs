using MyFashion.Core.Profile;

namespace MyFashion.Core.Recommendations;

/// <summary>What the user tells us about themselves, so suggestions fit them.</summary>
public sealed record UserPreferences(
    string? Gender,
    string? Sizes,
    string? Budget,
    IReadOnlyList<string> Stores,
    string? Notes)
{
    public static UserPreferences Default { get; } = new(null, null, null, [StoreCatalog.GoogleShopping, StoreCatalog.Zara, StoreCatalog.Trendyol], null);
}

public sealed record StoreLink(string Store, Uri Url);

/// <param name="Item">What to buy, for example "Siyah oversize kapüşonlu sweatshirt".</param>
/// <param name="Why">Why it fits the analyzed style.</param>
/// <param name="Priority">1 = buy first, 3 = nice to have.</param>
/// <param name="SearchQuery">Sanitized search words used to build the links.</param>
/// <param name="Links">Search links built by code from an allow-list of stores (never from model output).</param>
public sealed record ShoppingItem(string Item, string Why, int Priority, string SearchQuery, IReadOnlyList<StoreLink> Links);

public sealed record OutfitSuggestion(string Name, IReadOnlyList<string> Pieces, string Occasion, string Why);

public sealed record Recommendations(IReadOnlyList<ShoppingItem> ShoppingList, IReadOnlyList<OutfitSuggestion> Outfits)
{
    public static Recommendations Empty { get; } = new([], []);
}

/// <summary>Creates a shopping list and outfit ideas from a style profile.</summary>
public interface IRecommendationGenerator
{
    /// <exception cref="Analysis.AnalysisFailedException">The model could not produce usable suggestions.</exception>
    Task<Recommendations> GenerateAsync(StyleProfile profile, UserPreferences preferences, CancellationToken cancellationToken);
}
