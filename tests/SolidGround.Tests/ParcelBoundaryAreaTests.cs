using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Proves <see cref="ParcelBoundaryWgs84.ComputeAreaSquareMeters"/> uses the boundary's own true polygon area,
/// never a bounding-box (envelope width times height) approximation, which would systematically overstate area
/// for any non-rectangular parcel -- the norm for cadastral data. Uses an inline, non-committed, notched
/// ("L-shaped") polygon literal, the same "inline small ring arrays" style <see cref="EsriJsonPolygonReaderTests"/>
/// already uses, rather than a committed fixture (whose own near-rectangular ring would not expose this
/// distinction).
/// </summary>
public sealed class ParcelBoundaryAreaTests
{
    // An L-shape: a 10x10 square with a 4x4 corner notch removed, so its bounding-box area (100, in square
    // degree-like units) and its true polygon area (100 - 16 = 84) differ by ~19%, well over the 2% tolerance
    // this codebase otherwise uses for area assertions. Centered on latitude 45 (an arbitrary, non-real
    // location) purely so the meters-per-degree scale factors are realistic mid-latitude values.
    private const string NotchedPolygonWkt = "POLYGON((0 40, 10 40, 10 46, 6 46, 6 50, 0 50, 0 40))";
    private const double TrueAreaSquareDegreeUnits = 84d;
    private const double BoundingBoxAreaSquareDegreeUnits = 100d;
    private const double CenterLatitude = 45d;

    [Fact]
    public void ComputesTrueBoundaryAreaNotBoundingBoxArea()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, NotchedPolygonWkt, GeographicReference());

        double metersPerDegreeLongitude = Wgs84Ellipsoid.MetersPerDegreeLongitude(CenterLatitude);
        double metersPerDegreeLatitude = Wgs84Ellipsoid.MetersPerDegreeLatitude(CenterLatitude);
        double expectedTrueAreaSquareMeters = TrueAreaSquareDegreeUnits * metersPerDegreeLongitude * metersPerDegreeLatitude;
        double rejectedBoundingBoxAreaSquareMeters = BoundingBoxAreaSquareDegreeUnits * metersPerDegreeLongitude * metersPerDegreeLatitude;

        double actualAreaSquareMeters = ParcelBoundaryWgs84.ComputeAreaSquareMeters(boundary);

        // The true-area computation, verified independently by decomposing the L-shape into its own two
        // rectangles (10x10 minus 4x4) rather than by re-deriving the shoelace formula the implementation
        // itself uses.
        double relativeDifferenceFromTrueArea = Math.Abs(actualAreaSquareMeters - expectedTrueAreaSquareMeters) / expectedTrueAreaSquareMeters;
        Assert.True(relativeDifferenceFromTrueArea < 1e-9, $"Expected {expectedTrueAreaSquareMeters}, got {actualAreaSquareMeters}.");

        // The rejected bounding-box formula would have differed from the true area by well over 2% -- proving
        // this is a meaningful distinction, not a rounding-scale coincidence.
        double relativeDifferenceFromBoundingBox = Math.Abs(rejectedBoundingBoxAreaSquareMeters - expectedTrueAreaSquareMeters) / expectedTrueAreaSquareMeters;
        Assert.True(relativeDifferenceFromBoundingBox > 0.02d, "Expected the bounding-box area to differ from the true area by more than 2%.");
        Assert.True(
            Math.Abs(actualAreaSquareMeters - rejectedBoundingBoxAreaSquareMeters) / rejectedBoundingBoxAreaSquareMeters > 0.02d,
            "The computed area must not match the rejected bounding-box formula's result.");
    }

    [Fact]
    public void RejectsANullBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => ParcelBoundaryWgs84.ComputeAreaSquareMeters(null!));
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
