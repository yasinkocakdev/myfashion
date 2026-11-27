using MyFashion.Core.Instagram;

namespace MyFashion.Tests.Core;

public sealed class InstagramUsernameTests
{
    [Theory]
    [InlineData("https://www.instagram.com/some.user/", "some.user")]
    [InlineData("https://instagram.com/some_user", "some_user")]
    [InlineData("http://www.instagram.com/Some.User/?igsh=abc123&utm_source=qr", "some.user")]
    [InlineData("instagram.com/someuser/reels/", "someuser")]
    [InlineData("www.instagram.com/someuser/tagged", "someuser")]
    [InlineData("https://m.instagram.com/someuser", "someuser")]
    [InlineData("@someuser", "someuser")]
    [InlineData("  someuser  ", "someuser")]
    [InlineData("user123", "user123")]
    public void TryParse_accepts_profile_links_and_usernames(string input, string expected)
    {
        var ok = InstagramUsername.TryParse(input, out var username, out var error);

        Assert.True(ok, error);
        Assert.Equal(expected, username!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.com/instagram.com/someuser")]
    [InlineData("https://instagram.com.evil.com/someuser")]
    [InlineData("https://www.instagram.com/")]
    [InlineData("https://www.instagram.com/p/C1abc/")]
    [InlineData("https://www.instagram.com/reel/C1abc/")]
    [InlineData("https://www.instagram.com/explore/")]
    [InlineData("https://www.instagram.com/stories/someuser/123")]
    [InlineData("some user")]
    [InlineData("someuser; rm -rf ~")]
    [InlineData("$(whoami)")]
    [InlineData("--login=attacker")]
    [InlineData("../etc/passwd")]
    [InlineData(".someuser")]
    [InlineData("someuser.")]
    [InlineData("some..user")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    [InlineData("kullanıcı")]
    [InlineData("ftp://instagram.com/someuser")]
    public void TryParse_rejects_invalid_or_dangerous_input(string? input)
    {
        var ok = InstagramUsername.TryParse(input, out var username, out var error);

        Assert.False(ok);
        Assert.Null(username);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Usernames_compare_case_insensitively_by_normalizing()
    {
        Assert.True(InstagramUsername.TryParse("SomeUser", out var a, out _));
        Assert.True(InstagramUsername.TryParse("https://instagram.com/someuser", out var b, out _));

        Assert.Equal(a, b);
        Assert.Equal("someuser", a!.ToString());
        Assert.Equal(new Uri("https://www.instagram.com/someuser/"), a.ProfileUrl);
    }
}
