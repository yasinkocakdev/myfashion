using System.Globalization;
using System.Runtime.CompilerServices;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;

namespace MyFashion.Tests.Core;

/// <summary>
/// Tests the run orchestration with fakes. Fake media files contain a tiny script:
/// an image is "hash=N;id=X"; a video is several such frames separated by "|"; "corrupt" fails to prepare.
/// </summary>
public sealed class AnalysisRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("myfashion-runner-").FullName;
    private readonly FakeAnalyzer _analyzer = new();
    private readonly InMemoryStore _store = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Analyzes_images_and_video_frames_and_drops_near_duplicates()
    {
        var media = new[]
        {
            Image("a.jpg", "hash=0;id=coat"),
            Image("b.jpg", "hash=1;id=coat-again"),           // 1 bit from a.jpg: duplicate
            Video("klip.mp4", "hash=4095;id=jeans|hash=4094;id=jeans2|hash=16777215;id=nooutfit"),
        };

        var runId = await CreateRunner().RunAsync("hesap", new FakeSource(media), RunSource.Folder, null, TestContext.Current.CancellationToken);

        var run = _store.Runs[runId];
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(new RunCounts(MediaFiles: 3, Images: 5, Duplicates: 2, Analyzed: 2, NoOutfit: 1, Failed: 0, FromCache: 0), run.Counts);
        Assert.Equal(3, _store.Media[runId].Count);
        Assert.Equal(3, _analyzer.Calls);
        Assert.All(_store.Media[runId], m => Assert.False(Path.IsPathRooted(m.PreparedPath)));
    }

    [Fact]
    public async Task Broken_files_and_failed_analyses_are_recorded_and_the_run_continues()
    {
        var media = new[]
        {
            Image("bozuk.jpg", "corrupt"),
            Image("a.jpg", "hash=0;id=fail"),
            Image("b.jpg", "hash=4294967295;id=coat"),
        };

        var runId = await CreateRunner().RunAsync("hesap", new FakeSource(media), RunSource.Folder, null, TestContext.Current.CancellationToken);

        var run = _store.Runs[runId];
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(2, run.Counts!.Failed);
        Assert.Equal(1, run.Counts!.Analyzed);
        Assert.Equal(2, _store.Media[runId].Count(m => m.Status == MediaStatus.Failed && m.Error is not null));
    }

    [Fact]
    public async Task Stops_after_too_many_consecutive_failures()
    {
        // Hashes at least 16 bits apart from each other, so none is filtered as a duplicate.
        ulong[] hashes = [0, 0xFFFF, 0xFFFF_0000, 0xFFFF_0000_0000, 0xFFFF_0000_0000_0000, 0x00FF_00FF_00FF_00FF];
        var media = hashes.Select((h, i) => Image($"{i}.jpg", $"hash={h};id=fail")).ToArray();
        var runner = CreateRunner(new AnalysisRunnerOptions(_root, MaxConsecutiveFailures: 3));

        var ex = await Assert.ThrowsAsync<AnalysisFailedException>(() =>
            runner.RunAsync("hesap", new FakeSource(media), RunSource.Folder, null, TestContext.Current.CancellationToken));

        Assert.Contains("Ollama", ex.Message, StringComparison.Ordinal);
        var run = Assert.Single(_store.Runs.Values);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(3, run.Counts!.Failed);
    }

    [Fact]
    public async Task Cancelling_keeps_finished_work_and_the_next_run_reuses_it_from_cache()
    {
        var media = new[]
        {
            Image("a.jpg", "hash=0;id=coat"),
            Image("b.jpg", "hash=4294967295;id=jeans"),
            Image("c.jpg", "hash=18446744069414584320;id=shoes"),
        };
        using var cts = new CancellationTokenSource();
        _analyzer.AfterCall = calls =>
        {
            if (calls == 2)
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateRunner().RunAsync("hesap", new FakeSource(media), RunSource.Folder, null, cts.Token));

        var cancelled = Assert.Single(_store.Runs.Values);
        Assert.Equal(RunStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, _store.Media[cancelled.Id].Count);

        _analyzer.AfterCall = null;
        var runId = await CreateRunner().RunAsync("hesap", new FakeSource(media), RunSource.Folder, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunStatus.Completed, _store.Runs[runId].Status);
        Assert.Equal(2, _store.Runs[runId].Counts!.FromCache);
        Assert.Equal(3, _analyzer.Calls);
    }

    [Fact]
    public async Task Reports_progress()
    {
        var reports = new List<RunProgress>();
        var progress = new SynchronousProgress<RunProgress>(reports.Add);

        await CreateRunner().RunAsync("hesap", new FakeSource([Image("a.jpg", "hash=0;id=coat")]), RunSource.Folder, progress, TestContext.Current.CancellationToken);

        Assert.Contains(reports, r => r.Stage == "Analiz ediliyor" && r.CurrentItem == "a.jpg");
        Assert.Equal("Tamamlandı", reports[^1].Stage);
        Assert.Equal(1, reports[^1].Counts.Analyzed);
    }

    [Theory]
    [InlineData("some.user", "some.user")]
    [InlineData("Öne çıkanlar/../x", "Öne_çıkanlar_.._x")]
    [InlineData("..", "hesap")]
    [InlineData("a b", "a_b")]
    public void SafeName_produces_folder_safe_names(string input, string expected)
    {
        Assert.Equal(expected, AnalysisRunner.SafeName(input));
    }

    private AnalysisRunner CreateRunner(AnalysisRunnerOptions? options = null) =>
        new(new FakeToolkit(), _analyzer, _store, options ?? new AnalysisRunnerOptions(_root));

    private MediaFile Image(string name, string script) => Write(name, script, MediaFileType.Image);

    private MediaFile Video(string name, string script) => Write(name, script, MediaFileType.Video);

    private MediaFile Write(string name, string script, MediaFileType type)
    {
        var path = Path.Combine(_root, "input", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, script);
        return new MediaFile(path, type, MediaKind.Manual);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class FakeSource(IReadOnlyList<MediaFile> files) : IMediaSource
    {
        public async IAsyncEnumerable<MediaFile> GetMediaAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return file;
            }
        }
    }

    private sealed class FakeToolkit : IMediaToolkit
    {
        public Task<IReadOnlyList<PreparedImage>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            var frames = File.ReadAllText(videoPath).Split('|');
            var prepared = frames.Select((frame, i) =>
            {
                var path = Path.Combine(outputDirectory, $"frame_{i + 1:D4}.jpg");
                File.WriteAllText(path, frame);
                return new PreparedImage(path, Hash(frame));
            }).ToList();
            return Task.FromResult<IReadOnlyList<PreparedImage>>(prepared);
        }

        public Task<PreparedImage> PrepareImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken)
        {
            var content = File.ReadAllText(imagePath);
            if (content == "corrupt")
            {
                throw new InvalidDataException("ffmpeg hata verdi: bozuk dosya");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, content);
            return Task.FromResult(new PreparedImage(outputPath, Hash(content)));
        }

        private static ulong Hash(string script) => ulong.Parse(Field(script, "hash"), CultureInfo.InvariantCulture);
    }

    private sealed class FakeAnalyzer : IVisionAnalyzer
    {
        public int Calls { get; private set; }

        public Action<int>? AfterCall { get; set; }

        public string ModelName => "fake-model";

        public Task<ImageAnalysis> AnalyzeAsync(string imagePath, string? caption, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var id = Field(File.ReadAllText(imagePath), "id");
            AfterCall?.Invoke(Calls);
            if (id == "fail")
            {
                throw new AnalysisFailedException("Model geçerli JSON döndürmedi.");
            }

            return Task.FromResult(id == "nooutfit"
                ? ImageAnalysis.Empty
                : new ImageAnalysis(true, [new Garment(GarmentCategory.Top, id, "black", GarmentPattern.Solid, GarmentFit.Regular, null, BrandEvidence.None, 0.9)], ["minimal"]));
        }
    }

    private static string Field(string script, string name) =>
        script.Split(';').Select(part => part.Split('=')).First(kv => kv[0] == name)[1];

    private sealed class InMemoryStore : IAnalysisStore
    {
        public Dictionary<int, RunRow> Runs { get; } = [];

        public Dictionary<int, List<MediaRecord>> Media { get; } = [];

        public Task<int> StartRunAsync(string accountName, RunSource source, string model, CancellationToken cancellationToken)
        {
            var id = Runs.Count + 1;
            Runs[id] = new RunRow(id, model);
            Media[id] = [];
            return Task.FromResult(id);
        }

        public Task<ImageAnalysis?> FindCachedAsync(string sha256, string model, CancellationToken cancellationToken) =>
            Task.FromResult(Media
                .Where(kv => Runs[kv.Key].Model == model)
                .SelectMany(kv => kv.Value)
                .FirstOrDefault(m => m.Sha256 == sha256 && m.Analysis is not null)?.Analysis);

        public Task SaveMediaAsync(int runId, MediaRecord record, CancellationToken cancellationToken)
        {
            Media[runId].Add(record);
            return Task.CompletedTask;
        }

        public Task FinishRunAsync(int runId, RunStatus status, RunCounts counts, string? errorMessage, CancellationToken cancellationToken)
        {
            Runs[runId] = Runs[runId] with { Status = status, Counts = counts, Error = errorMessage };
            return Task.CompletedTask;
        }
    }

    private sealed record RunRow(int Id, string Model, RunStatus Status = RunStatus.Running, RunCounts? Counts = null, string? Error = null);
}
