using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="LocalCoordinateFrame.ToLocalHorizontal"/>, added for SolidGround Issue #15's
/// <c>SolidGround.Core.Exports.LocalBoundary</c> (deliberately Z-less; see
/// <see cref="LocalBoundaryFactoryTests"/>). Confirms this horizontal-only projection agrees with
/// <see cref="LocalCoordinateFrame.ToLocal"/>'s own X/Y for the same source point, and independently exercises
/// the origin-subtraction and unit-conversion steps it shares with <see cref="LocalCoordinateFrame.ToLocal"/>.
/// </summary>
public sealed class LocalCoordinateFrameHorizontalTests
{
    [Fact]
    public void ToLocalHorizontalMatchesTheXAndYOfToLocalForTheSameSourcePoint()
    {
        LocalCoordinateFrame frame = new(new Coordinate3D(100d, 200d, 10d), ProjectedReference(), VerticalReference(), LengthUnit.Meter);
        Coordinate3D source3D = new(150d, 260d, 25d);
        Coordinate2D source2D = new(source3D.X, source3D.Y);

        LocalCoordinate local3D = frame.ToLocal(source3D);
        LocalCoordinate2D local2D = frame.ToLocalHorizontal(source2D);

        Assert.Equal(local3D.X, local2D.X);
        Assert.Equal(local3D.Y, local2D.Y);
    }

    [Fact]
    public void ToLocalHorizontalAppliesOriginSubtraction()
    {
        LocalCoordinateFrame frame = new(new Coordinate3D(500d, 1000d, 0d), ProjectedReference(), VerticalReference(), LengthUnit.Meter);

        LocalCoordinate2D local = frame.ToLocalHorizontal(new Coordinate2D(530d, 1040d));

        Assert.Equal(30d, local.X);
        Assert.Equal(40d, local.Y);
    }

    [Fact]
    public void ToLocalHorizontalAppliesUnitConversion()
    {
        // A projected reference already in meters, converted to an output unit of US survey feet: a 3-4-5
        // triangle offset (3 m, 4 m from the origin) becomes (3 / (1200/3937), 4 / (1200/3937)) US survey feet.
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), ProjectedReference(), VerticalReference(), LengthUnit.UsSurveyFoot);

        LocalCoordinate2D local = frame.ToLocalHorizontal(new Coordinate2D(3d, 4d));

        double metersPerUsSurveyFoot = 1200d / 3937d;
        Assert.Equal(3d / metersPerUsSurveyFoot, local.X, precision: 9);
        Assert.Equal(4d / metersPerUsSurveyFoot, local.Y, precision: 9);
    }

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
