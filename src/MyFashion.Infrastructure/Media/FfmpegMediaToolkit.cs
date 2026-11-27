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

/// <summary>
/// Resizing, frame extraction and hashing with ffmpeg (and macOS <c>sips</c> as a HEIC fallback).
/// One ffmpeg process produces both the JPEG(s) and the 9x8 grayscale pixels for the hash:
/// the decoded picture is split into two branches, so nothing is decoded twice.
/// </summary>
public sealed class FfmpegMediaToolkit(IProcessRunner runner, MediaToolkitOptions options) : IMediaToolkit
{
    private const string SipsPath = "/usr/bin/sips";
    private const int HashBytes = DHash.Width * DHash.Height;

    private readonly string _ffmpeg = ToolLocator.Find("ffmpeg", options.FfmpegPath)
        ?? throw new ExternalToolException("ffmpeg bulunamadı. Kurulum: brew install ffmpeg");

    private string Scale => string.Create(
        CultureInfo.InvariantCulture,
        $"scale='min({options.MaxImageSide},iw)':'min({options.MaxImageSide},ih)':force_original_aspect_ratio=decrease");

    private static string HashBranch => $"scale={DHash.Width}:{DHash.Height}:flags=area,format=gray";

    public async Task<IReadOnlyList<PreparedImage>> ExtractFramesAsync(string videoPath, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var fps = string.Create(CultureInfo.InvariantCulture, $"fps=1/{options.FrameIntervalSeconds}");
        var maxFrames = options.MaxFramesPerVideo.ToString(CultureInfo.InvariantCulture);

        var pixels = await RunFfmpegAsync(
            [
                "-i", videoPath,
                "-filter_complex", $"[0:v]{fps},{Scale},split=2[jpg][h];[h]{HashBranch}[gray]",
                "-map", "[jpg]", "-frames:v", maxFrames, "-q:v", "3", Path.Combine(outputDirectory, "frame_%04d.jpg"),
                "-map", "[gray]", "-frames:v", maxFrames, "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1",
            ],
            cancellationToken).ConfigureAwait(false);

        var frames = Directory.EnumerateFiles(outputDirectory, "frame_*.jpg").Order(StringComparer.Ordinal).ToList();
        if (pixels.Length != frames.Count * HashBytes)
        {
            throw new ExternalToolException($"ffmpeg beklenmeyen çıktı üretti: {frames.Count} kare, {pixels.Length} bayt hash verisi.");
        }

        return [.. frames.Select((path, i) => new PreparedImage(path, DHash.FromGrayPixels(pixels.AsSpan(i * HashBytes, HashBytes))))];
    }

    public async Task<PreparedImage> PrepareImageAsync(string imagePath, string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var source = imagePath;
        if (IsHeic(imagePath) && File.Exists(SipsPath))
        {
            // Many ffmpeg builds cannot decode HEIC (iPhone photos); macOS can. Convert first, then continue as JPEG.
            source = outputPath + ".heic.jpg";
            var converted = await runner.RunAsync(SipsPath, ["-s", "format", "jpeg", imagePath, "--out", source], options.Timeout, cancellationToken)
                .ConfigureAwait(false);
            if (converted.ExitCode != 0 || !File.Exists(source))
            {
                throw new ExternalToolException($"HEIC görsel dönüştürülemedi: {Path.GetFileName(imagePath)}");
            }
        }

        try
        {
            var pixels = await RunFfmpegAsync(
                [
                    "-i", source,
                    "-filter_complex", $"[0:v]{Scale},split=2[jpg][h];[h]{HashBranch}[gray]",
                    "-map", "[jpg]", "-frames:v", "1", "-q:v", "3", outputPath,
                    "-map", "[gray]", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1",
                ],
                cancellationToken).ConfigureAwait(false);

            return new PreparedImage(outputPath, DHash.FromGrayPixels(pixels));
        }
        catch (ArgumentException ex)
        {
            throw new ExternalToolException($"Görsel okunamadı: {Path.GetFileName(imagePath)}", ex);
        }
        finally
        {
            if (!ReferenceEquals(source, imagePath))
            {
                File.Delete(source);
            }
        }
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
