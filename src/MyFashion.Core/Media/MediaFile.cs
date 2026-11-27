namespace MyFashion.Core.Media;

/// <summary>Where a piece of media came from on Instagram (or "manual" for anything else).</summary>
public enum MediaKind
{
    Manual,
    Post,
    Reel,
    Highlight,
}

public enum MediaFileType
{
    Image,
    Video,
}

/// <summary>A media file on local disk, ready for preprocessing.</summary>
/// <param name="FullPath">Absolute path on disk.</param>
/// <param name="Type">Image or video.</param>
/// <param name="Kind">Post, reel, highlight or manual.</param>
/// <param name="Caption">Instagram caption, when known. Treated as untrusted data.</param>
/// <param name="TakenAt">When it was posted, when known.</param>
/// <param name="SourceId">Stable id from the source (for example the Instagram media id), when known.</param>
public sealed record MediaFile(
    string FullPath,
    MediaFileType Type,
    MediaKind Kind,
    string? Caption = null,
    DateTimeOffset? TakenAt = null,
    string? SourceId = null);

/// <summary>Produces media files to analyze.</summary>
public interface IMediaSource
{
    IAsyncEnumerable<MediaFile> GetMediaAsync(CancellationToken cancellationToken);
}
