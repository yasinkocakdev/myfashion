using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyFashion.Core.Instagram;

namespace MyFashion.Infrastructure.Instagram;

/// <summary>Settings for the official Instagram Graph API.</summary>
public sealed record InstagramApiOptions
{
    public Uri BaseUrl { get; init; } = new("https://graph.facebook.com/");

    public string ApiVersion { get; init; } = "v27.0";

    /// <summary>Upper limit of media fetched per analysis.</summary>
    public int MaxMedia { get; init; } = 200;

    public int PageSize { get; init; } = 25;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>The app user's own credentials: their professional account id and a user access token. Secret.</summary>
public sealed record InstagramCredentials(string IgUserId, string AccessToken)
{
    // Never print the token (records include all properties in ToString by default).
    public override string ToString() => $"InstagramCredentials {{ IgUserId = {IgUserId}, AccessToken = *** }}";
}

/// <summary>One post or reel of the target account, as returned by Business Discovery.</summary>
public sealed record InstagramMedia(
    string Id,
    string MediaType,
    string? MediaProductType,
    Uri? MediaUrl,
    Uri? ThumbnailUrl,
    string? Caption,
    DateTimeOffset? Timestamp,
    IReadOnlyList<InstagramMedia> Children);

public enum InstagramErrorKind
{
    Unknown,
    NotBusinessAccount,
    InvalidToken,
    RateLimited,
    PermissionDenied,
}

/// <summary>A Graph API error translated into a message the user can act on.</summary>
public sealed class InstagramApiException : Exception
{
    public InstagramApiException()
    {
    }

    public InstagramApiException(string message)
        : base(message)
    {
    }

    public InstagramApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InstagramApiException(InstagramErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException) => Kind = kind;

    public InstagramErrorKind Kind { get; }
}

/// <summary>
/// Reads another professional account's posts and reels through the official Business Discovery API.
/// The token travels in the Authorization header, never in the URL, so it cannot end up in logs or history.
/// </summary>
public sealed class InstagramGraphClient(HttpClient http, InstagramApiOptions options)
{
    private const string MediaFields = "id,media_type,media_product_type,media_url,thumbnail_url,caption,timestamp,children{id,media_type,media_url,thumbnail_url}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Fetches the account's media, newest first, following pagination up to <see cref="InstagramApiOptions.MaxMedia"/>.</summary>
    public async Task<IReadOnlyList<InstagramMedia>> GetMediaAsync(InstagramCredentials credentials, InstagramUsername target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(target);
        if (!IsNumericId(credentials.IgUserId))
        {
            throw new InstagramApiException(InstagramErrorKind.InvalidToken, "Instagram kullanıcı kimliği geçersiz. Ayarlar'dan kontrol et.");
        }

        var result = new List<InstagramMedia>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        do
        {
            var page = await GetPageAsync(credentials, target, after, cancellationToken).ConfigureAwait(false);
            var media = page.BusinessDiscovery?.Media;
            foreach (var item in media?.Data ?? [])
            {
                result.Add(Map(item));
                if (result.Count >= options.MaxMedia)
                {
                    return result;
                }
            }

            after = media?.Paging?.Next is not null ? media.Paging.Cursors?.After : null;
        }
        while (after is not null && seenCursors.Add(after));

        return result;
    }

    /// <summary>Checks the credentials by reading the app user's own account.</summary>
    public async Task<string> TestAsync(InstagramCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (!IsNumericId(credentials.IgUserId))
        {
            throw new InstagramApiException(InstagramErrorKind.InvalidToken, "Instagram kullanıcı kimliği yalnızca rakamlardan oluşmalı.");
        }

        var reply = await SendAsync<SelfReply>(credentials, $"{credentials.IgUserId}?fields=username", cancellationToken).ConfigureAwait(false);
        return reply.Username ?? credentials.IgUserId;
    }

    private Task<DiscoveryReply> GetPageAsync(InstagramCredentials credentials, InstagramUsername target, string? after, CancellationToken cancellationToken)
    {
        var paging = after is null
            ? string.Create(CultureInfo.InvariantCulture, $".limit({options.PageSize})")
            : string.Create(CultureInfo.InvariantCulture, $".limit({options.PageSize}).after({after})");
        var fields = $"business_discovery.username({target.Value}){{username,media_count,media{paging}{{{MediaFields}}}}}";
        return SendAsync<DiscoveryReply>(credentials, $"{credentials.IgUserId}?fields={Uri.EscapeDataString(fields)}", cancellationToken);
    }

