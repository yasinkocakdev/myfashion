using System.Numerics;

namespace MyFashion.Core.Media;

/// <summary>
/// Difference hash (dHash): a 64-bit fingerprint of an image's brightness gradients.
/// Near-identical images (re-encoded, slightly resized, consecutive video frames) get hashes
/// that differ in only a few bits, so duplicates can be found without comparing pixels.
/// </summary>
public static class DHash
{
    public const int Width = 9;
    public const int Height = 8;

    /// <summary>Builds the hash from a 9x8 grayscale image (row-major, 72 bytes).</summary>
    public static ulong FromGrayPixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != Width * Height)
        {
            throw new ArgumentException($"Expected {Width * Height} grayscale pixels, got {pixels.Length}.", nameof(pixels));
        }

        ulong hash = 0;
        var bit = 0;
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width - 1; col++, bit++)
            {
                var index = (row * Width) + col;
                if (pixels[index] > pixels[index + 1])
                {
                    hash |= 1UL << bit;
                }
            }
        }

        return hash;
    }

    /// <summary>Number of differing bits (Hamming distance), 0..64.</summary>
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
}

/// <summary>Keeps only images whose hash is farther than <c>maxDistance</c> bits from every image kept so far.</summary>
public sealed class NearDuplicateFilter(int maxDistance = 6)
{
    private readonly List<ulong> _kept = [];

    public int Count => _kept.Count;

    /// <summary>Returns true and remembers the hash if it is new; false if it is a near-duplicate.</summary>
    public bool TryAdd(ulong hash)
    {
        foreach (var kept in _kept)
        {
            if (DHash.Distance(kept, hash) <= maxDistance)
            {
                return false;
            }
        }

        _kept.Add(hash);
        return true;
    }
}
