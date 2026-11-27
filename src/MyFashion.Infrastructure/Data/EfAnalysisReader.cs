using Microsoft.EntityFrameworkCore;
using MyFashion.Core.Application;
using MyFashion.Core.Profile;

namespace MyFashion.Infrastructure.Data;

/// <summary>Read-only queries. <c>AsNoTracking</c> everywhere: the UI never edits these entities.</summary>
public sealed class EfAnalysisReader(IDbContextFactory<MyFashionDbContext> contextFactory) : IAnalysisReader
{
    public async Task<IReadOnlyList<AccountOverview>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = await db.Accounts
                .AsNoTracking()
                .Select(a => new
                {
                    a.Username,
                    Runs = a.Runs.Count,
                    LastRun = a.Runs.Max(r => (DateTime?)r.StartedAtUtc),
                    Images = a.Runs.SelectMany(r => r.MediaItems).Where(m => m.Status == MediaStatus.Analyzed).Select(m => m.Sha256).Distinct().Count(),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. rows
                .OrderByDescending(r => r.LastRun)
                .Select(r => new AccountOverview(r.Username, r.Runs, ToOffset(r.LastRun), r.Images))];
        }
    }

    public async Task<IReadOnlyList<AnalyzedImage>> GetImagesAsync(string username, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var items = await db.MediaItems
                .AsNoTracking()
                .Include(m => m.Garments)
                .Where(m => m.Run!.Account!.Username == username && m.Status == MediaStatus.Analyzed)
                .OrderByDescending(m => m.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // The same image analyzed in several runs counts once (the latest analysis wins).
            return [.. items
                .DistinctBy(m => m.Sha256 ?? m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Select(m => new AnalyzedImage(
                    m.Id,
                    m.Kind,
                    m.PreparedPath,
                    ToOffset(m.TakenAtUtc),
                    Mapping.ToAnalysis(m).Garments,
                    Mapping.ToAnalysis(m).StyleTags))];
        }
    }

    public async Task<IReadOnlyList<RunInfo>> GetRunsAsync(string username, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var runs = await db.Runs
                .AsNoTracking()
                .Where(r => r.Account!.Username == username)
                .OrderByDescending(r => r.Id)
                .Select(r => new
                {
                    r.Id, r.Source, r.Status, r.Model, r.StartedAtUtc, r.FinishedAtUtc, r.Error,
                    r.MediaFiles, r.Images, r.Duplicates, r.Analyzed, r.NoOutfit, r.Failed, r.FromCache,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. runs.Select(r => new RunInfo(
                r.Id,
                username,
                r.Source,
                r.Status,
                r.Model,
                ToOffset(r.StartedAtUtc)!.Value,
                ToOffset(r.FinishedAtUtc),
                r.Error,
                new RunCounts(r.MediaFiles, r.Images, r.Duplicates, r.Analyzed, r.NoOutfit, r.Failed, r.FromCache)))];
        }
    }

    private static DateTimeOffset? ToOffset(DateTime? utc) =>
        utc is null ? null : new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc));
}
