using MyFashion.Core.Analysis;

namespace MyFashion.Tests.Core;

public sealed class StyleColorsTests
{
    [Theory]
    [InlineData("black", "black")]
    [InlineData("Black", "black")]
    [InlineData(" jet black ", "black")]
    [InlineData("siyah", "black")]
    [InlineData("off-white", "white")]
    [InlineData("ivory", "cream")]
    [InlineData("charcoal", "grey")]
    [InlineData("gray", "grey")]
    [InlineData("navy blue", "navy")]
    [InlineData("lacivert", "navy")]
    [InlineData("camel", "beige")]
    [InlineData("tan", "beige")]
    [InlineData("bordo", "burgundy")]
    [InlineData("olive green", "olive")]
    [InlineData("light blue", "light blue")]
    [InlineData("denim", "denim")]
    public void Normalize_maps_synonyms_to_canonical_names(string input, string expected)
    {
        Assert.Equal(expected, StyleColors.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rainbow-ish thing")]
    [InlineData("<script>alert(1)</script>")]
    public void Normalize_returns_other_for_unknown_or_empty(string? input)
    {
        Assert.Equal(StyleColors.Other, StyleColors.Normalize(input));
    }

    [Fact]
    public void Every_canonical_color_has_a_hex_value()
    {
        foreach (var name in StyleColors.All)
        {
            var hex = StyleColors.Hex(name);
            Assert.Matches("^#[0-9A-F]{6}$", hex);
        }
    }
}