    private async Task<T> SendAsync<T>(InstagramCredentials credentials, string pathAndQuery, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.BaseUrl, $"{options.ApiVersion}/{pathAndQuery}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

        using var timeout = new CancellationTokenSource(options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InstagramApiException(InstagramErrorKind.Unknown, "Instagram API zamanında yanıt vermedi.");
        }
        catch (HttpRequestException ex)
        {
            throw new InstagramApiException(InstagramErrorKind.Unknown, "Instagram API'ye bağlanılamadı. İnternet bağlantını kontrol et.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Translate(body, (int)response.StatusCode);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, Json)
                    ?? throw new InstagramApiException(InstagramErrorKind.Unknown, "Instagram API boş yanıt döndürdü.");
            }
            catch (JsonException ex)
            {
                throw new InstagramApiException(InstagramErrorKind.Unknown, "Instagram API beklenmeyen bir yanıt döndürdü.", ex);
            }
        }
    }

    /// <summary>Maps Graph API error codes to actionable messages. The raw message is not shown (it may echo request data).</summary>
    internal static InstagramApiException Translate(string body, int status)
    {
        GraphError? error = null;
        try
        {
            error = JsonSerializer.Deserialize<ErrorReply>(body, Json)?.Error;
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the generic message.
        }

        return error?.Code switch
        {
            190 or 102 => new(InstagramErrorKind.InvalidToken, "Instagram erişim anahtarı geçersiz ya da süresi dolmuş. Ayarlar'dan yenisini gir."),
            4 or 17 or 32 or 613 or 80002 => new(InstagramErrorKind.RateLimited, "Instagram API kullanım sınırına ulaşıldı. Bir süre bekleyip tekrar dene; o ana kadar alınanlar analiz edilir."),
            10 or 200 or 299 => new(InstagramErrorKind.PermissionDenied, "Meta uygulamasının bu işlem için izni yok. Kurulum rehberindeki izinleri kontrol et."),
            100 or 110 => new(InstagramErrorKind.NotBusinessAccount, "Bu hesap bulunamadı ya da İşletme/İçerik Üreticisi hesabı değil. Kişisel hesaplar API ile okunamaz; ekran kaydı yolunu kullan."),
            _ => new(InstagramErrorKind.Unknown, $"Instagram API hata döndürdü (HTTP {status}{(error?.Code is int c ? $", kod {c}" : string.Empty)})."),
        };
    }

    private static InstagramMedia Map(MediaItem item) => new(
        item.Id ?? string.Empty,
        item.MediaType ?? "IMAGE",
        item.MediaProductType,
        HttpsOrNull(item.MediaUrl),
        HttpsOrNull(item.ThumbnailUrl),
        item.Caption,
        DateTimeOffset.TryParse(item.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null,
        [.. (item.Children?.Data ?? []).Select(Map)]);

    private static Uri? HttpsOrNull(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    private static bool IsNumericId(string id) => id.Length is > 0 and <= 40 && id.All(char.IsAsciiDigit);

    private sealed record DiscoveryReply(Discovery? BusinessDiscovery);

    private sealed record Discovery(string? Username, int? MediaCount, MediaPage? Media);

    private sealed record MediaPage(IReadOnlyList<MediaItem>? Data, Paging? Paging);

    private sealed record Paging(Cursors? Cursors, string? Next);

    private sealed record Cursors(string? Before, string? After);

    private sealed record MediaItem(
        string? Id,
        string? MediaType,
        string? MediaProductType,
        string? MediaUrl,
        string? ThumbnailUrl,
        string? Caption,
        string? Timestamp,
        ChildPage? Children);

    private sealed record ChildPage(IReadOnlyList<MediaItem>? Data);

    private sealed record SelfReply(string? Username);

    private sealed record ErrorReply(GraphError? Error);

    private sealed record GraphError(string? Message, int? Code, [property: JsonPropertyName("error_subcode")] int? Subcode);
}
