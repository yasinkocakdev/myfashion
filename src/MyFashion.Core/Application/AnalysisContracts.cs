using MyFashion.Core.Analysis;
using MyFashion.Core.Media;

namespace MyFashion.Core.Application;

public enum RunSource
{
    Folder,
    InstagramApi,
}

public enum RunStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>What happened to one prepared image.</summary>
public enum MediaStatus
{
    Analyzed,
    NoOutfit,
    Failed,
}

/// <summary>One analyzed (or failed) image, ready to be stored.</summary>
/// <param name="Kind">Post, reel, highlight or manual.</param>
/// <param name="SourcePath">The original file the image came from.</param>
/// <param name="PreparedPath">The prepared JPEG (relative to the data root) that was analyzed.</param>
/// <param name="Sha256">Hash of the prepared JPEG, used as the analysis cache key.</param>
/// <param name="DHash">Perceptual hash.</param>
/// <param name="Caption">Caption, if any.</param>
/// <param name="TakenAt">Posting time, if known.</param>
/// <param name="Status">Outcome.</param>
/// <param name="Analysis">The analysis, or null when it failed.</param>
/// <param name="Error">Failure reason, when <paramref name="Status"/> is Failed.</param>
public sealed record MediaRecord(
    MediaKind Kind,
    string SourcePath,
    string? PreparedPath,
    string? Sha256,
    ulong DHash,
    string? Caption,
    DateTimeOffset? TakenAt,
    MediaStatus Status,
    ImageAnalysis? Analysis,
    string? Error);

/// <summary>Totals for a run.</summary>
public sealed record RunCounts(int MediaFiles, int Images, int Duplicates, int Analyzed, int NoOutfit, int Failed, int FromCache);

/// <summary>Progress reported while a run is going.</summary>
public sealed record RunProgress(int RunId, string Stage, RunCounts Counts, string? CurrentItem);

/// <summary>Persistence used by <see cref="AnalysisRunner"/>.</summary>
public interface IAnalysisStore
{
    Task<int> StartRunAsync(string accountName, RunSource source, string model, CancellationToken cancellationToken);

    /// <summary>Returns an earlier analysis of the exact same image by the same model, if any.</summary>
    Task<ImageAnalysis?> FindCachedAsync(string sha256, string model, CancellationToken cancellationToken);

    Task SaveMediaAsync(int runId, MediaRecord record, CancellationToken cancellationToken);

    Task FinishRunAsync(int runId, RunStatus status, RunCounts counts, string? errorMessage, CancellationToken cancellationToken);
}
