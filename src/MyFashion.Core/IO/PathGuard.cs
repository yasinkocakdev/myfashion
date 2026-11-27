namespace MyFashion.Core.IO;

/// <summary>Thrown when a path would point outside the directory it must stay in.</summary>
public sealed class UnsafePathException : Exception
{
    public UnsafePathException()
    {
    }

    public UnsafePathException(string message)
        : base(message)
    {
    }

    public UnsafePathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Protects against path traversal: file names coming from outside (downloads, uploads,
/// model output) must never resolve to a location outside the data root.
/// </summary>
public static class PathGuard
{
    /// <summary>Combines <paramref name="root"/> and <paramref name="relativePath"/> and guarantees the result is inside root.</summary>
    /// <exception cref="UnsafePathException">The path is empty, absolute, contains NUL or escapes the root.</exception>
    public static string ResolveWithin(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (relativePath.Length == 0 || relativePath.Contains('\0', StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            throw new UnsafePathException($"Unsafe relative path: '{relativePath.Replace("\0", "\\0", StringComparison.Ordinal)}'.");
        }

        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(root, full))
        {
            throw new UnsafePathException($"Path escapes the data root: '{relativePath}'.");
        }

        return full;
    }

    /// <summary>True if <paramref name="path"/> is strictly inside <paramref name="root"/> (not equal to it).</summary>
    public static bool IsWithin(string root, string path)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var pathFull = Path.GetFullPath(path);
        return pathFull.StartsWith(rootFull, StringComparison.Ordinal) && pathFull.Length > rootFull.Length;
    }
}
