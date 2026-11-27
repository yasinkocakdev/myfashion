namespace MyFashion.Core.Media;

/// <summary>An analysis-ready JPEG and its perceptual hash.</summary>
public sealed record PreparedImage(string Path, ulong DHash);

/// <summary>Image and video operations needed before analysis.</summary>
public interface IMediaToolkit
{
    /// <summary>
    /// Extracts evenly spaced frames from a video as resized JPEG files in <paramref name="outputDirectory"/>,
    /// together with each frame's hash. Returned in playback order.
    /// </summary>
    Task<IReadOnlyList<PreparedImage>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken);

    /// <summary>Writes a resized JPEG copy of the image (never upscaled) and returns it with its hash.</summary>
    Task<PreparedImage> PrepareImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken);
}
