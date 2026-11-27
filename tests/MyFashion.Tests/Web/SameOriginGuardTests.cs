using Microsoft.AspNetCore.Http;
using MyFashion.Web.Security;

namespace MyFashion.Tests.Web;

public sealed class SameOriginGuardTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("HTTP://127.0.0.1:5080", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("http://127.0.0.1:6000", false)]
    [InlineData("http://localhost:5080", false)]
    [InlineData("null", false)]
    [InlineData("not a url", false)]
    public void Only_same_origin_or_missing_origin_is_allowed(string? origin, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("127.0.0.1", 5080);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        Assert.Equal(allowed, SameOriginGuard.IsAllowed(context.Request));
    }
}
