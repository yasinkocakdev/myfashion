using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MyFashion.Infrastructure.Instagram;
using MyFashion.Web.Services;

namespace MyFashion.Tests.Web;

public sealed class InstagramCredentialStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("myfashion-secrets-").FullName;
    private readonly InstagramCredentialStore _store;

    public InstagramCredentialStoreTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MyFashion:DataRoot"] = _root }).Build();
        _store = new InstagramCredentialStore(new EphemeralDataProtectionProvider(), AppPaths.Create(config));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Saves_encrypted_with_owner_only_permissions_and_reads_back()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(new InstagramCredentials("17841400000000000", "very-secret-token"), ct);

        var file = Path.Combine(_root, "secrets", "instagram.dat");
        Assert.DoesNotContain("very-secret-token", await File.ReadAllTextAsync(file, ct), StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal("very-secret-token", (await _store.GetAsync(ct))!.AccessToken);
        Assert.Equal("17841400000000000", await _store.GetConnectedUserIdAsync(ct));
    }

    [Fact]
    public async Task Clear_disconnects_and_a_tampered_file_counts_as_not_connected()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(new InstagramCredentials("1", "t"), ct);
        await File.WriteAllTextAsync(Path.Combine(_root, "secrets", "instagram.dat"), "tampered", ct);

        Assert.Null(await _store.GetAsync(ct));

        _store.Clear();
        Assert.Null(await _store.GetConnectedUserIdAsync(ct));
    }
}
