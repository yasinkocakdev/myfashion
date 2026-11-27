using System.Globalization;
using System.Text;
using MyFashion.Core.Media;
using MyFashion.Infrastructure.Media;
using MyFashion.Infrastructure.Processes;

namespace MyFashion.Tests.Infrastructure;

/// <summary>Integration tests against the real ffmpeg. Skipped when ffmpeg is not installed.</summary>
public sealed class FfmpegMediaToolkitTests : IDisposable
{
    private static readonly string? Ffmpeg = ToolLocator.Find("ffmpeg");
    private static readonly string? Ffprobe = ToolLocator.Find("ffprobe");

    private readonly string _dir = Path.Combine(Directory.CreateTempSubdirectory("myfashion-ffmpeg-").FullName, "öne çıkanlar test");
    private readonly ProcessRunner _runner = new();

    public FfmpegMediaToolkitTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

    [Fact]
    public async Task ExtractFrames_takes_one_frame_per_interval_and_scales_down()
    {
        SkipWithoutFfmpeg();
        var video = await GenerateAsync("klip.mp4", ["-f", "lavfi", "-i", "testsrc=size=1280x720:rate=25:duration=10", "-pix_fmt", "yuv420p"]);
        var toolkit = CreateToolkit();

        var frames = await toolkit.ExtractFramesAsync(video, Path.Combine(_dir, "kareler"), TestContext.Current.CancellationToken);

        Assert.InRange(frames.Count, 5, 6);
        var (width, height) = await SizeAsync(frames[0]);
        Assert.Equal(768, width);
        Assert.Equal(432, height);
    }

    [Fact]
    public async Task ExtractFrames_respects_the_frame_limit()
    {
        SkipWithoutFfmpeg();
        var video = await GenerateAsync("uzun.mp4", ["-f", "lavfi", "-i", "testsrc=size=320x240:rate=10:duration=30", "-pix_fmt", "yuv420p"]);
        var toolkit = CreateToolkit(new MediaToolkitOptions { MaxFramesPerVideo = 3 });

        var frames = await toolkit.ExtractFramesAsync(video, Path.Combine(_dir, "sinirli"), TestContext.Current.CancellationToken);

        Assert.Equal(3, frames.Count);
    }

    [Fact]
    public async Task NormalizeImage_scales_large_images_and_never_upscales_small_ones()
    {
        SkipWithoutFfmpeg();
        var large = await GenerateAsync("büyük.png", ["-f", "lavfi", "-i", "testsrc=size=1000x2000:duration=1", "-frames:v", "1"]);
        var small = await GenerateAsync("küçük.png", ["-f", "lavfi", "-i", "testsrc=size=300x200:duration=1", "-frames:v", "1"]);
        var toolkit = CreateToolkit();

        await toolkit.NormalizeImageAsync(large, Path.Combine(_dir, "out", "büyük.jpg"), TestContext.Current.CancellationToken);
        await toolkit.NormalizeImageAsync(small, Path.Combine(_dir, "out", "küçük.jpg"), TestContext.Current.CancellationToken);

        Assert.Equal((384, 768), await SizeAsync(Path.Combine(_dir, "out", "büyük.jpg")));
        Assert.Equal((300, 200), await SizeAsync(Path.Combine(_dir, "out", "küçük.jpg")));
    }

    [Fact]
    public async Task DHash_is_stable_for_reencoded_copies_and_different_for_different_images()
    {
        SkipWithoutFfmpeg();
        var original = await GenerateAsync("asil.png", ["-f", "lavfi", "-i", "testsrc=size=640x480:duration=1", "-frames:v", "1"]);
        var copy = await GenerateAsync("kopya.jpg", ["-i", original, "-vf", "scale=500:375", "-q:v", "8"]);
        var other = await GenerateAsync("diger.png", ["-f", "lavfi", "-i", "mandelbrot=size=640x480", "-frames:v", "1"]);
        var toolkit = CreateToolkit();
        var ct = TestContext.Current.CancellationToken;

        var a = await toolkit.ComputeDHashAsync(original, ct);
        var b = await toolkit.ComputeDHashAsync(copy, ct);
        var c = await toolkit.ComputeDHashAsync(other, ct);

        Assert.Equal(a, await toolkit.ComputeDHashAsync(original, ct));
        Assert.InRange(DHash.Distance(a, b), 0, 6);
        Assert.True(DHash.Distance(a, c) > 12, $"distance was {DHash.Distance(a, c)}");
    }

    [Fact]
    public async Task Corrupt_file_gives_a_clear_error()
    {
        SkipWithoutFfmpeg();
        var bad = Path.Combine(_dir, "bozuk.jpg");
        await File.WriteAllTextAsync(bad, "not an image", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ExternalToolException>(() =>
            CreateToolkit().ComputeDHashAsync(bad, TestContext.Current.CancellationToken));

        Assert.Contains("ffmpeg", ex.Message, StringComparison.Ordinal);
    }

    private static void SkipWithoutFfmpeg() =>
        Assert.SkipWhen(Ffmpeg is null || Ffprobe is null, "ffmpeg/ffprobe kurulu değil.");

    private FfmpegMediaToolkit CreateToolkit(MediaToolkitOptions? options = null) =>
        new(_runner, options ?? new MediaToolkitOptions());

    private async Task<string> GenerateAsync(string name, string[] inputArguments)
    {
        var path = Path.Combine(_dir, name);
        var result = await _runner.RunAsync(
            Ffmpeg!,
            ["-hide_banner", "-loglevel", "error", "-y", .. inputArguments, path],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return path;
    }

    private async Task<(int Width, int Height)> SizeAsync(string path)
    {
        var result = await _runner.RunAsync(
            Ffprobe!,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", path],
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        var parts = Encoding.UTF8.GetString(result.StandardOutput).Trim().Split('x');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
