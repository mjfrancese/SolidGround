using SolidGround.Core.Aois;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Sources;

/// <summary>
/// Describes the area requested from an elevation source.
/// </summary>
public sealed record ElevationSourceRequest
{
    public ElevationSourceRequest(AreaOfInterest areaOfInterest)
    {
        AreaOfInterest = areaOfInterest ?? throw new ArgumentNullException(nameof(areaOfInterest));
    }

    public AreaOfInterest AreaOfInterest { get; }
}

/// <summary>
/// Couples source-returned terrain data with source-specific metadata.
/// </summary>
public sealed record ElevationAcquisition
{
    public ElevationAcquisition(ElevationData data, ElevationSourceMetadata source)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ElevationData Data { get; }
    public ElevationSourceMetadata Source { get; }
}

/// <summary>
/// Identifies the source dataset from which elevation data was acquired.
/// </summary>
public sealed record ElevationSourceMetadata
{
    public ElevationSourceMetadata(string sourceName, string datasetIdentifier, CollectionPeriod collectionPeriod, string qualityLevel)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("A source name is required.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(datasetIdentifier))
        {
            throw new ArgumentException("A dataset identifier is required.", nameof(datasetIdentifier));
        }

        ArgumentNullException.ThrowIfNull(collectionPeriod);
        if (string.IsNullOrWhiteSpace(qualityLevel))
        {
            throw new ArgumentException("A quality level is required.", nameof(qualityLevel));
        }

        SourceName = sourceName;
        DatasetIdentifier = datasetIdentifier;
        CollectionPeriod = collectionPeriod;
        QualityLevel = qualityLevel;
    }

    public string SourceName { get; }
    public string DatasetIdentifier { get; }
    public CollectionPeriod CollectionPeriod { get; }
    public string QualityLevel { get; }
}

/// <summary>An inclusive, validated date interval for data collection.</summary>
public sealed record CollectionPeriod
{
    public CollectionPeriod(DateOnly start, DateOnly end)
    {
        if (start > end)
        {
            throw new ArgumentException("Collection start must be on or before collection end.", nameof(start));
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }
}
