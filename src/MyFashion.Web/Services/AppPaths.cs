namespace MyFashion.Web.Services;

/// <summary>Where the app keeps its private data: outside the repository, readable only by the current user.</summary>
public sealed class AppPaths
{
    private AppPaths(string dataRoot) => DataRoot = dataRoot;

    public string DataRoot { get; }

    public string MediaRoot => Path.Combine(DataRoot, "media");

    public string DatabasePath => Path.Combine(DataRoot, "myfashion.db");

    /// <summary>Uses <c>MyFashion:DataRoot</c> if configured, otherwise ~/Library/Application Support/MyFashion on macOS.</summary>
    public static AppPaths Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration["MyFashion:DataRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(DefaultBase(), "MyFashion")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));

        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        {
            // Photos of other people live here: only the current user may read them.
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new AppPaths(root);
    }

    private static string DefaultBase() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
