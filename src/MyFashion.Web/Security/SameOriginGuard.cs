namespace MyFashion.Web.Security;

/// <summary>
/// Rejects requests that a browser marks as coming from another site. Blazor Server talks over a SignalR
/// WebSocket, and WebSockets are not covered by CORS, so without this check a malicious page open in the
/// same browser could try to connect to http://127.0.0.1:5080 (cross-site WebSocket hijacking / CSRF).
/// Requests without an Origin header (direct navigation, curl) are unaffected.
/// </summary>
public static class SameOriginGuard
{
    public static IApplicationBuilder UseSameOriginGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!IsAllowed(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

    /// <summary>True when there is no Origin header or it matches the request's own scheme and host.</summary>
    public static bool IsAllowed(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
