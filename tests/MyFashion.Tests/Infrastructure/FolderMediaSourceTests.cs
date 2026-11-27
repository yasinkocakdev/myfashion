using MyFashion.Core.Media;
using MyFashion.Infrastructure.Media;

namespace MyFashion.Tests.Infrastructure;

public sealed class FolderMediaSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("myfashion-folder-").FullName;
    private readonly string _outside = Directory.CreateTempSubdirectory("myfashion-outside-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    [Fact]
    public async Task Lists_supported_images_and_videos_with_kind_from_folder_name()
    {
        Touch("posts/a.jpg");
        Touch("reels/b.MP4");
        Touch("öne çıkanlar/yaz tatili.mov");
        Touch("highlights/c.png");
        Touch("misc/d.webp");
        Touch("e.heic");

        var files = await ListAsync();

        Assert.Equal(6, files.Count);
        Assert.Contains(files, f => f.FullPath.EndsWith("a.jpg", StringComparison.Ordinal) && f.Kind == MediaKind.Post && f.Type == MediaFileType.Image);
        Assert.Contains(files, f => f.FullPath.EndsWith("b.MP4", StringComparison.Ordinal) && f.Kind == MediaKind.Reel && f.Type == MediaFileType.Video);
        Assert.Contains(files, f => f.FullPath.EndsWith("yaz tatili.mov", StringComparison.Ordinal) && f.Kind == MediaKind.Highlight && f.Type == MediaFileType.Video);
        Assert.Contains(files, f => f.FullPath.EndsWith("c.png", StringComparison.Ordinal) && f.Kind == MediaKind.Highlight);
        Assert.Contains(files, f => f.FullPath.EndsWith("d.webp", StringComparison.Ordinal) && f.Kind == MediaKind.Manual);
        Assert.Contains(files, f => f.FullPath.EndsWith("e.heic", StringComparison.Ordinal) && f.Kind == MediaKind.Manual);
    }

    [Fact]
    public async Task Skips_hidden_unsupported_and_empty_files()
    {
        Touch(".DS_Store");
        Touch(".hidden/x.jpg");
        Touch("notes.txt");
        Touch("script.sh");
        Touch("empty.jpg", bytes: 0);
        Touch("ok.jpg");

        var files = await ListAsync();

        Assert.Single(files);
        Assert.EndsWith("ok.jpg", files[0].FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_follow_symlinks_out_of_the_folder()
    {
        File.WriteAllBytes(Path.Combine(_outside, "secret.jpg"), [1, 2, 3]);
        File.CreateSymbolicLink(Path.Combine(_root, "link.jpg"), Path.Combine(_outside, "secret.jpg"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "linkdir"), _outside);
        Touch("real.jpg");

        var files = await ListAsync();

        Assert.Single(files);
        Assert.All(files, f => Assert.StartsWith(_root, f.FullPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returns_files_in_a_stable_order()
    {
        Touch("b.jpg");
        Touch("a.jpg");
        Touch("c/a.jpg");

        var first = (await ListAsync()).Select(f => f.FullPath).ToList();
        var second = (await ListAsync()).Select(f => f.FullPath).ToList();

        Assert.Equal(first, second);
        Assert.Equal(first.Order(StringComparer.Ordinal), first);
    }

    [Fact]
    public async Task Missing_folder_throws_a_clear_error()
    {
        var source = new FolderMediaSource(Path.Combine(_root, "does-not-exist"));

        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in source.GetMediaAsync(TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
    }

    private async Task<List<MediaFile>> ListAsync()
    {
        var list = new List<MediaFile>();
        await foreach (var file in new FolderMediaSource(_root).GetMediaAsync(TestContext.Current.CancellationToken))
        {
            list.Add(file);
        }

        return list;
    }

    private void Touch(string relative, int bytes = 4)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }
}
