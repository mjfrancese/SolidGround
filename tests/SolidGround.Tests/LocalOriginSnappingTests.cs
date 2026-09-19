using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class LocalOriginSnappingTests
{
    [Fact]
    public void SnapToWholeSourceUnitRejectsNullReferences()
    {
        Coordinate3D candidate = new(0d, 0d, 0d);

        Assert.Throws<ArgumentNullException>(() => LocalOriginSnapping.SnapToWholeSourceUnit(candidate, null!, VerticalReference()));
        Assert.Throws<ArgumentNullException>(() => LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), null!));
    }

    [Fact]
    public void SnapToWholeSourceUnitRejectsAGeographicHorizontalReference()
    {
        Coordinate3D candidate = new(-90.5, 38.7, 183d);

        Assert.Throws<ArgumentException>(() => LocalOriginSnapping.SnapToWholeSourceUnit(candidate, GeographicReference(), VerticalReference()));
    }

    [Fact]
    public void SnapToWholeSourceUnitFloorsEachAxisIndependently()
    {
        Coordinate3D candidate = new([withheld], [withheld], 183.99);

        Coordinate3D snapped = LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), VerticalReference());

        Assert.Equal([withheld], snapped.X);
        Assert.Equal([withheld], snapped.Y);
        Assert.Equal(183d, snapped.Elevation);
    }

    [Fact]
    public void SnapToWholeSourceUnitProducesAnIntegerValuedDoubleInMetres()
    {
        // Deliberately near a whole-number boundary ([withheld], one part in 1e10 below [withheld]) to prove
        // the snapped result is genuinely an exact whole number in the projected reference's own metres, not
        // merely "close to one" with a residual floating-point fraction.
        Coordinate3D candidate = new([withheld], [withheld], 183.5);

        Coordinate3D snapped = LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), VerticalReference());

        Assert.Equal(0d, snapped.X % 1d);
        Assert.Equal(0d, snapped.Y % 1d);
        Assert.Equal(0d, snapped.Elevation % 1d);
    }

    [Theory]
    [InlineData([withheld], [withheld], 183.99)]
    [InlineData([withheld], [withheld], 183d)]
    [InlineData(-10.25, -20.75, -5.5)]
    public void SnapToWholeSourceUnitNeverMovesPastTheCandidateOnAnyAxis(double x, double y, double elevation)
    {
        Coordinate3D candidate = new(x, y, elevation);

        Coordinate3D snapped = LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), VerticalReference());

        Assert.True(snapped.X <= candidate.X);
        Assert.True(snapped.Y <= candidate.Y);
        Assert.True(snapped.Elevation <= candidate.Elevation);
    }

    [Fact]
    public void SnapToWholeSourceUnitIsIdempotent()
    {
        Coordinate3D candidate = new([withheld], [withheld], 183.99);
        Coordinate3D snappedOnce = LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), VerticalReference());

        Coordinate3D snappedTwice = LocalOriginSnapping.SnapToWholeSourceUnit(snappedOnce, ProjectedReference(), VerticalReference());

        Assert.Equal(snappedOnce, snappedTwice);
    }

    [Fact]
    public void SnappedOriginFeedsLocalCoordinateFrameForABitExactRoundTrip()
    {
        Coordinate3D candidate = new([withheld], [withheld], 183.99);
        Coordinate3D snappedOrigin = LocalOriginSnapping.SnapToWholeSourceUnit(candidate, ProjectedReference(), VerticalReference());
        LocalCoordinateFrame frame = new(snappedOrigin, ProjectedReference(), VerticalReference(), LengthUnit.UsSurveyFoot);
        Coordinate3D source = new([withheld], [withheld], 190d);

        LocalCoordinate local = frame.ToLocal(source);
        Coordinate3D roundTripped = frame.ToSource(local);

        Assert.Equal(source, roundTripped);
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference(LengthUnit unit = LengthUnit.Meter) => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(unit), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.Meter) => new("NAVD88", unit);
}
