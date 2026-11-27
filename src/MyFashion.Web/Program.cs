using Microsoft.EntityFrameworkCore;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;
using MyFashion.Core.Recommendations;
using MyFashion.Infrastructure.Data;
using MyFashion.Infrastructure.Media;
using MyFashion.Infrastructure.Ollama;
using MyFashion.Infrastructure.Processes;
using MyFashion.Web.Components;
using MyFashion.Web.Endpoints;
using MyFashion.Web.Security;
using MyFashion.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Paths and database
var paths = AppPaths.Create(builder.Configuration);
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<MyFashionDbContext>(options => options.UseSqlite($"Data Source={paths.DatabasePath}"));
builder.Services.AddSingleton<IAnalysisStore, EfAnalysisStore>();
builder.Services.AddSingleton<IAnalysisReader, EfAnalysisReader>();
builder.Services.AddSingleton<IPreferencesStore, EfPreferencesStore>();

// Media tools (ffmpeg)
var mediaOptions = builder.Configuration.GetSection("Media").Get<MediaToolkitOptions>() ?? new MediaToolkitOptions();
builder.Services.AddSingleton(mediaOptions);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IMediaToolkit, FfmpegMediaToolkit>();

// Local AI (Ollama)
var ollamaOptions = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
builder.Services.AddSingleton(ollamaOptions);
builder.Services.AddHttpClient<OllamaChatClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddTransient<IVisionAnalyzer, OllamaVisionAnalyzer>();
builder.Services.AddTransient<IRecommendationGenerator, OllamaRecommendationGenerator>();

// Application services
builder.Services.AddSingleton(new AnalysisRunnerOptions(paths.DataRoot));
builder.Services.AddTransient<AnalysisRunner>();
builder.Services.AddSingleton<AnalysisJobService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AnalysisJobService>());
builder.Services.AddSingleton<RecommendationService>();
builder.Services.AddSingleton<SystemCheck>();
builder.Services.AddHttpClient(nameof(SystemCheck));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MyFashionDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseSecurityHeaders();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapMediaEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can start the app.</summary>
public partial class Program
{
}
