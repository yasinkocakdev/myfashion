using Microsoft.EntityFrameworkCore;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;

namespace MyFashion.Infrastructure.Data;

/// <summary>Stores runs and analyses in SQLite. A new DbContext per call keeps it safe for background work.</summary>
public sealed class EfAnalysisStore(IDbContextFactory<MyFashionDbContext> contextFactory, TimeProvider clock) : IAnalysisStore
{
    public async Task<int> StartRunAsync(string accountName, RunSource source, string model, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Username == accountName, cancellationToken).ConfigureAwait(false)
                ?? db.Accounts.Add(new AccountEntity { Username = accountName, CreatedAtUtc = now }).Entity;

            var run = new RunEntity { Account = account, Source = source, Status = RunStatus.Running, Model = model, StartedAtUtc = now };
            db.Runs.Add(run);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return run.Id;
        }
    }

    public async Task<ImageAnalysis?> FindCachedAsync(string sha256, string model, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var item = await db.MediaItems
                .AsNoTracking()
                .Include(m => m.Garments)
                .Where(m => m.Sha256 == sha256
                    && m.Run!.Model == model
                    && (m.Status == MediaStatus.Analyzed || m.Status == MediaStatus.NoOutfit))
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return item is null ? null : Mapping.ToAnalysis(item);
        }
    }

    public async Task SaveMediaAsync(int runId, MediaRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var item = new MediaItemEntity
            {
                RunId = runId,
                Kind = record.Kind,
                SourcePath = record.SourcePath,
                PreparedPath = record.PreparedPath,
                Sha256 = record.Sha256,
                DHash = unchecked((long)record.DHash),
                Caption = Truncate(record.Caption, 2200),
                TakenAtUtc = record.TakenAt?.UtcDateTime,
                Status = record.Status,
                Error = Truncate(record.Error, 1000),
                StyleTags = string.Join(',', record.Analysis?.StyleTags ?? []),
            };
            foreach (var g in record.Analysis?.Garments ?? [])
            {
                item.Garments.Add(new GarmentEntity
                {
                    Category = g.Category,
                    Type = g.Type,
                    Color = g.Color,
                    Pattern = g.Pattern,
                    Fit = g.Fit,
                    Brand = g.Brand,
                    BrandEvidence = g.BrandEvidence,
                    Confidence = g.Confidence,
                });
            }

            db.MediaItems.Add(item);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FinishRunAsync(int runId, RunStatus status, RunCounts counts, string? errorMessage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var run = await db.Runs.SingleAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);
            run.Status = status;
            run.FinishedAtUtc = clock.GetUtcNow().UtcDateTime;
            run.Error = Truncate(errorMessage, 1000);
            (run.MediaFiles, run.Images, run.Duplicates, run.Analyzed, run.NoOutfit, run.Failed, run.FromCache) =
                (counts.MediaFiles, counts.Images, counts.Duplicates, counts.Analyzed, counts.NoOutfit, counts.Failed, counts.FromCache);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Cuts to <paramref name="max"/> chars without splitting an emoji (surrogate pair) in half.</summary>
    internal static string? Truncate(string? text, int max)
    {
        if (text is null || text.Length <= max)
        {
            return text;
        }

        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end];
    }
}

internal static class Mapping
{
    public static ImageAnalysis ToAnalysis(MediaItemEntity item)
    {
        var garments = item.Garments
            .OrderBy(g => g.Id)
            .Select(g => new Garment(g.Category, g.Type, g.Color, g.Pattern, g.Fit, g.Brand, g.BrandEvidence, g.Confidence))
            .ToList();
        var tags = item.StyleTags.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return item.Status == MediaStatus.NoOutfit ? ImageAnalysis.Empty : new ImageAnalysis(true, garments, tags);
    }
}
