namespace MyFashion.Core.Analysis;

public enum GarmentCategory
{
    Other,
    Top,
    Outerwear,
    Bottom,
    Dress,
    Footwear,
    Bag,
    Accessory,
}

public enum GarmentPattern
{
    Other,
    Solid,
    Striped,
    Checked,
    Floral,
    Graphic,
    Logo,
}

public enum GarmentFit
{
    Unknown,
    Slim,
    Regular,
    Oversized,
}

/// <summary>Why the model believes a brand is present. A brand without evidence is discarded.</summary>
public enum BrandEvidence
{
    None,
    Logo,
    Text,
    Caption,
}

/// <summary>One clothing item seen in an image.</summary>
/// <param name="Category">Broad category.</param>
/// <param name="Type">Short free-text type, for example "oversized hoodie".</param>
/// <param name="Color">Canonical color name from <see cref="StyleColors"/>.</param>
/// <param name="Pattern">Pattern.</param>
/// <param name="Fit">Fit/silhouette.</param>
/// <param name="Brand">Brand name only when there is visible or caption evidence; otherwise null.</param>
/// <param name="BrandEvidence">Evidence for <paramref name="Brand"/>.</param>
/// <param name="Confidence">Model confidence, 0..1.</param>
public sealed record Garment(
    GarmentCategory Category,
    string Type,
    string Color,
    GarmentPattern Pattern,
    GarmentFit Fit,
    string? Brand,
    BrandEvidence BrandEvidence,
    double Confidence);

/// <summary>Everything the model found in one image.</summary>
public sealed record ImageAnalysis(bool HasOutfit, IReadOnlyList<Garment> Garments, IReadOnlyList<string> StyleTags)
{
    public static ImageAnalysis Empty { get; } = new(false, [], []);
}

/// <summary>Analyzes one prepared image (already resized) and returns the clothing it shows.</summary>
public interface IVisionAnalyzer
{
    /// <summary>Identifies the model, so cached results from another model are not reused.</summary>
    string ModelName { get; }

    /// <param name="imagePath">A prepared JPEG.</param>
    /// <param name="caption">The post caption, if any. Untrusted: only used as a hint for brands.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="AnalysisFailedException">The image could not be analyzed.</exception>
    Task<ImageAnalysis> AnalyzeAsync(string imagePath, string? caption, CancellationToken cancellationToken);
}

/// <summary>Thrown when an image could not be analyzed (model unreachable, timed out, or returned unusable output).</summary>
public sealed class AnalysisFailedException : Exception
{
    public AnalysisFailedException()
    {
    }

    public AnalysisFailedException(string message)
        : base(message)
    {
    }

    public AnalysisFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
