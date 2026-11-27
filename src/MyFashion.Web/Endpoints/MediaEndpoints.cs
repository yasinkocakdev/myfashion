using MyFashion.Core.IO;
using MyFashion.Web.Services;

namespace MyFashion.Web.Endpoints;

/// <summary>Serves prepared JPEGs from the private media folder, and nothing else.</summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/{**path}", (string path, AppPaths paths) =>
        {
            if (!path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }

            string full;
            try
            {
                full = PathGuard.ResolveWithin(paths.MediaRoot, path);
            }
            catch (UnsafePathException)
            {
                return Results.NotFound();
            }

            return File.Exists(full) ? Results.File(full, "image/jpeg", enableRangeProcessing: false) : Results.NotFound();
        });

        return app;
    }

    /// <summary>The URL for a prepared image path stored relative to the data root ("media/...").</summary>
    public static string UrlFor(string preparedPath)
    {
        ArgumentNullException.ThrowIfNull(preparedPath);
        var segments = preparedPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
    }
}
