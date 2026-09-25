using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Parses the committed synthetic example-site parcel fixtures (see Fixtures/README.md) through
/// <see cref="ParcelGeometryParser"/>, exercising the GeoJSON and WKT paths against real fixture files rather
/// than inline literals.
/// </summary>
public sealed class ParcelGeometryFixtureTests
{
    private const double ExampleSiteLatitude = 41.591194d;

    [Fact]
    public void GeoJsonFixtureParsesIntoTheExpectedSyntheticLot()
    {
        string text = ReadFixture("example-site-synthetic-parcel.geojson");

        PolygonalRegion region = ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, text, GeographicReference());

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
        Assert.Equal(-93.604047631d, region.Envelope.MinX, 9);
        Assert.Equal(-93.603564371d, region.Envelope.MaxX, 9);
        Assert.Equal(41.591012685d, region.Envelope.MinY, 9);
        Assert.Equal(41.591375478d, region.Envelope.MaxY, 9);

        // The synthetic footprint is 1,600 m^2 (~17,222.26 sq ft); converting the parsed envelope's degree
        // extents back to meters with the same (constant-latitude, ellipsoid-only) factors AoiNormalizer uses
        // should land close to that, though not as tightly as the fixture's own former, hand-computed-in-degree-
        // space rectangle did: this fixture's corners are instead the real forward/inverse transverse-Mercator
        // projection of a true 40 m UTM square (see Fixtures/README.md), so the gap between that genuine
        // projection and this simpler constant-factor approximation shows up here as a real, expected few
        // percent of distortion, not a defect in either the fixture or the approximation.
        double widthMeters = (region.Envelope.MaxX - region.Envelope.MinX) * Wgs84Ellipsoid.MetersPerDegreeLongitude(ExampleSiteLatitude);
        double heightMeters = (region.Envelope.MaxY - region.Envelope.MinY) * Wgs84Ellipsoid.MetersPerDegreeLatitude(ExampleSiteLatitude);
        double approximateAreaSquareMeters = widthMeters * heightMeters;
        double relativeDifference = Math.Abs(approximateAreaSquareMeters - 1600d) / 1600d;
        Assert.True(
            relativeDifference < 0.02d,
            $"Expected approximately 1600 m^2, computed {approximateAreaSquareMeters} (relative difference {relativeDifference}).");
    }

    [Fact]
    public void WktFixtureParsesIntoTheExpectedSyntheticLot()
    {
        string text = ReadFixture("example-site-synthetic-parcel.wkt");

        PolygonalRegion region = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, text, ProjectedReference());

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
        Assert.Equal(449655.327630d, region.Envelope.MinX);
        Assert.Equal(449695.327630d, region.Envelope.MaxX);
        Assert.Equal(4604544.615466d, region.Envelope.MinY);
        Assert.Equal(4604584.615466d, region.Envelope.MaxY);

        double expectedArea = (449695.327630d - 449655.327630d) * (4604584.615466d - 4604544.615466d);
        Assert.Equal(expectedArea, region.Area);
        Assert.Equal(1600d, region.Area, 3);
    }

    [Fact]
    public void BothFixturesParseDeterministically()
    {
        string geoJsonText = ReadFixture("example-site-synthetic-parcel.geojson");
        string wktText = ReadFixture("example-site-synthetic-parcel.wkt");

        PolygonalRegion geoJsonFirst = ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, geoJsonText, GeographicReference());
        PolygonalRegion geoJsonSecond = ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, geoJsonText, GeographicReference());
        Assert.Equal(geoJsonFirst.Polygons.Single().Shell, geoJsonSecond.Polygons.Single().Shell);

        PolygonalRegion wktFirst = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wktText, ProjectedReference());
        PolygonalRegion wktSecond = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wktText, ProjectedReference());
        Assert.Equal(wktFirst.Polygons.Single().Shell, wktSecond.Polygons.Single().Shell);
    }

    [Fact]
    public void FixturesCanFeedAoiNormalizerAsParcelGeometryAoiInputs()
    {
        string geoJsonText = ReadFixture("example-site-synthetic-parcel.geojson");
        ParcelGeometryAoi aoi = new(ParcelGeometryFormat.GeoJson, geoJsonText, GeographicReference());

        NormalizedAoi normalized = AoiNormalizer.Normalize(aoi);

        Assert.Equal(FetchEnvelopeBasis.GeographicParcelEnvelope, normalized.Basis);
        Assert.NotNull(normalized.Parcel);
        Assert.True(normalized.FetchEnvelope.WestLongitude <= normalized.Parcel!.Envelope.MinX);
        Assert.True(normalized.FetchEnvelope.EastLongitude >= normalized.Parcel.Envelope.MaxX);
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
}
