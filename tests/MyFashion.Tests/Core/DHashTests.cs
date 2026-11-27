using MyFashion.Core.Media;

namespace MyFashion.Tests.Core;

public sealed class DHashTests
{
    [Fact]
    public void FromGrayPixels_sets_a_bit_when_left_pixel_is_brighter_than_right()
    {
        // 9x8 image where every row is strictly decreasing: every comparison is "left > right".
        var pixels = new byte[72];
        for (var row = 0; row < 8; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                pixels[(row * 9) + col] = (byte)(200 - (col * 10));
            }
        }

        Assert.Equal(ulong.MaxValue, DHash.FromGrayPixels(pixels));
    }

    [Fact]
    public void FromGrayPixels_is_zero_for_a_flat_image()
    {
        Assert.Equal(0UL, DHash.FromGrayPixels(Enumerable.Repeat((byte)128, 72).ToArray()));
    }

    [Fact]
    public void FromGrayPixels_rejects_wrong_size()
    {
        Assert.Throws<ArgumentException>(() => DHash.FromGrayPixels(new byte[71]));
    }

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, 1UL, 1)]
    [InlineData(0b1011UL, 0b0110UL, 3)]
    [InlineData(ulong.MaxValue, 0UL, 64)]
    public void Distance_counts_differing_bits(ulong a, ulong b, int expected)
    {
        Assert.Equal(expected, DHash.Distance(a, b));
    }

    [Fact]
    public void NearDuplicateFilter_keeps_first_and_drops_close_hashes()
    {
        var filter = new NearDuplicateFilter(maxDistance: 6);

        Assert.True(filter.TryAdd(0UL));
        Assert.False(filter.TryAdd(0b111111UL));       // distance 6: duplicate
        Assert.True(filter.TryAdd(0b1111111UL));       // distance 7 from the kept hash: new
        Assert.False(filter.TryAdd(0UL));              // exact duplicate
        Assert.Equal(2, filter.Count);
    }
}
