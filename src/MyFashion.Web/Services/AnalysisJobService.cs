using System.Collections.Concurrent;
using System.Threading.Channels;
using MyFashion.Core.Application;
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

/// <summary>An immutable snapshot of a background analysis, safe to read from any thread.</summary>
public sealed record AnalysisJob(
    Guid Id,
    string Account,
    string Folder,
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

    public AnalysisJob Enqueue(string account, string folder)
    {
        var job = new AnalysisJob(Guid.NewGuid(), account, folder, JobStatus.Queued, clock.GetUtcNow());
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
            var runId = await runner.RunAsync(job.Account, new FolderMediaSource(job.Folder), RunSource.Folder, progress, cancellationToken).ConfigureAwait(false);
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
