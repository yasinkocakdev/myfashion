using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;
using MyFashion.Core.Recommendations;
using MyFashion.Infrastructure.Data;

namespace MyFashion.Tests.Infrastructure;

public sealed class EfReadSideTests : IDisposable
{
    private static readonly ImageAnalysis Outfit = new(
        true,
        [new Garment(GarmentCategory.Top, "hoodie", "black", GarmentPattern.Solid, GarmentFit.Oversized, null, BrandEvidence.None, 0.9)],
        ["streetwear"]);

    private readonly SqliteTestDatabase _db = new();
    private readonly EfAnalysisStore _store;
    private readonly EfAnalysisReader _reader;

    public EfReadSideTests()
    {
        _store = new EfAnalysisStore(_db, TimeProvider.System);
        _reader = new EfAnalysisReader(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Accounts_show_runs_last_run_and_distinct_analyzed_images()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.SaveMediaAsync(first, Media("sha-1", MediaStatus.Analyzed), ct);
        await _store.SaveMediaAsync(first, Media("sha-2", MediaStatus.NoOutfit), ct);
        var second = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.SaveMediaAsync(second, Media("sha-1", MediaStatus.Analyzed), ct);
        await _store.SaveMediaAsync(second, Media("sha-3", MediaStatus.Analyzed), ct);
        await _store.StartRunAsync("b.user", RunSource.Folder, "m", ct);

        var accounts = await _reader.GetAccountsAsync(ct);

        var a = Assert.Single(accounts, x => x.Username == "a.user");
        Assert.Equal(2, a.Runs);
        Assert.Equal(2, a.AnalyzedImages);
        Assert.NotNull(a.LastRunAt);
        Assert.Equal(0, Assert.Single(accounts, x => x.Username == "b.user").AnalyzedImages);
    }

    [Fact]
    public async Task Images_are_deduplicated_across_runs_and_keep_garments()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.SaveMediaAsync(first, Media("sha-1", MediaStatus.Analyzed), ct);
        await _store.SaveMediaAsync(first, Media("sha-x", MediaStatus.Failed), ct);
        var second = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.SaveMediaAsync(second, Media("sha-1", MediaStatus.Analyzed), ct);

        var images = await _reader.GetImagesAsync("a.user", ct);

        var image = Assert.Single(images);
        Assert.Equal("hoodie", Assert.Single(image.Garments).Type);
        Assert.Equal(["streetwear"], image.StyleTags);
        Assert.Empty(await _reader.GetImagesAsync("nobody", ct));
    }

    [Fact]
    public async Task Runs_are_listed_newest_first_with_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.FinishRunAsync(first, RunStatus.Completed, new RunCounts(3, 4, 1, 2, 1, 0, 0), null, ct);
        var second = await _store.StartRunAsync("a.user", RunSource.Folder, "m", ct);
        await _store.FinishRunAsync(second, RunStatus.Failed, new RunCounts(1, 1, 0, 0, 0, 1, 0), "Ollama kapalı", ct);

        var runs = await _reader.GetRunsAsync("a.user", ct);

        Assert.Equal([second, first], runs.Select(r => r.Id));
        Assert.Equal("Ollama kapalı", runs[0].Error);
        Assert.Equal(2, runs[1].Counts.Analyzed);
    }

    [Fact]
    public async Task Preferences_default_then_round_trip_and_drop_unknown_stores()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new EfPreferencesStore(_db);

        Assert.Equal(UserPreferences.Default.Stores, (await store.GetAsync(ct)).Stores);

        await store.SaveAsync(new UserPreferences("erkek", "M / 32", "  orta  ", [StoreCatalog.Zara, "evil", StoreCatalog.Zara], new string('n', 900)), ct);
        var saved = await store.GetAsync(ct);

        Assert.Equal("erkek", saved.Gender);
        Assert.Equal("orta", saved.Budget);
        Assert.Equal([StoreCatalog.Zara], saved.Stores);
        Assert.Equal(500, saved.Notes!.Length);

        await store.SaveAsync(saved with { Gender = null }, ct);
        Assert.Null((await store.GetAsync(ct)).Gender);
    }

    private static MediaRecord Media(string sha, MediaStatus status) =>
        new(MediaKind.Post, "/in.jpg", "media/x.jpg", sha, 1, null, null, status,
            status == MediaStatus.Analyzed ? Outfit : status == MediaStatus.NoOutfit ? ImageAnalysis.Empty : null,
            status == MediaStatus.Failed ? "hata" : null);
}
