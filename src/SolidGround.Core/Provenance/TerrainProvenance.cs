using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>Reversible provenance retained with a terrain export or later Revit element.</summary>
public sealed record TerrainProvenance
{
    /// <summary>
    /// The schema version this build of SolidGround writes, and the only version its strict reader accepts.
    /// See docs/architecture/provenance-and-deterministic-exports.md's "Versioning and compatibility policy"
    /// section for what changing this constant means and why the old manifest stays documented. Version 2
    /// (SolidGround Issue #21) added <see cref="SourceHorizontalReferenceOrigin"/> and
    /// <see cref="SourceVerticalReferenceOrigin"/>; see "Export document manifest, schema version 2".
    /// Version 3 (SolidGround Issue #33) added <see cref="AddressParcel"/>; see "Export document manifest,
    /// schema version 3". Version 4 (SolidGround Issue #35) added
    /// <see cref="ElevationSourceMetadata.Attribution"/>; see "Export document manifest, schema version 4".
    /// Version 5 (SolidGround Issue #47) added collection-period availability and coverage-floor provenance.
    /// Version 6 adds an optional, explicit building-floor reference without assigning a floor meaning to
    /// historical terrain records.
    /// </summary>
    public const int CurrentSchemaVersion = 6;

    public TerrainProvenance(
        int schemaVersion,
        ElevationSourceMetadata source,
        HorizontalTransformationDefinition horizontalTransformation,
        VerticalReference sourceVerticalReference,
        ReferenceOrigin sourceHorizontalReferenceOrigin,
        ReferenceOrigin sourceVerticalReferenceOrigin,
        LocalCoordinateFrame localFrame,
        SimplificationRequest simplificationRequest,
        int originalPointCount,
        int retainedPointCount,
        ElevationRange? elevationRange,
        AddressParcelProvenance? addressParcel = null,
        BuildingFloorReference? floorReference = null,
        BuildingOutlineProvenance? buildingOutline = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        if (originalPointCount < 0 || retainedPointCount < 0 || retainedPointCount > originalPointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedPointCount), "Point counts must be non-negative and retained count cannot exceed original count.");
        }

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(horizontalTransformation);
        ArgumentNullException.ThrowIfNull(sourceVerticalReference);
        if (!Enum.IsDefined(sourceHorizontalReferenceOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHorizontalReferenceOrigin), sourceHorizontalReferenceOrigin, "Unsupported reference origin kind.");
        }

        if (!Enum.IsDefined(sourceVerticalReferenceOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVerticalReferenceOrigin), sourceVerticalReferenceOrigin, "Unsupported reference origin kind.");
        }

        ArgumentNullException.ThrowIfNull(localFrame);
        ArgumentNullException.ThrowIfNull(simplificationRequest);

        if (schemaVersion == CurrentSchemaVersion && source.CollectionPeriodAvailability is null)
        {
            throw new ArgumentException("Current-schema provenance requires an explicit collection-period availability.", nameof(source));
        }

        if (schemaVersion == CurrentSchemaVersion && simplificationRequest.CoverageFloorFraction is null)
        {
            throw new ArgumentException("Current-schema provenance requires the actual coverage floor fraction.", nameof(simplificationRequest));
        }
        if (retainedPointCount > simplificationRequest.PointBudget)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedPointCount),
                retainedPointCount,
                "Retained point count cannot exceed the requested point budget.");
        }

        if (horizontalTransformation.TargetReference != localFrame.ProjectedHorizontalReference)
        {
            throw new ArgumentException("Horizontal transformation target must equal the local frame projected reference.", nameof(horizontalTransformation));
        }

        if (sourceVerticalReference != localFrame.VerticalReference)
        {
            throw new ArgumentException("Source vertical reference must equal the local frame vertical reference because no vertical datum transformation is performed.", nameof(sourceVerticalReference));
        }

        if (originalPointCount > 0 && elevationRange is null)
        {
            throw new ArgumentException("Elevation range is required when original data is nonempty.", nameof(elevationRange));
        }

        if (elevationRange is not null && elevationRange.Unit != sourceVerticalReference.Unit)
        {
            throw new ArgumentException("Elevation range unit must match the source vertical reference.", nameof(elevationRange));
        }

        if (floorReference is not null && floorReference.SourceReference != sourceVerticalReference)
        {
            throw new ArgumentException("A building floor reference must use the terrain source vertical reference.", nameof(floorReference));
        }

        // Trusted in-memory composition may retain an outline only on the floor reference; the canonical
        // writer emits the effective value in both schema-6 locations. The strict export reader rejects a
        // missing or conflicting serialized counterpart before it reaches this convenience fallback.
        if (buildingOutline is not null && floorReference?.BuildingOutline is { } floorOutline &&
            !BuildingOutlineProvenanceJson.HasSameValue(buildingOutline, floorOutline))
        {
            throw new ArgumentException(
                "The explicit building-outline provenance must equal the floor reference's building-outline provenance.",
                nameof(buildingOutline));
        }

        SchemaVersion = schemaVersion;
        Source = source;
        HorizontalTransformation = horizontalTransformation;
        SourceVerticalReference = sourceVerticalReference;
        SourceHorizontalReferenceOrigin = sourceHorizontalReferenceOrigin;
        SourceVerticalReferenceOrigin = sourceVerticalReferenceOrigin;
        LocalFrame = localFrame;
        SimplificationRequest = simplificationRequest;
        OriginalPointCount = originalPointCount;
        RetainedPointCount = retainedPointCount;
        ElevationRange = elevationRange;
        AddressParcel = addressParcel;
        FloorReference = floorReference;
        ExplicitBuildingOutline = buildingOutline;
    }

    public int SchemaVersion { get; }
    public ElevationSourceMetadata Source { get; }
    public HorizontalTransformationDefinition HorizontalTransformation { get; }
    public HorizontalReference SourceHorizontalReference => HorizontalTransformation.SourceReference;
    public VerticalReference SourceVerticalReference { get; }

    /// <summary>Where <see cref="SourceHorizontalReference"/> actually came from. See <see cref="ReferenceOrigin"/>.</summary>
    public ReferenceOrigin SourceHorizontalReferenceOrigin { get; }

    /// <summary>Where <see cref="SourceVerticalReference"/> actually came from. See <see cref="ReferenceOrigin"/>.</summary>
    public ReferenceOrigin SourceVerticalReferenceOrigin { get; }

    public LocalCoordinateFrame LocalFrame { get; }
    public SimplificationRequest SimplificationRequest { get; }
    public int OriginalPointCount { get; }
    public int RetainedPointCount { get; }
    public ElevationRange? ElevationRange { get; }

    /// <summary>How this export's area of interest was located via an address/parcel lookup, or null when it was not (SolidGround Issue #33). Always null in a schema version 1 or 2 document.</summary>
    public AddressParcelProvenance? AddressParcel { get; }

    /// <summary>Optional operator-selected datum evidence; null preserves the historical source-elevation interpretation.</summary>
    public BuildingFloorReference? FloorReference { get; }

    /// <summary>
    /// Building-outline acquisition attribution retained even when the operator keeps the historical
    /// source-elevation placement and therefore supplies no floor reference.
    /// </summary>
    public BuildingOutlineProvenance? BuildingOutline => ExplicitBuildingOutline ?? FloorReference?.BuildingOutline;

    private BuildingOutlineProvenance? ExplicitBuildingOutline { get; }
}

/// <summary>Finite minimum and maximum elevations expressed in an explicit vertical unit.</summary>
public sealed record ElevationRange
{
    public ElevationRange(double minimum, double maximum, LengthUnit unit)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Elevation bounds must be finite and ordered.");
        }

        _ = LengthConverter.MetersPerUnit(unit);
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public LengthUnit Unit { get; }
}
