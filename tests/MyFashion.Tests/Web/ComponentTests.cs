using Bunit;
using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;
using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;
using MyFashion.Web.Components.Common;
using MyFashion.Web.Endpoints;
using MyFashion.Web.Services;

namespace MyFashion.Tests.Web;

public sealed class ComponentTests : BunitContext
{
    private static AnalyzedImage Image(string? brand = null, string color = "black") => new(
        1,
        MediaKind.Highlight,
        "media/hesap/run-1/0001.jpg",
        null,
        [
            new Garment(GarmentCategory.Top, "hoodie", color, GarmentPattern.Solid, GarmentFit.Oversized, brand, brand is null ? BrandEvidence.None : BrandEvidence.Logo, 0.9),
            new Garment(GarmentCategory.Bottom, "jeans", "denim", GarmentPattern.Solid, GarmentFit.Regular, null, BrandEvidence.None, 0.9),
        ],
        ["streetwear"]);

    [Fact]
    public void ProfileReport_shows_an_empty_state_without_data()
    {
        var cut = Render<ProfileReport>(p => p.Add(x => x.Profile, StyleProfileBuilder.Build("hesap", [])));

        Assert.Contains("henüz kıyafet bulunamadı", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void ProfileReport_shows_turkish_labels_palette_formulas_and_accessible_images()
    {
        IReadOnlyList<AnalyzedImage> images = [Image("Nike"), Image("Nike")];
        var cut = Render<ProfileReport>(p => p
            .Add(x => x.Profile, StyleProfileBuilder.Build("hesap", images))
            .Add(x => x.Images, images));

        Assert.Contains("siyah", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("sokak stili", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Nike", cut.Find("table tbody td").TextContent);
        Assert.Contains("siyah üst + denim alt", cut.Markup, StringComparison.Ordinal);
        var img = cut.FindAll("img")[0];
        Assert.Equal("/media/hesap/run-1/0001.jpg", img.GetAttribute("src"));
        Assert.StartsWith("Kıyafetler: siyah hoodie", img.GetAttribute("alt"), StringComparison.Ordinal);
        Assert.NotNull(cut.Find(".palette[role=img]").GetAttribute("aria-label"));
    }

    [Fact]
    public void ProfileReport_escapes_untrusted_text()
    {
        const string evil = "<script>alert(1)</script>";
        IReadOnlyList<AnalyzedImage> images = [Image(evil)];
        var cut = Render<ProfileReport>(p => p
            .Add(x => x.Profile, StyleProfileBuilder.Build("hesap", images))
            .Add(x => x.Images, images));

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;script&gt;", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendationsView_opens_store_links_safely_in_a_new_tab()
    {
        var recommendations = new Recommendations(
            [new ShoppingItem("Siyah hoodie", "İmza parça.", 1, "siyah hoodie", StoreCatalog.BuildLinks("siyah hoodie", [StoreCatalog.Zara]))],
            [new OutfitSuggestion("Hafta sonu", ["siyah hoodie", "denim jean", "beyaz sneaker"], "Günlük", "Rahat.")]);

        var cut = Render<RecommendationsView>(p => p.Add(x => x.Value, recommendations));

        var link = cut.Find("a[target=_blank]");
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.StartsWith("https://www.zara.com/", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains("Önce bunu al", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("siyah hoodie · denim jean · beyaz sneaker", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendationsView_has_an_empty_state()
    {
        var cut = Render<RecommendationsView>(p => p.Add(x => x.Value, Recommendations.Empty));

        Assert.Contains("Öneri çıkmadı", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void JobProgressView_announces_failures_and_counts()
    {
        var job = new AnalysisJob(Guid.NewGuid(), "hesap", "/klasör", false, JobStatus.Failed, DateTimeOffset.UtcNow,
            new RunProgress(1, "Analiz ediliyor", new RunCounts(2, 3, 1, 1, 0, 1, 0), null), 1, "Ollama çalışmıyor");

        var cut = Render<JobProgressView>(p => p.Add(x => x.Job, job));

        Assert.Equal("Ollama çalışmıyor", cut.Find("[role=alert]").TextContent);
        Assert.Contains("Başarısız", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".progress"));
    }

    [Fact]
    public void JobProgressView_explains_a_run_that_found_no_files()
    {
        var job = new AnalysisJob(Guid.NewGuid(), "hesap", "/bos", false, JobStatus.Completed, DateTimeOffset.UtcNow,
            new RunProgress(1, "Tamamlandı", new RunCounts(0, 0, 0, 0, 0, 0, 0), null), 1);

        var cut = Render<JobProgressView>(p => p.Add(x => x.Job, job));

        Assert.Contains("Hiç dosya işlenmedi", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void JobProgressView_shows_live_status_while_running()
    {
        var job = new AnalysisJob(Guid.NewGuid(), "hesap", "/klasör", true, JobStatus.Running, DateTimeOffset.UtcNow,
            new RunProgress(1, "Analiz ediliyor", new RunCounts(1, 1, 0, 0, 0, 0, 0), "a.jpg"));

        var cut = Render<JobProgressView>(p => p.Add(x => x.Job, job));

        Assert.Contains("Analiz ediliyor", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find(".progress"));
        Assert.Contains("Instagram API", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("black", "siyah")]
    [InlineData("light blue top", "açık mavi üst")]
    [InlineData("black hoodie", "siyah hoodie")]
    [InlineData("old money", "old money")]
    [InlineData("Zara", "Zara")]
    public void Labels_translate_known_words_and_keep_the_rest(string input, string expected)
    {
        Assert.Equal(expected, Labels.Translate(input));
    }

    [Fact]
    public void Media_urls_encode_each_path_segment()
    {
        Assert.Equal("/media/%C3%B6ne%20%C3%A7%C4%B1kan/run-1/0001.jpg", MediaEndpoints.UrlFor("media/öne çıkan/run-1/0001.jpg"));
    }
}
