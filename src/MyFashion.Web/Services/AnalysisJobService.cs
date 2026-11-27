using System.Collections.Concurrent;
using System.Threading.Channels;
using MyFashion.Core.Application;
using MyFashion.Core.Instagram;
using MyFashion.Core.Media;
using MyFashion.Infrastructure.Instagram;
using MyFashion.Infrastructure.Media;

namespace MyFashion.Web.Services;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// An immutable snapshot of a background analysis, safe to read from any thread.
/// <c>Folder</c> is the folder of screenshots/recordings (or null); <c>UseApi</c> also fetches posts and reels through the official Instagram API.
/// </summary>
public sealed record AnalysisJob(
    Guid Id,
    string Account,
    string? Folder,
    bool UseApi,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    RunProgress? Progress = null,
    int? RunId = null,
    string? Error = null)
{
    public bool IsFinished => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;
}

/// <summary>
/// Runs analyses one at a time in the background (the local model can only do one image at a time anyway),
/// so the UI stays responsive. Pages subscribe to <see cref="Changed"/> for live progress.
/// </summary>
public sealed partial class AnalysisJobService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AnalysisJobService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, AnalysisJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();

    public event Action<AnalysisJob>? Changed;

    /// <summary>Most jobs that may wait in the queue; more is almost certainly a mistake (double clicks, a stuck model).</summary>
    public const int MaxQueuedJobs = 10;

    /// <exception cref="InvalidOperationException">The queue is full.</exception>
    public AnalysisJob Enqueue(string account, string? folder, bool useApi)
    {
        if (_jobs.Values.Count(j => !j.IsFinished) >= MaxQueuedJobs)
        {
            throw new InvalidOperationException($"Sırada zaten {MaxQueuedJobs} analiz var. Önce onların bitmesini bekle.");
        }

        var job = new AnalysisJob(Guid.NewGuid(), account, folder, useApi, JobStatus.Queued, clock.GetUtcNow());
        _jobs[job.Id] = job;
        _cancellations[job.Id] = new CancellationTokenSource();
        _queue.Writer.TryWrite(job.Id);
        Changed?.Invoke(job);
        return job;
    }

    public AnalysisJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public IReadOnlyList<AnalysisJob> Recent() => [.. _jobs.Values.OrderByDescending(j => j.CreatedAt).Take(10)];

    public void Cancel(Guid id)
    {
        if (_cancellations.TryGetValue(id, out var cts))
        {
            cts.Cancel();
        }

        if (_jobs.TryGetValue(id, out var job) && job.Status == JobStatus.Queued)
        {
            Update(job with { Status = JobStatus.Cancelled, Error = "İptal edildi." });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!_jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Queued)
            {
                continue;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cancellations[id].Token);
            await RunJobAsync(job, cts.Token).ConfigureAwait(false);
            _cancellations.TryRemove(id, out var own);
            own?.Dispose();
        }
    }

    private async Task RunJobAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        Update(job with { Status = JobStatus.Running });
        var progress = new InlineProgress(p => Update(Get(job.Id)! with { Progress = p, RunId = p.RunId }));
        try
        {
            using var scope = scopes.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AnalysisRunner>();
            var (source, kind) = await CreateSourceAsync(scope.ServiceProvider, job, cancellationToken).ConfigureAwait(false);
            var runId = await runner.RunAsync(job.Account, source, kind, progress, cancellationToken).ConfigureAwait(false);
            Update(Get(job.Id)! with { Status = JobStatus.Completed, RunId = runId });
        }
        catch (OperationCanceledException)
        {
            Update(Get(job.Id)! with { Status = JobStatus.Cancelled, Error = "İptal edildi." });
        }
        catch (Exception ex)
        {
            LogJobFailed(logger, ex, job.Id);
            Update(Get(job.Id)! with { Status = JobStatus.Failed, Error = ex.Message });
        }
    }

    private static async Task<(IMediaSource Source, RunSource Kind)> CreateSourceAsync(IServiceProvider services, AnalysisJob job, CancellationToken cancellationToken)
    {
        var sources = new List<IMediaSource>();
        if (job.UseApi)
        {
            var credentials = await services.GetRequiredService<InstagramCredentialStore>().GetAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Instagram bağlantısı yok. Ayarlar'dan erişim anahtarını gir.");
            if (!InstagramUsername.TryParse(job.Account, out var target, out var error))
            {
                throw new InvalidOperationException(error);
            }

            var cdn = services.GetRequiredService<IHttpClientFactory>().CreateClient(InstagramCdn);
            var downloads = Path.Combine(services.GetRequiredService<AppPaths>().DataRoot, "downloads");
            sources.Add(new InstagramApiMediaSource(services.GetRequiredService<InstagramGraphClient>(), cdn, credentials, target, downloads));
        }

        if (job.Folder is not null)
        {
            sources.Add(new FolderMediaSource(job.Folder));
        }

        var kind = (job.UseApi, job.Folder is not null) switch
        {
            (true, true) => RunSource.Mixed,
            (true, false) => RunSource.InstagramApi,
            _ => RunSource.Folder,
        };
        return (sources.Count == 1 ? sources[0] : new CompositeMediaSource(sources), kind);
    }

    /// <summary>Name of the HttpClient that downloads media from the Instagram CDN (no redirects, see Program.cs).</summary>
    public const string InstagramCdn = "instagram-cdn";

    private void Update(AnalysisJob job)
    {
        _jobs[job.Id] = job;
        Changed?.Invoke(job);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis job {JobId} failed")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, Guid jobId);

    /// <summary>Reports synchronously on the worker thread (no captured context needed in a background service).</summary>
    private sealed class InlineProgress(Action<RunProgress> report) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => report(value);
    }
}
