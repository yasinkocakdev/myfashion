using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyFashion.Infrastructure.Ollama;

/// <summary>Thrown when Ollama cannot answer. <see cref="IsTransient"/> tells whether a retry may help.</summary>
public sealed class OllamaException : Exception
{
    public OllamaException()
    {
    }

    public OllamaException(string message)
        : base(message)
    {
    }

    public OllamaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public OllamaException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException) => IsTransient = isTransient;

    public bool IsTransient { get; }
}

public sealed record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("images"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Images = null);

/// <summary>Minimal client for Ollama's <c>/api/chat</c> with structured (JSON schema) output. No SDK needed.</summary>
public sealed class OllamaChatClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public OllamaChatClient(HttpClient http, OllamaOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.BaseUrl.IsLoopback)
        {
            throw new ArgumentException("Ollama yalnızca bu bilgisayarda (localhost) çalışmalı; fotoğraflar dışarı gönderilmez.", nameof(options));
        }

        _http = http;
        _options = options;
    }

    /// <summary>Sends one non-streaming chat request and returns the assistant's message content.</summary>
    /// <param name="model">Model name.</param>
    /// <param name="messages">Chat messages.</param>
    /// <param name="format">JSON schema the answer must follow.</param>
    /// <param name="maxTokens">Upper limit of generated tokens (Ollama <c>num_predict</c>).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task<string> ChatAsync(string model, IReadOnlyList<OllamaMessage> messages, JsonNode format, int maxTokens, CancellationToken cancellationToken)
    {
        var body = new
        {
            model,
            messages,
            format,
            stream = false,
            think = false,
            keep_alive = _options.KeepAlive,
            options = new { temperature = 0, num_predict = maxTokens },
        };

        using var timeout = new CancellationTokenSource(_options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(new Uri(_options.BaseUrl, "/api/chat"), body, Json, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaException($"Ollama {_options.RequestTimeout.TotalSeconds:0} saniyede yanıt vermedi.", isTransient: true);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException || ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            throw new OllamaException("Ollama çalışmıyor. Başlatmak için: brew services start ollama", isTransient: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaException("Ollama'ya bağlanırken hata oluştu.", isTransient: true, ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new OllamaException($"Model kurulu değil. Kurmak için: ollama pull {model}", isTransient: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
                throw new OllamaException($"Ollama hata döndürdü ({(int)response.StatusCode}).", transient);
            }

            var reply = await response.Content.ReadFromJsonAsync<ChatReply>(Json, linked.Token).ConfigureAwait(false);
            return reply?.Message?.Content
                ?? throw new OllamaException("Ollama boş yanıt döndürdü.", isTransient: true);
        }
    }

    private sealed record ChatReply(ChatReplyMessage? Message);

    private sealed record ChatReplyMessage(string? Content);
}
