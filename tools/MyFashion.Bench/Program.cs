using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MyFashion.Bench;
using MyFashion.Core.Application;
using MyFashion.Infrastructure.Data;
using MyFashion.Infrastructure.Media;
using MyFashion.Infrastructure.Ollama;
using MyFashion.Infrastructure.Processes;

// Usage:
//   dotnet run --project tools/MyFashion.Bench -c Release -- run <folder> [--model qwen3-vl:8b] [--side 768] [--cold]
//   dotnet run --project tools/MyFashion.Bench -c Release -- parallel <folder> [--model qwen3-vl:8b] [--count 12]
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: run|parallel <folder> [--model m] [--side px] [--cold] [--count n]");
    return 2;
}

var mode = args[0];
var folder = Path.GetFullPath(args[1]);
var model = Option("--model") ?? "qwen3-vl:8b";
var side = int.Parse(Option("--side") ?? "768", CultureInfo.InvariantCulture);
var ollama = new OllamaOptions { VisionModel = model, MaxAttempts = 1 };
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

if (args.Contains("--cold"))
{
    // keep_alive 0 unloads the model, so the next request pays the load time.
    using var unload = await http.PostAsJsonAsync(new Uri(ollama.BaseUrl, "/api/generate"), new { model, keep_alive = 0 });
    await Task.Delay(1500);
}

return mode switch
{
    "run" => await RunAsync(),
    "parallel" => await ParallelAsync(),
    _ => 2,
};

async Task<int> RunAsync()
{
    var dataRoot = Directory.CreateTempSubdirectory("myfashion-bench-").FullName;
    var dbOptions = new DbContextOptionsBuilder<MyFashionDbContext>().UseSqlite($"Data Source={Path.Combine(dataRoot, "bench.db")}").Options;
    await using (var db = new MyFashionDbContext(dbOptions))
    {
        await db.Database.MigrateAsync();
    }

    var timings = new Timings();
    var toolkit = new TimedToolkit(new FfmpegMediaToolkit(new ProcessRunner(), new MediaToolkitOptions { MaxImageSide = side }), timings);
    var analyzer = new TimedAnalyzer(new OllamaVisionAnalyzer(new OllamaChatClient(http, ollama), ollama), timings);
    var store = new EfAnalysisStore(new Factory(dbOptions), TimeProvider.System);
    var runner = new AnalysisRunner(toolkit, analyzer, store, new AnalysisRunnerOptions(dataRoot));

    Console.WriteLine($"model={model} side={side} folder={folder}");
    foreach (var pass in new[] { "first", "cached" })
    {
        var before = timings.Get("model").Count;
        var watch = Stopwatch.StartNew();
        RunProgress? last = null;
        await runner.RunAsync("bench", new FolderMediaSource(folder), RunSource.Folder, new Sync<RunProgress>(p => last = p), CancellationToken.None);
        watch.Stop();

        var counts = last!.Counts;
        var modelCalls = timings.Get("model").Count - before;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {pass} | {watch.Elapsed.TotalSeconds:0.0} s | {counts.MediaFiles} | {counts.Images} | {counts.Duplicates} | {modelCalls} | {counts.FromCache} | {counts.Failed} | {counts.Images / watch.Elapsed.TotalMinutes:0.0} |"));
    }

    var model1 = timings.Get("model");
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"model: calls={model1.Count} avg={Avg(model1):0} ms p50={P(model1, 0.5):0} ms p95={P(model1, 0.95):0} ms total={model1.Sum() / 1000:0.0} s"));
    foreach (var op in new[] { "ffmpeg.image", "ffmpeg.frames" })
    {
        var s = timings.Get(op);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{op}: calls={s.Count} avg={Avg(s):0} ms total={s.Sum() / 1000:0.00} s"));
    }

    Directory.Delete(dataRoot, recursive: true);
    return 0;
}

async Task<int> ParallelAsync()
{
    // Raw model throughput with 1 vs 2 concurrent requests on the same prepared images.
    var count = int.Parse(Option("--count") ?? "12", CultureInfo.InvariantCulture);
    var prepared = Directory.CreateTempSubdirectory("myfashion-par-").FullName;
    var toolkit = new FfmpegMediaToolkit(new ProcessRunner(), new MediaToolkitOptions { MaxImageSide = side });
    var images = new List<string>();
    foreach (var file in Directory.EnumerateFiles(folder, "*.jpg", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Take(count))
    {
        var output = Path.Combine(prepared, Path.GetFileName(file));
        images.Add((await toolkit.PrepareImageAsync(file, output, CancellationToken.None)).Path);
    }

    var analyzer = new OllamaVisionAnalyzer(new OllamaChatClient(http, ollama), ollama);
    await analyzer.AnalyzeAsync(images[0], null, CancellationToken.None); // warm up
    foreach (var parallelism in new[] { 1, 2, 3 })
    {
        var watch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(images, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, async (image, ct) =>
            await analyzer.AnalyzeAsync(image, null, ct));
        watch.Stop();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {parallelism} | {images.Count} | {watch.Elapsed.TotalSeconds:0.0} s | {images.Count / watch.Elapsed.TotalMinutes:0.0} |"));
    }

    Directory.Delete(prepared, recursive: true);
    return 0;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static double Avg(IReadOnlyList<double> s) => s.Count == 0 ? 0 : s.Average();

static double P(IReadOnlyList<double> sorted, double q) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1)];

internal sealed class Factory(DbContextOptions<MyFashionDbContext> options) : IDbContextFactory<MyFashionDbContext>
{
    public MyFashionDbContext CreateDbContext() => new(options);
}

internal sealed class Sync<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
