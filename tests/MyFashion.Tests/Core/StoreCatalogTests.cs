using MyFashion.Core.Recommendations;

namespace MyFashion.Tests.Core;

public sealed class StoreCatalogTests
{
    [Fact]
    public void Builds_encoded_links_only_for_known_stores()
    {
        var links = StoreCatalog.BuildLinks("siyah oversize kapüşonlu", [StoreCatalog.Zara, "evil", StoreCatalog.GoogleShopping, StoreCatalog.Zara]);

        Assert.Equal(2, links.Count);
        Assert.Equal("Zara", links[0].Store);
        Assert.Equal("https://www.zara.com/tr/tr/search?searchTerm=siyah%20oversize%20kap%C3%BC%C5%9Fonlu", links[0].Url.AbsoluteUri);
        Assert.Equal("www.google.com", links[1].Url.Host);
    }

    [Theory]
    [InlineData("siyah ceket", "siyah ceket")]
    [InlineData("  çok   boşluk  ", "çok boşluk")]
    [InlineData("ceket&q=evil#frag", "ceket q evil frag")]
    [InlineData("<script>alert(1)</script>", "script alert 1 script")]
    [InlineData("https://evil.com/x", "https evil com x")]
    [InlineData("slim-fit pantolon", "slim-fit pantolon")]
    public void SanitizeQuery_keeps_only_safe_characters(string input, string expected)
    {
        Assert.Equal(expected, StoreCatalog.SanitizeQuery(input));
    }

    [Fact]
    public void Long_and_empty_queries_are_handled()
    {
        Assert.True(StoreCatalog.SanitizeQuery(new string('a', 500)).Length <= 80);
        Assert.Empty(StoreCatalog.BuildLinks("   ", [StoreCatalog.Zara]));
        Assert.Empty(StoreCatalog.BuildLinks("!!!", [StoreCatalog.Zara]));
    }

    [Fact]
    public void Every_store_link_is_https()
    {
        var links = StoreCatalog.BuildLinks("ceket", StoreCatalog.All.Select(s => s.Id));

        Assert.Equal(StoreCatalog.All.Count, links.Count);
        Assert.All(links, l => Assert.Equal(Uri.UriSchemeHttps, l.Url.Scheme));
    }
}
