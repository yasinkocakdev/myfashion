using System.Collections.Concurrent;
using MyFashion.Core.Application;
using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;

namespace MyFashion.Web.Services;

/// <summary>Builds profiles and keeps generated recommendations in memory until the data or preferences change.</summary>
public sealed class RecommendationService(IAnalysisReader reader, IPreferencesStore preferences, IServiceScopeFactory scopes)
{
    private readonly ConcurrentDictionary<string, (string Key, Recommendations Value)> _cache = new(StringComparer.Ordinal);

    public async Task<StyleProfile> GetProfileAsync(string username, CancellationToken cancellationToken)
    {
        var images = await reader.GetImagesAsync(username, cancellationToken).ConfigureAwait(false);
        return StyleProfileBuilder.Build(username, images);
    }

    public Recommendations? GetCached(StyleProfile profile, UserPreferences prefs) =>
        _cache.TryGetValue(profile.Username, out var entry) && entry.Key == CacheKey(profile, prefs) ? entry.Value : null;

    public async Task<Recommendations> GenerateAsync(StyleProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var prefs = await preferences.GetAsync(cancellationToken).ConfigureAwait(false);
        using var scope = scopes.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IRecommendationGenerator>();
        var result = await generator.GenerateAsync(profile, prefs, cancellationToken).ConfigureAwait(false);
        _cache[profile.Username] = (CacheKey(profile, prefs), result);
        return result;
    }

    private static string CacheKey(StyleProfile profile, UserPreferences prefs) =>
        $"{profile.ImagesWithOutfit}|{profile.GarmentCount}|{prefs.Gender}|{prefs.Sizes}|{prefs.Budget}|{string.Join(',', prefs.Stores)}|{prefs.Notes}";
}
