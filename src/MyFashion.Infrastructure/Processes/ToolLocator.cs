namespace MyFashion.Infrastructure.Processes;

/// <summary>
/// Finds an executable. Apps started from Finder do not inherit the shell's PATH, so the usual
/// Homebrew locations are checked explicitly before PATH.
/// </summary>
public static class ToolLocator
{
    private static readonly string[] WellKnownDirectories = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"];

    /// <summary>Returns the configured path if it exists, otherwise the first match in well-known folders or PATH, otherwise null.</summary>
    public static string? Find(string toolName, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return WellKnownDirectories
            .Concat(pathDirectories)
            .Select(dir => Path.Combine(dir, toolName))
            .FirstOrDefault(File.Exists);
    }
}
