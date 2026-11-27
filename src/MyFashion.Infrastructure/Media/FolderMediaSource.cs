using System.Runtime.CompilerServices;
using MyFashion.Core.IO;
using MyFashion.Core.Media;

namespace MyFashion.Infrastructure.Media;

/// <summary>
/// Reads screenshots and screen recordings from a local folder. A sub-folder name decides the kind:
/// "posts", "reels", "highlights" (or Turkish "öne çıkanlar"); everything else is <see cref="MediaKind.Manual"/>.
/// Hidden files, empty files, unsupported extensions and symbolic links are skipped.
/// </summary>
public sealed class FolderMediaSource(string folder) : IMediaSource
{
    private static readonly Dictionary<string, MediaFileType> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = MediaFileType.Image,
        [".jpeg"] = MediaFileType.Image,
        [".png"] = MediaFileType.Image,
        [".webp"] = MediaFileType.Image,
        [".heic"] = MediaFileType.Image,
        [".mp4"] = MediaFileType.Video,
        [".mov"] = MediaFileType.Video,
        [".m4v"] = MediaFileType.Video,
    };

    public async IAsyncEnumerable<MediaFile> GetMediaAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Klasör bulunamadı: {root}");
        }

        foreach (var path in EnumerateFiles(root).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = Extensions[Path.GetExtension(path)];
            yield return new MediaFile(path, type, KindFor(root, path));
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                // Never follow links: they could point outside the folder.
                if (entry.LinkTarget is not null || entry.Name.StartsWith('.'))
                {
                    continue;
                }

                if (entry is DirectoryInfo sub)
                {
                    pending.Push(sub);
                }
                else if (entry is FileInfo file
                         && Extensions.ContainsKey(file.Extension)
                         && file.Length > 0
                         && PathGuard.IsWithin(root, file.FullName))
                {
                    yield return file.FullName;
                }
            }
        }
    }

    private static MediaKind KindFor(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar).SkipLast(1))
        {
            var name = segment.ToLowerInvariant();
            if (name.Contains("highlight", StringComparison.Ordinal) || name.Contains("öne çıkan", StringComparison.Ordinal))
            {
                return MediaKind.Highlight;
            }

            if (name.Contains("reel", StringComparison.Ordinal))
            {
                return MediaKind.Reel;
            }

            if (name.Contains("post", StringComparison.Ordinal) || name.Contains("gönderi", StringComparison.Ordinal))
            {
                return MediaKind.Post;
            }
        }

        return MediaKind.Manual;
    }
}
