using System.Collections.Concurrent;
using System.Diagnostics;
using MyFashion.Core.Analysis;
using MyFashion.Core.Media;

namespace MyFashion.Bench;

/// <summary>Collects durations per operation name. Thread-safe.</summary>
internal sealed class Timings
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _samples = new(StringComparer.Ordinal);

    public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _samples.GetOrAdd(name, _ => []).Add(watch.Elapsed.TotalMilliseconds);
        }
    }

    public IReadOnlyList<double> Get(string name) =>
        _samples.TryGetValue(name, out var bag) ? [.. bag.Order()] : [];

    public double Total(string name) => Get(name).Sum();
}

/// <summary>Decorator: measures every toolkit call without changing it.</summary>
internal sealed class TimedToolkit(IMediaToolkit inner, Timings timings) : IMediaToolkit
{
    public Task<IReadOnlyList<PreparedImage>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken) =>
        timings.MeasureAsync("ffmpeg.frames", () => inner.ExtractFramesAsync(videoPath, outputDirectory, cancellationToken));

    public Task<PreparedImage> PrepareImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken) =>
        timings.MeasureAsync("ffmpeg.image", () => inner.PrepareImageAsync(imagePath, outputPath, cancellationToken));
}

/// <summary>Decorator: measures every model call.</summary>
internal sealed class TimedAnalyzer(IVisionAnalyzer inner, Timings timings) : IVisionAnalyzer
{
    public string ModelName => inner.ModelName;

    public Task<ImageAnalysis> AnalyzeAsync(string imagePath, string? caption, CancellationToken cancellationToken) =>
        timings.MeasureAsync("model", () => inner.AnalyzeAsync(imagePath, caption, cancellationToken));
}
