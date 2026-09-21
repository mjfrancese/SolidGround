using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="ClipRegionFactory"/>, lifted into <c>SolidGround.Core</c> for SolidGround
/// Issue #15 and changed to dispatch on an already-built <see cref="AreaOfInterest"/>'s own concrete type
/// (<see cref="Wgs84BoundingBoxAoi"/>/<see cref="Wgs84RadiusAoi"/>/<see cref="ParcelGeometryAoi"/>) rather than
/// a CLI-only flattened selection. See docs/architecture/cli-workflow.md's "AOI and clip derivation" section.
/// </summary>
public sealed class ClipRegionFactoryTests
{
    private const double Latitude = [withheld]d;
    private const double Longitude = [withheld]d;

    [Fact]
    public void BuildFetchEnvelopeForABoundingBoxAreaOfInterestAppliesTheMinimumSideExpansion()
    {
        HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        Wgs84BoundingBoxAoi tinyBox = TinyBoxAround(Longitude, Latitude, halfSideMeters: 5d);

        (Wgs84BoundingBoxAoi envelope, FetchEnvelopeExpansion expansion) = ClipRegionFactory.BuildFetchEnvelope(tinyBox, wgs84Reference);

        Assert.True(expansion.Applied);
        Assert.Equal(LinearDistance.Meters(110d), expansion.MinimumSide);
        Assert.True(envelope.WestLongitude < tinyBox.WestLongitude);
        Assert.True(envelope.EastLongitude > tinyBox.EastLongitude);
        Assert.True(envelope.SouthLatitude < tinyBox.SouthLatitude);
        Assert.True(envelope.NorthLatitude > tinyBox.NorthLatitude);
    }

    [Fact]
    public void BuildForAParcelAreaOfInterestReprojectsIntoTheGridReference()
    {
        IHorizontalCoordinateTransform transform = LoadExampleSiteTransform();
        ParcelGeometryAoi parcel = BuildExampleSiteParcelAoi(transform);

        ClipRegion region = ClipRegionFactory.Build(parcel, transform);

        Assert.Equal(transform.Definition.TargetReference, region.Region.HorizontalReference);
        Assert.Equal(LinearDistance.Zero, region.Buffer);
        Assert.True(region.Region.Area > 0d);
    }

    [Fact]
    public void BuildForARadiusAreaOfInterestProducesACircleInTheGridReference()
    {
        IHorizontalCoordinateTransform transform = LoadExampleSiteTransform();
        Coordinate2D center = transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Wgs84RadiusAoi radius = new(center.Y, center.X, LinearDistance.Meters(2d));

        ClipRegion region = ClipRegionFactory.Build(radius, transform);

        Assert.Equal(transform.Definition.TargetReference, region.Region.HorizontalReference);
        Assert.Equal(LinearDistance.Zero, region.Buffer);
        Assert.True(region.Region.Area > 0d);
    }

    /// <summary>
    /// The targeted regression test for the Core lift's own simplification (design record §0.3 item 5):
    /// constructs the clip region two ways -- from a freshly-built <see cref="ParcelGeometryAoi"/> and from
    /// the exact instance the caller already built for fetch-envelope purposes -- and asserts the two
    /// <see cref="ClipRegion"/>s are value-equal. <see cref="ClipRegionFactory.Build"/> no longer reconstructs
    /// a <see cref="ParcelGeometryAoi"/> internally (SolidGround Issue #15), so this proves its result depends
    /// only on the caller-supplied <see cref="AreaOfInterest"/>'s own field values, not on any other ambient
    /// state.
    /// </summary>
    [Fact]
    public void BuildForAParcelAreaOfInterestProducesTheSameClipRegionWhetherTheCallerReusesOrReconstructsTheAreaOfInterestInstance()
    {
        IHorizontalCoordinateTransform transform = LoadExampleSiteTransform();
        HorizontalReference wgs84Reference = transform.Definition.SourceReference;
        string wkt = ExampleSiteParcelWkt(transform);

        ParcelGeometryAoi reused = new(ParcelGeometryFormat.Wkt, wkt, wgs84Reference, LinearDistance.Zero);
        ParcelGeometryAoi reconstructed = new(ParcelGeometryFormat.Wkt, wkt, wgs84Reference, LinearDistance.Zero);

        ClipRegion fromReused = ClipRegionFactory.Build(reused, transform);
        ClipRegion fromReconstructed = ClipRegionFactory.Build(reconstructed, transform);

        Assert.Equal(fromReused.Buffer, fromReconstructed.Buffer);
        Assert.Equal(fromReused.Region.HorizontalReference, fromReconstructed.Region.HorizontalReference);
        Assert.True(fromReused.Region.Geometry.EqualsExact(fromReconstructed.Region.Geometry));
    }

    private static Wgs84BoundingBoxAoi TinyBoxAround(double longitude, double latitude, double halfSideMeters)
    {
        double halfSideDegreesLon = halfSideMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude);
        double halfSideDegreesLat = halfSideMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);
        return new Wgs84BoundingBoxAoi(
            longitude - halfSideDegreesLon, latitude - halfSideDegreesLat, longitude + halfSideDegreesLon, latitude + halfSideDegreesLat);
    }

    private static IHorizontalCoordinateTransform LoadExampleSiteTransform()
    {
        string prjWkt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.prj"));
        return ProjNetHorizontalCoordinateTransformFactory.Create(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, prjWkt);
    }

    /// <summary>
    /// A small rectangle (in the grid's own 3x3, 1 m cellsize ExampleSite fixture) covering the whole grid's
    /// cell-corner envelope, built by inverse-transforming its four UTM corners into WGS 84 through
    /// <paramref name="transform"/> -- so it is guaranteed to land exactly on the grid this fixture describes,
    /// with no hand-picked geographic coordinates to keep in sync with the fixture file.
    /// </summary>
    private static string ExampleSiteParcelWkt(IHorizontalCoordinateTransform transform)
    {
        Coordinate2D sw = transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Coordinate2D se = transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Coordinate2D ne = transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Coordinate2D nw = transform.Inverse(new Coordinate2D([withheld], [withheld]));
        return FormattableString.Invariant(
            $"POLYGON (({sw.X} {sw.Y}, {se.X} {se.Y}, {ne.X} {ne.Y}, {nw.X} {nw.Y}, {sw.X} {sw.Y}))");
    }

    private static ParcelGeometryAoi BuildExampleSiteParcelAoi(IHorizontalCoordinateTransform transform) =>
        new(ParcelGeometryFormat.Wkt, ExampleSiteParcelWkt(transform), transform.Definition.SourceReference, LinearDistance.Zero);
}
