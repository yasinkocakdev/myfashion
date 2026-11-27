using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MyFashion.Infrastructure.Instagram;

namespace MyFashion.Web.Services;

/// <summary>
/// Keeps the Instagram access token encrypted on disk (ASP.NET Core Data Protection) in a file only the
/// current user can read. The token is never sent back to the browser or written to logs.
/// </summary>
public sealed class InstagramCredentialStore(IDataProtectionProvider protection, AppPaths paths)
{
    private readonly IDataProtector _protector = protection.CreateProtector("MyFashion.InstagramCredentials.v1");

    private string FilePath => Path.Combine(paths.DataRoot, "secrets", "instagram.dat");

    public async Task<InstagramCredentials?> GetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false));
            return JsonSerializer.Deserialize<InstagramCredentials>(json);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys changed or the file was tampered with: treat as not connected.
            return null;
        }
    }

    /// <summary>The account id only (safe to show), or null when not connected.</summary>
    public async Task<string?> GetConnectedUserIdAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken).ConfigureAwait(false))?.IgUserId;

    public async Task SaveAsync(InstagramCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var protectedJson = _protector.Protect(JsonSerializer.Serialize(credentials));
        await File.WriteAllTextAsync(FilePath, protectedJson, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
