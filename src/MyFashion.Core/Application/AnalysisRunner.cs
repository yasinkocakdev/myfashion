using System.Security.Cryptography;
using System.Threading.Channels;
using MyFashion.Core.Analysis;
using MyFashion.Core.IO;
using MyFashion.Core.Media;

namespace MyFashion.Core.Application;

/// <summary>Settings for <see cref="AnalysisRunner"/>.</summary>
/// <param name="DataRoot">Where prepared images are written (outside the repository).</param>
/// <param name="DuplicateDistance">dHash distance at or below which two images count as the same.</param>
/// <param name="MaxConsecutiveFailures">Stop the run when this many images fail in a row (for example Ollama is down).</param>
/// <param name="PrefetchCapacity">How many media files may be prepared ahead while the model is busy.</param>
/// <param name="MinFreeDiskBytes">Stop before the disk under <paramref name="DataRoot"/> has less free space than this.</param>
/// <param name="FreeDiskBytes">Measures free space; null uses the real disk. Replaceable in tests.</param>
public sealed record AnalysisRunnerOptions(
    string DataRoot,
    int DuplicateDistance = 6,
    int MaxConsecutiveFailures = 5,
    int PrefetchCapacity = 4,
    long MinFreeDiskBytes = 2L * 1024 * 1024 * 1024,
    Func<string, long>? FreeDiskBytes = null);

