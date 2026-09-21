using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Transformations;

/// <summary>Reversibly shifts projected horizontal and vertical source ordinates into one output unit.</summary>
public sealed record LocalCoordinateFrame
{
    public LocalCoordinateFrame(
        Coordinate3D origin,
        HorizontalReference projectedHorizontalReference,
        VerticalReference verticalReference,
        LengthUnit outputUnit = LengthConverter.DefaultOutputUnit)
    {
        ArgumentNullException.ThrowIfNull(projectedHorizontalReference);
        ArgumentNullException.ThrowIfNull(verticalReference);
        if (projectedHorizontalReference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new ArgumentException("A local coordinate frame requires a projected horizontal reference.", nameof(projectedHorizontalReference));
        }

        LengthUnit horizontalUnit = projectedHorizontalReference.Unit.LinearUnit
            ?? throw new ArgumentException("A projected horizontal reference requires a linear unit.", nameof(projectedHorizontalReference));
        _ = LengthConverter.MetersPerUnit(horizontalUnit);
        _ = LengthConverter.MetersPerUnit(verticalReference.Unit);
        _ = LengthConverter.MetersPerUnit(outputUnit);
        Origin = origin;
        ProjectedHorizontalReference = projectedHorizontalReference;
        VerticalReference = verticalReference;
        OutputUnit = outputUnit;
    }

    public Coordinate3D Origin { get; }
    public HorizontalReference ProjectedHorizontalReference { get; }
    public VerticalReference VerticalReference { get; }
    public LengthUnit OutputUnit { get; }

    public LocalCoordinate ToLocal(Coordinate3D source) => new(
        LengthConverter.Convert(source.X - Origin.X, HorizontalUnit, OutputUnit),
        LengthConverter.Convert(source.Y - Origin.Y, HorizontalUnit, OutputUnit),
        LengthConverter.Convert(source.Elevation - Origin.Elevation, VerticalReference.Unit, OutputUnit));

    /// <summary>
    /// The horizontal-only projection of <see cref="ToLocal"/>: the same origin subtraction and unit
    /// conversion, with no elevation/vertical-reference involvement. Added for SolidGround Issue #15's
    /// <c>SolidGround.Core.Exports.LocalBoundary</c>, which is deliberately Z-less -- the created
    /// <c>Toposolid</c>'s boundary-ring elevation is instead one Revit-side scalar applied uniformly (design
    /// record §7.2).
    /// </summary>
    public LocalCoordinate2D ToLocalHorizontal(Coordinate2D source) => new(
        LengthConverter.Convert(source.X - Origin.X, HorizontalUnit, OutputUnit),
        LengthConverter.Convert(source.Y - Origin.Y, HorizontalUnit, OutputUnit));

    public Coordinate3D ToSource(LocalCoordinate local) => new(
        Origin.X + LengthConverter.Convert(local.X, OutputUnit, HorizontalUnit),
        Origin.Y + LengthConverter.Convert(local.Y, OutputUnit, HorizontalUnit),
        Origin.Elevation + LengthConverter.Convert(local.Elevation, OutputUnit, VerticalReference.Unit));

    private LengthUnit HorizontalUnit => ProjectedHorizontalReference.Unit.LinearUnit!.Value;
}
