using Microsoft.EntityFrameworkCore;
using MyFashion.Core.Application;
using MyFashion.Core.Recommendations;

namespace MyFashion.Infrastructure.Data;

/// <summary>The user's own preferences: a single row.</summary>
public sealed class UserPreferencesEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string? Gender { get; set; }

    public string? Sizes { get; set; }

    public string? Budget { get; set; }

    /// <summary>Comma-separated store ids from <see cref="StoreCatalog"/>.</summary>
    public string Stores { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

public sealed class EfPreferencesStore(IDbContextFactory<MyFashionDbContext> contextFactory) : IPreferencesStore
{
    private const int MaxField = 100;
    private const int MaxNotes = 500;

    public async Task<UserPreferences> GetAsync(CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var row = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            return row is null
                ? UserPreferences.Default
                : new UserPreferences(row.Gender, row.Sizes, row.Budget, ParseStores(row.Stores), row.Notes);
        }
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var row = await db.UserPreferences.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                row = new UserPreferencesEntity();
                db.UserPreferences.Add(row);
            }

            row.Gender = Limit(preferences.Gender, MaxField);
            row.Sizes = Limit(preferences.Sizes, MaxField);
            row.Budget = Limit(preferences.Budget, MaxField);
            row.Notes = Limit(preferences.Notes, MaxNotes);
            row.Stores = string.Join(',', preferences.Stores.Where(StoreCatalog.IsKnown).Distinct(StringComparer.Ordinal));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<string> ParseStores(string stores) =>
        [.. stores.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(StoreCatalog.IsKnown)];

    private static string? Limit(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
