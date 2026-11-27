using Microsoft.EntityFrameworkCore;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;
using MyFashion.Infrastructure.Data;

namespace MyFashion.Tests.Infrastructure;

public sealed class EfAnalysisStoreTests : IDisposable
{
    private static readonly ImageAnalysis Coat = new(
        true,
        [new Garment(GarmentCategory.Outerwear, "wool coat", "beige", GarmentPattern.Solid, GarmentFit.Oversized, "Zara", BrandEvidence.Text, 0.8)],
        ["old money", "minimal"]);

    private readonly SqliteTestDatabase _db = new();
    private readonly EfAnalysisStore _store;

    public EfAnalysisStoreTests() => _store = new EfAnalysisStore(_db, TimeProvider.System);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Run_lifecycle_is_persisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = await _store.StartRunAsync("some.user", RunSource.Folder, "qwen3-vl:8b", ct);

        await _store.SaveMediaAsync(runId, Record("a", Coat, MediaStatus.Analyzed), ct);
        await _store.FinishRunAsync(runId, RunStatus.Completed, new RunCounts(1, 1, 0, 1, 0, 0, 0), null, ct);

        await using var db = _db.CreateDbContext();
        var run = await db.Runs.Include(r => r.Account).Include(r => r.MediaItems).ThenInclude(m => m.Garments).SingleAsync(ct);
        Assert.Equal("some.user", run.Account!.Username);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.FinishedAtUtc);
        Assert.Equal(1, run.Analyzed);
        var garment = Assert.Single(Assert.Single(run.MediaItems).Garments);
        Assert.Equal("Zara", garment.Brand);
        Assert.Equal(GarmentCategory.Outerwear, garment.Category);
    }

    [Fact]
    public async Task Same_account_is_reused_across_runs()
    {
        var ct = TestContext.Current.CancellationToken;

        await _store.StartRunAsync("some.user", RunSource.Folder, "m", ct);
        await _store.StartRunAsync("some.user", RunSource.InstagramApi, "m", ct);

        await using var db = _db.CreateDbContext();
        Assert.Equal(1, await db.Accounts.CountAsync(ct));
        Assert.Equal(2, await db.Runs.CountAsync(ct));
    }

    [Fact]
    public async Task Cache_returns_analysis_for_same_image_and_model_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = await _store.StartRunAsync("some.user", RunSource.Folder, "model-a", ct);
        await _store.SaveMediaAsync(runId, Record("sha-1", Coat, MediaStatus.Analyzed), ct);
        await _store.SaveMediaAsync(runId, Record("sha-2", ImageAnalysis.Empty, MediaStatus.NoOutfit), ct);
        await _store.SaveMediaAsync(runId, Record("sha-3", null, MediaStatus.Failed), ct);

        var hit = await _store.FindCachedAsync("sha-1", "model-a", ct);
        Assert.NotNull(hit);
        Assert.Equal(Coat.Garments, hit.Garments);
        Assert.Equal(Coat.StyleTags, hit.StyleTags);

        Assert.False((await _store.FindCachedAsync("sha-2", "model-a", ct))!.HasOutfit);
        Assert.Null(await _store.FindCachedAsync("sha-3", "model-a", ct));
        Assert.Null(await _store.FindCachedAsync("sha-1", "model-b", ct));
        Assert.Null(await _store.FindCachedAsync("unknown", "model-a", ct));
    }

    [Fact]
    public async Task Turkish_text_long_captions_and_large_hashes_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = await _store.StartRunAsync("öne.çıkan", RunSource.Folder, "m", ct);
        var caption = "Yeni ceketim 🧥 @markaadı " + new string('ğ', 5000);

        await _store.SaveMediaAsync(runId, Record("x", Coat, MediaStatus.Analyzed) with { Caption = caption, DHash = ulong.MaxValue }, ct);

        await using var db = _db.CreateDbContext();
        var item = await db.MediaItems.SingleAsync(ct);
        Assert.Equal(2200, item.Caption!.Length);
        Assert.StartsWith("Yeni ceketim 🧥 @markaadı ", item.Caption, StringComparison.Ordinal);
        Assert.Equal(ulong.MaxValue, unchecked((ulong)item.DHash));
        Assert.Equal("öne.çıkan", (await db.Accounts.SingleAsync(ct)).Username);
    }

    [Fact]
    public void Truncate_never_splits_an_emoji()
    {
        var text = new string('a', 9) + "🧥🧥";

        var cut = EfAnalysisStore.Truncate(text, 10);

        Assert.Equal(new string('a', 9), cut);
        Assert.Equal("ab", EfAnalysisStore.Truncate("abc", 2));
        Assert.Null(EfAnalysisStore.Truncate(null, 2));
    }

    private static MediaRecord Record(string sha, ImageAnalysis? analysis, MediaStatus status) =>
        new(MediaKind.Post, "/tmp/in.jpg", "media/x.jpg", sha, 42, null, DateTimeOffset.UtcNow, status, analysis, status == MediaStatus.Failed ? "boom" : null);
}
