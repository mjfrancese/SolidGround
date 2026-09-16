using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>Reversible provenance retained with a terrain export or later Revit element.</summary>
public sealed record TerrainProvenance
{
    public TerrainProvenance(
        int schemaVersion,
        ElevationSourceMetadata source,
        HorizontalTransformationDefinition horizontalTransformation,
        VerticalReference sourceVerticalReference,
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
