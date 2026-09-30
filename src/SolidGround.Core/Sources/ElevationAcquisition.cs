using SolidGround.Core.Aois;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Sources;

/// <summary>Whether a source reported a factual collection period for this acquisition.</summary>
public enum CollectionPeriodAvailability
{
    Reported,
    NotReportedBySource,
}

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
    /// <param name="sourceName">The elevation source's display name. Required.</param>
    /// <param name="datasetIdentifier">The source's own dataset identifier. Required.</param>
    /// <param name="collectionPeriod">
    /// The dataset's collection interval, or null when the source does not report one. Some sources,
    /// such as OpenTopography's usgsdem endpoint, carry neither a collection period nor a catalog
    /// quality level in their response; fabricating catalog values here would violate the project's
    /// "fail rather than assume" rule, so both are optional.
    /// </param>
    /// <param name="qualityLevel">The dataset's quality level, or null when the source does not report one. When supplied, it cannot be blank.</param>
    /// <param name="attribution">
    /// The source's own required attribution/citation text, or null when the source does not carry one. When
    /// supplied, it cannot be blank. See docs/architecture/source-licensing-and-attribution.md's "Attribution
    /// catalogue" section for each shipped source's own attribution text and citation.
    /// </param>
    public ElevationSourceMetadata(
        string sourceName, string datasetIdentifier,
        CollectionPeriod? collectionPeriod = null, string? qualityLevel = null, string? attribution = null,
        CollectionPeriodAvailability? collectionPeriodAvailability = null)
        : this(
            sourceName,
            datasetIdentifier,
            collectionPeriod,
            qualityLevel,
            attribution,
            collectionPeriodAvailability ?? (collectionPeriod is null
                ? global::SolidGround.Core.Sources.CollectionPeriodAvailability.NotReportedBySource
                : global::SolidGround.Core.Sources.CollectionPeriodAvailability.Reported),
            isLegacyAvailabilityUnknown: false)
    {
    }

    private ElevationSourceMetadata(
        string sourceName,
        string datasetIdentifier,
        CollectionPeriod? collectionPeriod,
        string? qualityLevel,
        string? attribution,
        CollectionPeriodAvailability? collectionPeriodAvailability,
        bool isLegacyAvailabilityUnknown)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("A source name is required.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(datasetIdentifier))
        {
            throw new ArgumentException("A dataset identifier is required.", nameof(datasetIdentifier));
        }

        if (qualityLevel is not null && string.IsNullOrWhiteSpace(qualityLevel))
        {
            throw new ArgumentException("Quality level cannot be blank when it is supplied.", nameof(qualityLevel));
        }

        if (attribution is not null && string.IsNullOrWhiteSpace(attribution))
        {
            throw new ArgumentException("Attribution cannot be blank when it is supplied.", nameof(attribution));
        }

        if (collectionPeriodAvailability is { } availability && !Enum.IsDefined(availability))
        {
            throw new ArgumentOutOfRangeException(nameof(collectionPeriodAvailability), availability, "Unsupported collection-period availability.");
        }

        if (isLegacyAvailabilityUnknown != (collectionPeriodAvailability is null))
        {
            throw new ArgumentException("Only a legacy source record may have an unknown collection-period availability.", nameof(collectionPeriodAvailability));
        }

        if (collectionPeriod is null && collectionPeriodAvailability == global::SolidGround.Core.Sources.CollectionPeriodAvailability.Reported)
        {
            throw new ArgumentException("A reported collection period requires dates.", nameof(collectionPeriodAvailability));
        }

        if (collectionPeriod is not null && collectionPeriodAvailability != global::SolidGround.Core.Sources.CollectionPeriodAvailability.Reported)
        {
            throw new ArgumentException("Collection-period dates must be marked reported.", nameof(collectionPeriodAvailability));
        }

        SourceName = sourceName;
        DatasetIdentifier = datasetIdentifier;
        CollectionPeriod = collectionPeriod;
        CollectionPeriodAvailability = collectionPeriodAvailability;
        QualityLevel = qualityLevel;
        Attribution = attribution;
    }

    public string SourceName { get; }
    public string DatasetIdentifier { get; }
    public CollectionPeriod? CollectionPeriod { get; }
    /// <summary>
    /// Explicit source status for collection-period metadata. Null only represents a schema version 4 export,
    /// whose historical writer did not carry this field.
    /// </summary>
    public CollectionPeriodAvailability? CollectionPeriodAvailability { get; }
    public string? QualityLevel { get; }
    public string? Attribution { get; }

    /// <summary>Reconstructs a pre-status export without assigning it a factual or inferred status.</summary>
    internal static ElevationSourceMetadata FromLegacy(
        string sourceName,
        string datasetIdentifier,
        CollectionPeriod? collectionPeriod,
        string? qualityLevel,
        string? attribution) =>
        new(sourceName, datasetIdentifier, collectionPeriod, qualityLevel, attribution, null, isLegacyAvailabilityUnknown: true);
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
