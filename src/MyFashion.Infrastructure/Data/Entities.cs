using MyFashion.Core.Analysis;
using MyFashion.Core.Application;
using MyFashion.Core.Media;

namespace MyFashion.Infrastructure.Data;

/// <summary>An analyzed Instagram account or folder.</summary>
public sealed class AccountEntity
{
    public int Id { get; set; }

    public required string Username { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<RunEntity> Runs { get; } = [];
}

/// <summary>One analysis run.</summary>
public sealed class RunEntity
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public AccountEntity? Account { get; set; }

    public RunSource Source { get; set; }

    public RunStatus Status { get; set; }

    public required string Model { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    public string? Error { get; set; }

    public int MediaFiles { get; set; }

    public int Images { get; set; }

    public int Duplicates { get; set; }

    public int Analyzed { get; set; }

    public int NoOutfit { get; set; }

    public int Failed { get; set; }

    public int FromCache { get; set; }

    public List<MediaItemEntity> MediaItems { get; } = [];
}

/// <summary>One prepared image and what the model found in it.</summary>
public sealed class MediaItemEntity
{
    public int Id { get; set; }

    public int RunId { get; set; }

    public RunEntity? Run { get; set; }

    public MediaKind Kind { get; set; }

    public required string SourcePath { get; set; }

    public string? PreparedPath { get; set; }

    public string? Sha256 { get; set; }

    public long DHash { get; set; }

    public string? Caption { get; set; }

    public DateTime? TakenAtUtc { get; set; }

    public MediaStatus Status { get; set; }

    public string? Error { get; set; }

    /// <summary>Comma-separated canonical style tags.</summary>
    public string StyleTags { get; set; } = string.Empty;

    public List<GarmentEntity> Garments { get; } = [];
}

/// <summary>One clothing item in an image.</summary>
public sealed class GarmentEntity
{
    public int Id { get; set; }

    public int MediaItemId { get; set; }

    public MediaItemEntity? MediaItem { get; set; }

    public GarmentCategory Category { get; set; }

    public required string Type { get; set; }

    public required string Color { get; set; }

    public GarmentPattern Pattern { get; set; }

    public GarmentFit Fit { get; set; }

    public string? Brand { get; set; }

    public BrandEvidence BrandEvidence { get; set; }

    public double Confidence { get; set; }
}
