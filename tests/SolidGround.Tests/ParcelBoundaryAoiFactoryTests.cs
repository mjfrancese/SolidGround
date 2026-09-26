using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ParcelBoundaryAoiFactoryTests
{
    private const string SquareWkt = "POLYGON((-93.6041 41.5910, -93.6036 41.5910, -93.6036 41.5914, -93.6041 41.5914, -93.6041 41.5910))";

    [Fact]
    public void FromCandidateRoundTripsThroughWktIntoAnEquivalentAreaParcelGeometryAoi()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());
        ParcelBoundaryCandidate candidate = new(
            boundary, "PARCEL-1", 1600d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.");

        ParcelGeometryAoi aoi = ParcelBoundaryAoiFactory.FromCandidate(candidate);

        Assert.Equal(ParcelGeometryFormat.Wkt, aoi.Format);
        Assert.Equal(boundary.HorizontalReference, aoi.HorizontalReference);
        Assert.Equal(LinearDistance.Zero, aoi.Buffer);

        // No new AreaOfInterestKind: the result is today's existing ParcelGeometryAoi, and re-parsing its own
        // WKT text recovers an equivalent-area PolygonalRegion.
        PolygonalRegion roundTripped = ParcelGeometryParser.Parse(aoi);
        Assert.Equal(boundary.Area, roundTripped.Area, 9);
        Assert.Equal(boundary.PolygonCount, roundTripped.PolygonCount);
        Assert.Equal(boundary.HoleCount, roundTripped.HoleCount);
    }

    [Fact]
    public void FromCandidateAppliesTheSuppliedBuffer()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());
        ParcelBoundaryCandidate candidate = new(
            boundary, "PARCEL-1", 1600d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.");

        ParcelGeometryAoi aoi = ParcelBoundaryAoiFactory.FromCandidate(candidate, LinearDistance.Meters(2));

        Assert.Equal(LinearDistance.Meters(2), aoi.Buffer);
    }

    [Fact]
    public void RejectsANullCandidate()
    {
        Assert.Throws<ArgumentNullException>(() => ParcelBoundaryAoiFactory.FromCandidate(null!));
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
