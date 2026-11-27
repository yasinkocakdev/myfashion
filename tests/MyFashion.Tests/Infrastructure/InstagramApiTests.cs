using System.Net;
using System.Text;
using MyFashion.Core.Instagram;
using MyFashion.Core.Media;
using MyFashion.Infrastructure.Instagram;

namespace MyFashion.Tests.Infrastructure;

public sealed class InstagramApiTests : IDisposable
{
    private static readonly InstagramCredentials Credentials = new("17841400000000000", "secret-token-123");

    private readonly string _root = Directory.CreateTempSubdirectory("myfashion-ig-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Follows_pagination_and_sends_the_token_only_in_the_header()
    {
        var handler = new Handler(request => request.RequestUri!.AbsoluteUri.Contains("after%28CUR1%29", StringComparison.Ordinal)
            ? Json(Page(["3"], next: null))
            : Json(Page(["1", "2"], next: "CUR1")));
        var client = new InstagramGraphClient(new HttpClient(handler), new InstagramApiOptions());

        var media = await client.GetMediaAsync(Credentials, User("brand.account"), TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3"], media.Select(m => m.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("Bearer secret-token-123", r.Headers.Authorization!.ToString());
            Assert.DoesNotContain("secret-token", r.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.StartsWith("https://graph.facebook.com/v27.0/17841400000000000?fields=", r.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        });
        Assert.Contains("business_discovery.username%28brand.account%29", handler.Requests[0].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_at_the_media_limit()
    {
        var handler = new Handler(_ => Json(Page(["1", "2", "3"], next: "MORE")));
        var client = new InstagramGraphClient(new HttpClient(handler), new InstagramApiOptions { MaxMedia = 4 });

        var media = await client.GetMediaAsync(Credentials, User("brand"), TestContext.Current.CancellationToken);

        Assert.Equal(4, media.Count);
    }

    [Fact]
    public async Task Stops_when_the_api_repeats_the_same_cursor()
    {
        var handler = new Handler(_ => Json(Page(["1", "2"], next: "SAME")));
        var client = new InstagramGraphClient(new HttpClient(handler), new InstagramApiOptions());

        var media = await client.GetMediaAsync(Credentials, User("brand"), TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2"], media.Select(m => m.Id).Distinct());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(190, InstagramErrorKind.InvalidToken, "süresi dolmuş")]
    [InlineData(4, InstagramErrorKind.RateLimited, "kullanım sınırına")]
    [InlineData(32, InstagramErrorKind.RateLimited, "kullanım sınırına")]
    [InlineData(110, InstagramErrorKind.NotBusinessAccount, "İçerik Üreticisi")]
    [InlineData(10, InstagramErrorKind.PermissionDenied, "izni yok")]
    public async Task Graph_errors_become_actionable_messages(int code, InstagramErrorKind kind, string expected)
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent($$$"""{"error":{"message":"raw message with secret-token-123","code":{{{code}}}}}"""),
        });
        var client = new InstagramGraphClient(new HttpClient(handler), new InstagramApiOptions());

        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.GetMediaAsync(Credentials, User("brand"), TestContext.Current.CancellationToken));

        Assert.Equal(kind, ex.Kind);
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_non_numeric_user_id_before_calling_the_api()
    {
        var handler = new Handler(_ => Json("{}"));
        var client = new InstagramGraphClient(new HttpClient(handler), new InstagramApiOptions());

        await Assert.ThrowsAsync<InstagramApiException>(() =>
            client.GetMediaAsync(Credentials with { IgUserId = "1/../me" }, User("brand"), TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Credentials_never_print_the_token()
    {
        Assert.DoesNotContain("secret-token", Credentials.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://scontent-ist1-1.cdninstagram.com/v/t51/1.jpg", true)]
    [InlineData("https://video.xx.fbcdn.net/v/1.mp4", true)]
    [InlineData("http://scontent.cdninstagram.com/1.jpg", false)]
    [InlineData("https://evil.com/cdninstagram.com/1.jpg", false)]
    [InlineData("https://cdninstagram.com.evil.com/1.jpg", false)]
    [InlineData("https://127.0.0.1/1.jpg", false)]
    [InlineData("https://scontent.cdninstagram.com:8443/1.jpg", false)]
    public void Only_https_instagram_cdn_hosts_are_downloaded(string url, bool allowed)
    {
        Assert.Equal(allowed, InstagramApiMediaSource.IsAllowedHost(new Uri(url)));
    }

    [Fact]
    public async Task Downloads_posts_reels_and_carousels_with_captions_and_skips_unsafe_or_known_files()
    {
        var api = new Handler(_ => Json("""
            {"business_discovery":{"media":{"data":[
              {"id":"1","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/1.jpg","caption":"Yeni @acme ceket","timestamp":"2026-11-01T10:00:00+0000"},
              {"id":"2","media_type":"VIDEO","media_product_type":"REELS","media_url":"https://v.fbcdn.net/2.mp4"},
              {"id":"3","media_type":"VIDEO","media_product_type":"REELS","thumbnail_url":"https://a.cdninstagram.com/3.jpg"},
              {"id":"4","media_type":"CAROUSEL_ALBUM","children":{"data":[
                {"id":"41","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/41.jpg"},
                {"id":"42","media_type":"IMAGE","media_url":"https://evil.example/42.jpg"}]}},
              {"id":"../5","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/5.jpg"}
            ]}}}
            """));
        var downloads = new Handler(request => request.RequestUri!.AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal)
            ? File("video/mp4", 10)
            : File("image/jpeg", 10));
        var source = new InstagramApiMediaSource(
            new InstagramGraphClient(new HttpClient(api), new InstagramApiOptions()),
            new HttpClient(downloads),
            Credentials,
            User("brand"),
            _root);

        var files = await ListAsync(source);

        Assert.Equal(["1", "2", "3", "41"], files.Select(f => f.SourceId));
        Assert.Equal("Yeni @acme ceket", files[0].Caption);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), files[0].TakenAt);
        Assert.Equal((MediaKind.Reel, MediaFileType.Video), (files[1].Kind, files[1].Type));
        Assert.Equal((MediaKind.Reel, MediaFileType.Image), (files[2].Kind, files[2].Type));
        Assert.All(files, f => Assert.StartsWith(Path.Combine(_root, "brand"), f.FullPath, StringComparison.Ordinal));
        Assert.DoesNotContain(downloads.Requests, r => r.RequestUri!.Host == "evil.example");

        var before = downloads.Requests.Count;
        await ListAsync(source);
        Assert.Equal(before, downloads.Requests.Count);
    }

    [Fact]
    public async Task Oversized_or_wrong_type_downloads_are_skipped()
    {
        var api = new Handler(_ => Json("""
            {"business_discovery":{"media":{"data":[
              {"id":"1","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/1.jpg"},
              {"id":"2","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/2.jpg"}
            ]}}}
            """));
        var downloads = new Handler(request => request.RequestUri!.AbsolutePath == "/1.jpg"
            ? File("image/jpeg", 10, declaredLength: InstagramApiMediaSource.MaxDownloadBytes + 1)
            : File("text/html", 10));
        var source = new InstagramApiMediaSource(
            new InstagramGraphClient(new HttpClient(api), new InstagramApiOptions()), new HttpClient(downloads), Credentials, User("brand"), _root);

        Assert.Empty(await ListAsync(source));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "brand")));
    }

    private static InstagramUsername User(string name)
    {
        Assert.True(InstagramUsername.TryParse(name, out var user, out _));
        return user;
    }

    private static async Task<List<MediaFile>> ListAsync(InstagramApiMediaSource source)
    {
        var list = new List<MediaFile>();
        await foreach (var file in source.GetMediaAsync(TestContext.Current.CancellationToken))
        {
            list.Add(file);
        }

        return list;
    }

    private static string Page(string[] ids, string? next)
    {
        var data = string.Join(',', ids.Select(id => $$"""{"id":"{{id}}","media_type":"IMAGE","media_url":"https://a.cdninstagram.com/{{id}}.jpg"}"""));
        var paging = next is null ? """{"cursors":{"after":"END"}}""" : $$"""{"cursors":{"after":"{{next}}"},"next":"https://graph.facebook.com/next"}""";
        return """{"business_discovery":{"media":{"data":[""" + data + "],\"paging\":" + paging + "}}}";
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage File(string contentType, int bytes, long? declaredLength = null)
    {
        var content = new ByteArrayContent(new byte[bytes]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        if (declaredLength is not null)
        {
            content.Headers.ContentLength = declaredLength;
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
