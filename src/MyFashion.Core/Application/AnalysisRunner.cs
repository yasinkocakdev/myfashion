using System.Security.Cryptography;
using MyFashion.Core.Analysis;
using MyFashion.Core.IO;
using MyFashion.Core.Media;

namespace MyFashion.Core.Application;

/// <summary>Settings for <see cref="AnalysisRunner"/>.</summary>
/// <param name="DataRoot">Where prepared images are written (outside the repository).</param>
/// <param name="DuplicateDistance">dHash distance at or below which two images count as the same.</param>
/// <param name="MaxConsecutiveFailures">Stop the run when this many images fail in a row (for example Ollama is down).</param>
public sealed record AnalysisRunnerOptions(string DataRoot, int DuplicateDistance = 6, int MaxConsecutiveFailures = 5);

/// <summary>
/// Runs one analysis: media → prepared images → near-duplicate filter → cache or model → store.
/// A broken file never stops the run; it is recorded as failed and skipped. Cancelling keeps
/// everything analyzed so far, and a later run reuses it from the cache.
/// </summary>
public sealed class AnalysisRunner(
    IMediaToolkit toolkit,
    IVisionAnalyzer analyzer,
    IAnalysisStore store,
    AnalysisRunnerOptions options)
{
    public async Task<int> RunAsync(
        string accountName,
        IMediaSource source,
        RunSource sourceKind,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(source);

        var runId = await store.StartRunAsync(accountName, sourceKind, analyzer.ModelName, cancellationToken).ConfigureAwait(false);
        var counts = new MutableCounts();
        var filter = new NearDuplicateFilter(options.DuplicateDistance);
        var runFolder = PathGuard.ResolveWithin(options.DataRoot, Path.Combine("media", SafeName(accountName), $"run-{runId}"));
        var consecutiveFailures = 0;

        try
        {
            await foreach (var media in source.GetMediaAsync(cancellationToken).ConfigureAwait(false))
            {
                counts.MediaFiles++;
                Report(progress, runId, "Hazırlanıyor", counts, media.FullPath);

                IReadOnlyList<string> images;
                try
                {
                    images = await PrepareAsync(media, runFolder, counts.MediaFiles, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    counts.Failed++;
                    await SaveAsync(runId, Failed(media, null, null, 0, ex.Message), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var image in images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outcome = await ProcessImageAsync(runId, media, image, filter, counts, progress, cancellationToken).ConfigureAwait(false);
                    consecutiveFailures = outcome == MediaStatus.Failed ? consecutiveFailures + 1 : 0;
                    if (consecutiveFailures >= options.MaxConsecutiveFailures)
                    {
                        throw new AnalysisFailedException($"Üst üste {consecutiveFailures} görsel analiz edilemedi; analiz durduruldu. Ollama çalışıyor mu?");
                    }
                }
            }

            await store.FinishRunAsync(runId, RunStatus.Completed, counts.Snapshot(), null, CancellationToken.None).ConfigureAwait(false);
            Report(progress, runId, "Tamamlandı", counts, null);
            return runId;
        }
        catch (OperationCanceledException)
        {
            await store.FinishRunAsync(runId, RunStatus.Cancelled, counts.Snapshot(), "İptal edildi.", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await store.FinishRunAsync(runId, RunStatus.Failed, counts.Snapshot(), ex.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<MediaStatus?> ProcessImageAsync(
        int runId,
        MediaFile media,
        string image,
        NearDuplicateFilter filter,
        MutableCounts counts,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        counts.Images++;
        ulong hash;
        try
        {
            hash = await toolkit.ComputeDHashAsync(image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            counts.Failed++;
            await SaveAsync(runId, Failed(media, Relative(image), null, 0, ex.Message), cancellationToken).ConfigureAwait(false);
            return MediaStatus.Failed;
        }

        if (!filter.TryAdd(hash))
        {
            counts.Duplicates++;
            return null;
        }

        Report(progress, runId, "Analiz ediliyor", counts, media.FullPath);
        var sha = await Sha256Async(image, cancellationToken).ConfigureAwait(false);
        ImageAnalysis analysis;
        var cached = await store.FindCachedAsync(sha, analyzer.ModelName, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            counts.FromCache++;
            analysis = cached;
        }
        else
        {
            try
            {
                analysis = await analyzer.AnalyzeAsync(image, media.Caption, cancellationToken).ConfigureAwait(false);
            }
            catch (AnalysisFailedException ex)
            {
                counts.Failed++;
                await SaveAsync(runId, Failed(media, Relative(image), sha, hash, ex.Message), cancellationToken).ConfigureAwait(false);
                return MediaStatus.Failed;
            }
        }

        var status = analysis.HasOutfit && analysis.Garments.Count > 0 ? MediaStatus.Analyzed : MediaStatus.NoOutfit;
        if (status == MediaStatus.Analyzed)
        {
            counts.Analyzed++;
        }
        else
        {
            counts.NoOutfit++;
        }

        var record = new MediaRecord(media.Kind, media.FullPath, Relative(image), sha, hash, media.Caption, media.TakenAt, status, analysis, null);
        await SaveAsync(runId, record, cancellationToken).ConfigureAwait(false);
        return status;
    }

    private async Task<IReadOnlyList<string>> PrepareAsync(MediaFile media, string runFolder, int index, CancellationToken cancellationToken)
    {
        var name = $"{index:D4}";
        if (media.Type == MediaFileType.Video)
        {
            return await toolkit.ExtractFramesAsync(media.FullPath, Path.Combine(runFolder, name), cancellationToken).ConfigureAwait(false);
        }

        var output = Path.Combine(runFolder, name + ".jpg");
        await toolkit.NormalizeImageAsync(media.FullPath, output, cancellationToken).ConfigureAwait(false);
        return [output];
    }

    private Task SaveAsync(int runId, MediaRecord record, CancellationToken cancellationToken) =>
        store.SaveMediaAsync(runId, record, cancellationToken);

    private static MediaRecord Failed(MediaFile media, string? prepared, string? sha, ulong hash, string error) =>
        new(media.Kind, media.FullPath, prepared, sha, hash, media.Caption, media.TakenAt, MediaStatus.Failed, null, error);

    private string Relative(string path) => Path.GetRelativePath(options.DataRoot, path);

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }
    }

    /// <summary>Folder-safe version of an account name (Instagram names are already safe; folder names may not be).</summary>
    internal static string SafeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
        var safe = new string(chars).Trim('.');
        return safe.Length == 0 ? "hesap" : safe;
    }

    private static void Report(IProgress<RunProgress>? progress, int runId, string stage, MutableCounts counts, string? current) =>
        progress?.Report(new RunProgress(runId, stage, counts.Snapshot(), current is null ? null : Path.GetFileName(current)));

    private sealed class MutableCounts
    {
        public int MediaFiles { get; set; }

        public int Images { get; set; }

        public int Duplicates { get; set; }

        public int Analyzed { get; set; }

        public int NoOutfit { get; set; }

        public int Failed { get; set; }

        public int FromCache { get; set; }

        public RunCounts Snapshot() => new(MediaFiles, Images, Duplicates, Analyzed, NoOutfit, Failed, FromCache);
    }
}