/// <summary>
/// Runs one analysis: media → prepared images → near-duplicate filter → cache or model → store.
/// A broken file never stops the run; it is recorded as failed and skipped. Cancelling keeps
/// everything analyzed so far, and a later run reuses it from the cache.
/// Preparation (ffmpeg) runs ahead on a producer task while the model analyzes the previous image;
/// a bounded channel keeps the order and limits how far it may run ahead.
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

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<PreparedMedia>(new BoundedChannelOptions(Math.Max(1, options.PrefetchCapacity))
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var producer = ProduceAsync(source, channel.Writer, runFolder, counts, stop.Token);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                Report(progress, runId, "Hazırlanıyor", counts, item.Media.FullPath);
                if (item.Error is not null)
                {
                    counts.Add(failed: 1);
                    await SaveAsync(runId, Failed(item.Media, null, null, 0, item.Error), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var image in item.Images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outcome = await ProcessImageAsync(runId, item.Media, image, filter, counts, progress, cancellationToken).ConfigureAwait(false);
                    consecutiveFailures = outcome == MediaStatus.Failed ? consecutiveFailures + 1 : 0;
                    if (consecutiveFailures >= options.MaxConsecutiveFailures)
                    {
                        throw new AnalysisFailedException($"Üst üste {consecutiveFailures} görsel analiz edilemedi; analiz durduruldu. Ollama çalışıyor mu?");
                    }
                }
            }

            await producer.ConfigureAwait(false);
            await store.FinishRunAsync(runId, RunStatus.Completed, counts.Snapshot(), null, CancellationToken.None).ConfigureAwait(false);
            Report(progress, runId, "Tamamlandı", counts, null);
            return runId;
        }
        catch (OperationCanceledException)
        {
            await StopProducerAsync(stop, producer).ConfigureAwait(false);
            await store.FinishRunAsync(runId, RunStatus.Cancelled, counts.Snapshot(), "İptal edildi.", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await StopProducerAsync(stop, producer).ConfigureAwait(false);
            await store.FinishRunAsync(runId, RunStatus.Failed, counts.Snapshot(), ex.Message, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Prepares media in order and hands it to the consumer. Never throws: errors complete the channel.</summary>
    private async Task ProduceAsync(IMediaSource source, ChannelWriter<PreparedMedia> writer, string runFolder, MutableCounts counts, CancellationToken cancellationToken)
    {
        try
        {
            var index = 0;
            await foreach (var media in source.GetMediaAsync(cancellationToken).ConfigureAwait(false))
            {
                index++;
                counts.Add(mediaFiles: 1);
                EnsureFreeDiskSpace();
                PreparedMedia item;
                try
                {
                    item = new PreparedMedia(media, await PrepareAsync(media, runFolder, index, cancellationToken).ConfigureAwait(false), null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    item = new PreparedMedia(media, [], ex.Message);
                }

                await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            }

            writer.TryComplete();
        }
        catch (Exception ex)
        {
            // Includes cancellation and errors from the source itself (for example a missing folder).
            writer.TryComplete(ex);
        }
    }

    private void EnsureFreeDiskSpace()
    {
        var free = (options.FreeDiskBytes ?? RealFreeBytes)(options.DataRoot);
        if (free < options.MinFreeDiskBytes)
        {
            throw new IOException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Diskte yeterli boş alan yok ({free / (1024.0 * 1024 * 1024):0.0} GB). Analiz durduruldu; biraz yer açıp tekrar başlat, kalan dosyalar önbellekten devam eder."));
        }
    }

    private static long RealFreeBytes(string path) => new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace;

    private static async Task StopProducerAsync(CancellationTokenSource stop, Task producer)
    {
        await stop.CancelAsync().ConfigureAwait(false);
        await producer.ConfigureAwait(false);
    }

    private async Task<MediaStatus?> ProcessImageAsync(
        int runId,
        MediaFile media,
        PreparedImage prepared,
        NearDuplicateFilter filter,
        MutableCounts counts,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        counts.Add(images: 1);
        var (image, hash) = (prepared.Path, prepared.DHash);
        if (!filter.TryAdd(hash))
        {
            counts.Add(duplicates: 1);
            return null;
        }

        Report(progress, runId, "Analiz ediliyor", counts, media.FullPath);
        var sha = await Sha256Async(image, cancellationToken).ConfigureAwait(false);
        ImageAnalysis analysis;
        var cached = await store.FindCachedAsync(sha, analyzer.ModelName, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            counts.Add(fromCache: 1);
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
                counts.Add(failed: 1);
                await SaveAsync(runId, Failed(media, Relative(image), sha, hash, ex.Message), cancellationToken).ConfigureAwait(false);
                return MediaStatus.Failed;
            }
        }

        var status = analysis.HasOutfit && analysis.Garments.Count > 0 ? MediaStatus.Analyzed : MediaStatus.NoOutfit;
        if (status == MediaStatus.Analyzed)
        {
            counts.Add(analyzed: 1);
        }
        else
        {
            counts.Add(noOutfit: 1);
        }

        var record = new MediaRecord(media.Kind, media.FullPath, Relative(image), sha, hash, media.Caption, media.TakenAt, status, analysis, null);
        await SaveAsync(runId, record, cancellationToken).ConfigureAwait(false);
        return status;
    }

    private async Task<IReadOnlyList<PreparedImage>> PrepareAsync(MediaFile media, string runFolder, int index, CancellationToken cancellationToken)
    {
        var name = $"{index:D4}";
        if (media.Type == MediaFileType.Video)
        {
            return await toolkit.ExtractFramesAsync(media.FullPath, Path.Combine(runFolder, name), cancellationToken).ConfigureAwait(false);
        }

        return [await toolkit.PrepareImageAsync(media.FullPath, Path.Combine(runFolder, name + ".jpg"), cancellationToken).ConfigureAwait(false)];
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

    private sealed record PreparedMedia(MediaFile Media, IReadOnlyList<PreparedImage> Images, string? Error);

    /// <summary>Counters shared by the producer and consumer tasks.</summary>
    private sealed class MutableCounts
    {
        private readonly Lock _lock = new();
        private RunCounts _counts = new(0, 0, 0, 0, 0, 0, 0);

        public void Add(int mediaFiles = 0, int images = 0, int duplicates = 0, int analyzed = 0, int noOutfit = 0, int failed = 0, int fromCache = 0)
        {
            lock (_lock)
            {
                var c = _counts;
                _counts = new RunCounts(
                    c.MediaFiles + mediaFiles,
                    c.Images + images,
                    c.Duplicates + duplicates,
                    c.Analyzed + analyzed,
                    c.NoOutfit + noOutfit,
                    c.Failed + failed,
                    c.FromCache + fromCache);
            }
        }

        public RunCounts Snapshot()
        {
            lock (_lock)
            {
                return _counts;
            }
        }
    }
}
