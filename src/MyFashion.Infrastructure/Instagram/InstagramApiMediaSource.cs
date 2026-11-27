using System.Runtime.CompilerServices;
using MyFashion.Core.Instagram;
using MyFashion.Core.IO;
using MyFashion.Core.Media;

namespace MyFashion.Infrastructure.Instagram;

/// <summary>
/// Downloads a professional account's posts and reels through the official API and hands them to the analysis
/// as local files. Downloads go only to Instagram/Facebook CDN hosts over https (SSRF guard), are size-limited,
/// and are skipped when the same media id was downloaded before.
/// </summary>
public sealed class InstagramApiMediaSource(
    InstagramGraphClient client,
    HttpClient http,
    InstagramCredentials credentials,
    InstagramUsername target,
    string downloadRoot) : IMediaSource
{
    public const long MaxDownloadBytes = 50L * 1024 * 1024;

    private static readonly string[] AllowedHostSuffixes = [".cdninstagram.com", ".fbcdn.net"];

    public async IAsyncEnumerable<MediaFile> GetMediaAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var folder = PathGuard.ResolveWithin(downloadRoot, target.Value);
        Directory.CreateDirectory(folder);
        var media = await client.GetMediaAsync(credentials, target, cancellationToken).ConfigureAwait(false);

        foreach (var item in media)
        {
            var kind = item.MediaProductType?.Equals("REELS", StringComparison.OrdinalIgnoreCase) == true ? MediaKind.Reel : MediaKind.Post;
            var parts = item.MediaType.Equals("CAROUSEL_ALBUM", StringComparison.OrdinalIgnoreCase) && item.Children.Count > 0
                ? item.Children
                : [item];

            foreach (var part in parts)
            {
                var (url, type) = ChooseDownload(part);
                if (url is null || !IsSafeId(part.Id))
                {
                    continue;
                }

                var path = Path.Combine(folder, part.Id + (type == MediaFileType.Video ? ".mp4" : ".jpg"));
                if (!File.Exists(path) && !await TryDownloadAsync(url, path, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                yield return new MediaFile(path, type, kind, item.Caption, item.Timestamp, part.Id);
            }
        }
    }

    /// <summary>Video when the API gives one; otherwise the image or the video's cover image (reels with downloads turned off).</summary>
    internal static (Uri? Url, MediaFileType Type) ChooseDownload(InstagramMedia media) =>
        media.MediaType.Equals("VIDEO", StringComparison.OrdinalIgnoreCase)
            ? media.MediaUrl is not null ? (media.MediaUrl, MediaFileType.Video) : (media.ThumbnailUrl, MediaFileType.Image)
            : (media.MediaUrl, MediaFileType.Image);

    internal static bool IsAllowedHost(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && AllowedHostSuffixes.Any(suffix => url.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsSafeId(string id) => id.Length is > 0 and <= 40 && id.All(char.IsAsciiDigit);

    /// <summary>One failed download (network blip, CDN error, timeout) skips that file instead of failing the whole analysis.</summary>
    private async Task<bool> TryDownloadAsync(Uri url, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadAsync(url, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return false;
        }
    }

    private async Task<bool> DownloadAsync(Uri url, string path, CancellationToken cancellationToken)
    {
        if (!IsAllowedHost(url))
        {
            return false;
        }

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!response.IsSuccessStatusCode
            || !(contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            || response.Content.Headers.ContentLength > MaxDownloadBytes)
        {
            return false;
        }

        var partial = path + ".part";
        try
        {
            var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (input.ConfigureAwait(false))
            {
                var output = File.Create(partial);
                await using (output.ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxDownloadBytes)
                        {
                            return false;
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            File.Move(partial, path, overwrite: true);
            return true;
        }
        finally
        {
            File.Delete(partial);
        }
    }
}
