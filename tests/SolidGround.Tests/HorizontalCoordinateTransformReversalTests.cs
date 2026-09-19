using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="HorizontalCoordinateTransforms.Reverse(IHorizontalCoordinateTransform)"/>: the orientation
/// contract it documents, its <see cref="HorizontalTransformationDefinition"/> swap, its delegation to the
/// wrapped transform's opposite method, and the integration seam it exists to serve
/// (<see cref="AoiNormalizer"/>'s injected <c>parcelToWgs84</c> transform for a projected parcel).
/// </summary>
public sealed class HorizontalCoordinateTransformReversalTests
{
    private static readonly Coordinate2D FixtureUtmPoint = new([withheld], [withheld]);
    private static readonly Coordinate2D FixtureLonLatPoint = new([withheld]d, [withheld]d);

    [Fact]
    public void ReverseRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HorizontalCoordinateTransforms.Reverse(null!));
    }

    [Fact]
    public void ReverseSwapsSourceAndTargetReferencesAndOperationsAndPreservesEngineFields()
    {
        IHorizontalCoordinateTransform original = CreateFixtureTransform();

        IHorizontalCoordinateTransform reversed = HorizontalCoordinateTransforms.Reverse(original);

        Assert.Equal(original.Definition.TargetReference, reversed.Definition.SourceReference);
        Assert.Equal(original.Definition.SourceReference, reversed.Definition.TargetReference);
        Assert.Equal(original.Definition.InverseOperation, reversed.Definition.ForwardOperation);
        Assert.Equal(original.Definition.ForwardOperation, reversed.Definition.InverseOperation);
        Assert.Equal(original.Definition.EngineName, reversed.Definition.EngineName);
        Assert.Equal(original.Definition.EngineVersion, reversed.Definition.EngineVersion);

        // The reference and operation objects reused as-is (not rebuilt), so the swapped definition's own
        // constructor validation trivially passes -- but confirm it really is a distinct, newly built
        // definition, not a mutated alias of the original.
        Assert.NotSame(original.Definition, reversed.Definition);
    }

    [Fact]
    public void ReversedForwardAndInverseDelegateToTheOriginalsOppositeMethodBitForBit()
    {
        IHorizontalCoordinateTransform original = CreateFixtureTransform();
        IHorizontalCoordinateTransform reversed = HorizontalCoordinateTransforms.Reverse(original);

        Coordinate2D reversedForward = reversed.Forward(FixtureUtmPoint);
        Coordinate2D originalInverse = original.Inverse(FixtureUtmPoint);
        Assert.Equal(originalInverse.X, reversedForward.X);
        Assert.Equal(originalInverse.Y, reversedForward.Y);

        Coordinate2D reversedInverse = reversed.Inverse(FixtureLonLatPoint);
        Coordinate2D originalForward = original.Forward(FixtureLonLatPoint);
        Assert.Equal(originalForward.X, reversedInverse.X);
        Assert.Equal(originalForward.Y, reversedInverse.Y);
    }

    [Fact]
    public void ReversingTwiceRoundTripsToAnEqualDefinitionAndBitIdenticalOutputs()
    {
        IHorizontalCoordinateTransform original = CreateFixtureTransform();

        IHorizontalCoordinateTransform doubleReversed = HorizontalCoordinateTransforms.Reverse(HorizontalCoordinateTransforms.Reverse(original));

        Assert.Equal(original.Definition, doubleReversed.Definition);

        Coordinate2D originalForward = original.Forward(FixtureLonLatPoint);
        Coordinate2D doubleReversedForward = doubleReversed.Forward(FixtureLonLatPoint);
        Assert.Equal(originalForward.X, doubleReversedForward.X);
        Assert.Equal(originalForward.Y, doubleReversedForward.Y);

        Coordinate2D originalInverse = original.Inverse(FixtureUtmPoint);
        Coordinate2D doubleReversedInverse = doubleReversed.Inverse(FixtureUtmPoint);
        Assert.Equal(originalInverse.X, doubleReversedInverse.X);
        Assert.Equal(originalInverse.Y, doubleReversedInverse.Y);
    }

    [Fact]
    public void NormalizingAProjectedParcelWithAReversedTransformProducesAWgs84EnvelopeContainingEveryOriginalVertex()
    {
        // Arrange: the real ExampleSite WGS 84 parcel fixture, and the real ProjNET-backed transform this issue
        // builds from Wgs84WellKnownText to the example-site-synthetic.prj fixture (geographic source, projected
        // target -- Create's own fixed orientation).
        string wgs84GeoJsonText = ReadFixture("example-site-synthetic-parcel.geojson");
        HorizontalReference wgs84 = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        ParcelGeometryAoi wgs84Aoi = new(ParcelGeometryFormat.GeoJson, wgs84GeoJsonText, wgs84);
        PolygonalRegion wgs84Parcel = ParcelGeometryParser.Parse(wgs84Aoi);

        IHorizontalCoordinateTransform transform = CreateFixtureTransform();

        // Reproject the WGS 84 parcel Forward into the fixture's projected reference with
        // PolygonalRegionReprojection, exactly as a real acquire-reproject-clip pipeline would produce a
        // projected parcel to normalize.
        PolygonalRegion projectedParcel = PolygonalRegionReprojection.Reproject(wgs84Parcel, transform, HorizontalTransformDirection.Forward);

        ParcelGeometryAoi projectedAoi = new(
            ParcelGeometryFormat.Wkt,
            projectedParcel.Geometry.AsText(),
            transform.Definition.TargetReference,
            LinearDistance.Meters(2d));

        // Act: AoiNormalizer.NormalizeProjectedParcel calls parcelToWgs84.Forward on vertices already in the
        // parcel's own projected reference and expects WGS 84 longitude/latitude back -- exactly the
        // projected-to-geographic direction Reverse(transform) provides (see
        // HorizontalCoordinateTransforms.Reverse's own orientation-contract remarks). Passing the un-reversed
        // transform here would instead apply Create's fixed geographic-to-projected Forward to an
        // already-projected coordinate.
        NormalizedAoi normalized = AoiNormalizer.Normalize(projectedAoi, parcelToWgs84: HorizontalCoordinateTransforms.Reverse(transform));

        // Assert: this succeeded at all (no AoiNormalizationException propagated out of Normalize), the fetch
        // envelope contains every original geographic parcel vertex, ...
        Assert.Equal(FetchEnvelopeBasis.TransformedParcelEnvelope, normalized.Basis);
        foreach (Coordinate2D vertex in wgs84Parcel.Polygons.Single().Shell)
        {
            Assert.True(
                vertex.X >= normalized.FetchEnvelope.WestLongitude && vertex.X <= normalized.FetchEnvelope.EastLongitude,
                $"Vertex longitude {vertex.X} was outside the fetch envelope longitude range [{normalized.FetchEnvelope.WestLongitude}, {normalized.FetchEnvelope.EastLongitude}].");
            Assert.True(
                vertex.Y >= normalized.FetchEnvelope.SouthLatitude && vertex.Y <= normalized.FetchEnvelope.NorthLatitude,
                $"Vertex latitude {vertex.Y} was outside the fetch envelope latitude range [{normalized.FetchEnvelope.SouthLatitude}, {normalized.FetchEnvelope.NorthLatitude}].");
        }

        // ... and is not absurdly large: each side under 0.01 degrees for this ~[withheld] m^2 lot [withheld] plus the 2 m buffer used above.
        double widthDegrees = normalized.FetchEnvelope.EastLongitude - normalized.FetchEnvelope.WestLongitude;
        double heightDegrees = normalized.FetchEnvelope.NorthLatitude - normalized.FetchEnvelope.SouthLatitude;
        Assert.True(widthDegrees < 0.01d, $"Fetch envelope width {widthDegrees} deg was not under 0.01 deg.");
        Assert.True(heightDegrees < 0.01d, $"Fetch envelope height {heightDegrees} deg was not under 0.01 deg.");
    }

    // No negative "un-reversed transform" case is asserted here: AoiNormalizer.NormalizeProjectedParcel
    // (src/SolidGround.Core/Aois/AoiNormalizer.cs) never compares parcelToWgs84.Definition.SourceReference or
    // TargetReference against parcelAoi.HorizontalReference -- it only null-checks parcelToWgs84 and then
    // calls .Forward on every vertex, trusting the caller to supply the correctly oriented transform. Confirmed
    // with a throwaway spike (not committed): feeding the un-reversed transform's .Forward the fixture's own
    // UTM point ([withheld], [withheld]) does not throw at the ProjNET layer at all -- it silently returns
    // a finite but nonsensical pseudo-coordinate (X=-174844493.76014572, Y=156563873.98870513, observed against
    // the pinned ProjNET 2.1.0 package), which is not a validation failure so much as ProjNET's Transverse
    // Mercator series evaluating at a degrees-as-if-lon/lat input many orders of magnitude outside its normal
    // domain. Piped into NormalizeProjectedParcel with a nonzero buffer/margin, min/max longitude/latitude over
    // vertices like that would then trip Wgs84Ellipsoid.MetersPerDegreeLongitude/Latitude's own latitude-range
    // check (called while padding the envelope) and throw ArgumentOutOfRangeException -- an incidental side
    // effect of unrelated range validation elsewhere, not a deliberate check by this seam or by the reversal
    // API. Because that outcome depends on ProjNET's exact numeric aliasing for out-of-contract input rather
    // than on any contract this issue owns, asserting it here would encode an implementation detail, not a
    // guarantee -- so it is omitted rather than pinned to a specific exception type.

    private static IHorizontalCoordinateTransform CreateFixtureTransform() =>
        ProjNetHorizontalCoordinateTransformFactory.Create(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, ReadFixture("example-site-synthetic.prj"));

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
