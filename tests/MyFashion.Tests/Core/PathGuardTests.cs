using MyFashion.Core.IO;

namespace MyFashion.Tests.Core;

public sealed class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "myfashion-root");

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("someuser/highlights/2026-01-01_UTC.jpg")]
    [InlineData("hesap/öne çıkanlar/ekran kaydı 1.mov")]
    [InlineData("a/./b/../c.jpg")]
    public void ResolveWithin_returns_full_path_inside_root(string relative)
    {
        var full = PathGuard.ResolveWithin(Root, relative);

        Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, full, StringComparison.Ordinal);
        Assert.True(Path.IsPathFullyQualified(full));
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("a/../../outside.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("a/\0b.jpg")]
    public void ResolveWithin_rejects_paths_that_escape_the_root(string relative)
    {
        Assert.Throws<UnsafePathException>(() => PathGuard.ResolveWithin(Root, relative));
    }

    [Fact]
    public void ResolveWithin_rejects_sibling_directory_with_same_prefix()
    {
        // "myfashion-root-evil" starts with "myfashion-root" but is not inside it.
        Assert.Throws<UnsafePathException>(() => PathGuard.ResolveWithin(Root, "../myfashion-root-evil/x.jpg"));
    }

    [Fact]
    public void IsWithin_reports_containment()
    {
        Assert.True(PathGuard.IsWithin(Root, Path.Combine(Root, "x", "y.jpg")));
        Assert.False(PathGuard.IsWithin(Root, Path.Combine(Root + "-evil", "y.jpg")));
        Assert.False(PathGuard.IsWithin(Root, Root));
    }
}
