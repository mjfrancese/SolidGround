using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
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
    /// </summary>
    public const int CurrentSchemaVersion = 2;

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
        ElevationRange? elevationRange)
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
