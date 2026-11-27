using System.Globalization;
using MyFashion.Core.Media;
using MyFashion.Infrastructure.Processes;

namespace MyFashion.Infrastructure.Media;

/// <summary>Settings for <see cref="FfmpegMediaToolkit"/>.</summary>
public sealed record MediaToolkitOptions
{
    /// <summary>Path to ffmpeg. Null means "find it" (Homebrew folders, then PATH).</summary>
    public string? FfmpegPath { get; init; }

    /// <summary>Longest side of images sent to the model, in pixels.</summary>
    public int MaxImageSide { get; init; } = 768;

    /// <summary>One frame is taken every this many seconds of video.</summary>
    public double FrameIntervalSeconds { get; init; } = 2;

    /// <summary>Upper limit of frames taken from one video.</summary>
    public int MaxFramesPerVideo { get; init; } = 120;

    /// <summary>Time limit for one ffmpeg call.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>Frame extraction, resizing and hashing with ffmpeg (and macOS <c>sips</c> as a HEIC fallback).</summary>
public sealed class FfmpegMediaToolkit(IProcessRunner runner, MediaToolkitOptions options) : IMediaToolkit
{
    private const string SipsPath = "/usr/bin/sips";

    private readonly string _ffmpeg = ToolLocator.Find("ffmpeg", options.FfmpegPath)
        ?? throw new ExternalToolException("ffmpeg bulunamadı. Kurulum: brew install ffmpeg");

    private string ScaleFilter => string.Create(
        CultureInfo.InvariantCulture,
        $"scale='min({options.MaxImageSide},iw)':'min({options.MaxImageSide},ih)':force_original_aspect_ratio=decrease");

    public async Task<IReadOnlyList<string>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var fps = string.Create(CultureInfo.InvariantCulture, $"fps=1/{options.FrameIntervalSeconds}");
        var pattern = Path.Combine(outputDirectory, "frame_%04d.jpg");

        await RunFfmpegAsync(
            [
                "-i", videoPath,
                "-vf", $"{fps},{ScaleFilter}",
                "-frames:v", options.MaxFramesPerVideo.ToString(CultureInfo.InvariantCulture),
                "-q:v", "3",
                pattern,
            ],
            cancellationToken).ConfigureAwait(false);

        return Directory.EnumerateFiles(outputDirectory, "frame_*.jpg").Order(StringComparer.Ordinal).ToList();
    }

    public async Task NormalizeImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        try
        {
            await RunFfmpegAsync(["-i", imagePath, "-vf", ScaleFilter, "-frames:v", "1", "-q:v", "3", outputPath], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExternalToolException) when (IsHeic(imagePath) && File.Exists(SipsPath))
        {
            // Some ffmpeg builds cannot decode HEIC (iPhone photos); macOS can.
            var result = await runner.RunAsync(
                SipsPath,
                ["-s", "format", "jpeg", "-Z", options.MaxImageSide.ToString(CultureInfo.InvariantCulture), imagePath, "--out", outputPath],
                options.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new ExternalToolException($"HEIC görsel dönüştürülemedi: {Path.GetFileName(imagePath)}");
            }
        }
    }

    public async Task<ulong> ComputeDHashAsync(string imagePath, CancellationToken cancellationToken)
    {
        var pixels = await RunFfmpegAsync(
            [
                "-i", imagePath,
                "-vf", $"scale={DHash.Width}:{DHash.Height}:flags=area,format=gray",
                "-frames:v", "1",
                "-f", "rawvideo",
                "-pix_fmt", "gray",
                "pipe:1",
            ],
            cancellationToken).ConfigureAwait(false);

        return DHash.FromGrayPixels(pixels);
    }

    private async Task<byte[]> RunFfmpegAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        string[] common = ["-hide_banner", "-nostdin", "-loglevel", "error", "-y"];
        var result = await runner.RunAsync(_ffmpeg, [.. common, .. arguments], options.Timeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var reason = result.StandardError.Trim();
            throw new ExternalToolException($"ffmpeg hata verdi ({result.ExitCode}): {(reason.Length > 300 ? reason[..300] : reason)}");
        }

        return result.StandardOutput;
    }

    private static bool IsHeic(string path) =>
        Path.GetExtension(path).Equals(".heic", StringComparison.OrdinalIgnoreCase);
}
