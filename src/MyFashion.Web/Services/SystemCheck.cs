using System.Net.Http.Json;
using MyFashion.Infrastructure.Media;
using MyFashion.Infrastructure.Ollama;
using MyFashion.Infrastructure.Processes;

namespace MyFashion.Web.Services;

/// <summary>Result of checking the tools the app depends on, with a fix for each problem.</summary>
public sealed record SystemStatus(bool FfmpegOk, bool OllamaOk, bool ModelOk, string Model, IReadOnlyList<string> Fixes)
{
    public bool AllOk => FfmpegOk && OllamaOk && ModelOk;
}

/// <summary>Checks ffmpeg, the Ollama server and the vision model, so the home page can say what is missing.</summary>
public sealed class SystemCheck(IHttpClientFactory httpFactory, OllamaOptions ollama, MediaToolkitOptions media)
{
    public async Task<SystemStatus> CheckAsync(CancellationToken cancellationToken)
    {
        var fixes = new List<string>();
        var ffmpeg = ToolLocator.Find("ffmpeg", media.FfmpegPath) is not null;
        if (!ffmpeg)
        {
            fixes.Add("ffmpeg kurulu değil: brew install ffmpeg");
        }

        var (ollamaOk, modelOk) = await CheckOllamaAsync(cancellationToken).ConfigureAwait(false);
        if (!ollamaOk)
        {
            fixes.Add("Ollama çalışmıyor: brew install ollama && brew services start ollama");
        }
        else if (!modelOk)
        {
            fixes.Add($"Model kurulu değil: ollama pull {ollama.VisionModel}");
        }

        return new SystemStatus(ffmpeg, ollamaOk, modelOk, ollama.VisionModel, fixes);
    }

    private async Task<(bool Ollama, bool Model)> CheckOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpFactory.CreateClient(nameof(SystemCheck));
            http.Timeout = TimeSpan.FromSeconds(3);
            var tags = await http.GetFromJsonAsync<TagsReply>(new Uri(ollama.BaseUrl, "/api/tags"), cancellationToken).ConfigureAwait(false);
            var model = tags?.Models?.Any(m => string.Equals(m.Name, ollama.VisionModel, StringComparison.OrdinalIgnoreCase)) ?? false;
            return (true, model);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (false, false);
        }
    }

    private sealed record TagsReply(IReadOnlyList<TagModel>? Models);

    private sealed record TagModel(string Name);
}
