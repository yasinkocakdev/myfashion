using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MyFashion.Core.Instagram;

/// <summary>
/// A validated Instagram username. The only way to create one is <see cref="TryParse"/>,
/// so any instance is safe to pass to external tools and APIs.
/// </summary>
public sealed partial record InstagramUsername
{
    private const int MaxLength = 30;

    // Paths on instagram.com that are not profiles.
    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "reel", "reels", "tv", "stories", "explore", "accounts", "direct", "about", "developer", "legal",
    };

    private static readonly HashSet<string> InstagramHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "instagram.com", "www.instagram.com", "m.instagram.com",
    };

    private InstagramUsername(string value) => Value = value;

    /// <summary>The normalized (lower-case) username, without "@".</summary>
    public string Value { get; }

    /// <summary>The canonical profile URL.</summary>
    public Uri ProfileUrl => new($"https://www.instagram.com/{Value}/");

    public override string ToString() => Value;

    /// <summary>
    /// Parses a profile link (with or without scheme, "www." and query string), "@name" or a bare username.
    /// </summary>
    public static bool TryParse(
        string? input,
        [NotNullWhen(true)] out InstagramUsername? username,
        [NotNullWhen(false)] out string? error)
    {
        username = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "Bir Instagram profil linki ya da kullanıcı adı gir.";
            return false;
        }

        string candidate;
        if (text.Contains('/', StringComparison.Ordinal) || text.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryExtractFromLink(text, out candidate, out error))
            {
                return false;
            }
        }
        else
        {
            candidate = text.StartsWith('@') ? text[1..] : text;
        }

        if (!IsValidUsername(candidate))
        {
            error = "Geçersiz kullanıcı adı. Instagram kullanıcı adları en fazla 30 karakterdir ve yalnızca harf, rakam, nokta ve alt çizgi içerir.";
            return false;
        }

        username = new InstagramUsername(candidate.ToLowerInvariant());
        error = null;
        return true;
    }

    private static bool TryExtractFromLink(string text, out string candidate, [NotNullWhen(false)] out string? error)
    {
        candidate = string.Empty;
        var withScheme = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;

        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !InstagramHosts.Contains(uri.Host))
        {
            error = "Bu bir Instagram profil linki değil.";
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || ReservedPaths.Contains(segments[0]))
        {
            error = "Link bir profile değil, bir gönderiye ya da sayfaya ait. Profil linkini gir (instagram.com/kullaniciadi).";
            return false;
        }

        candidate = Uri.UnescapeDataString(segments[0]);
        error = null;
        return true;
    }

    private static bool IsValidUsername(string candidate) =>
        candidate.Length is > 0 and <= MaxLength
        && UsernamePattern().IsMatch(candidate)
        && !candidate.StartsWith('.')
        && !candidate.EndsWith('.')
        && !candidate.Contains("..", StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9._]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
