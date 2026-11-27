namespace MyFashion.Core.Media;

/// <summary>Image and video operations needed before analysis.</summary>
public interface IMediaToolkit
{
    /// <summary>Extracts evenly spaced frames from a video as JPEG files in <paramref name="outputDirectory"/>. Returns their paths in order.</summary>
    Task<IReadOnlyList<string>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken);

    /// <summary>Writes a JPEG copy of the image, scaled down so its longest side fits the analysis size (never upscaled).</summary>
    Task NormalizeImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken);

    /// <summary>Computes the 64-bit difference hash of an image.</summary>
    Task<ulong> ComputeDHashAsync(string imagePath, CancellationToken cancellationToken);
}
