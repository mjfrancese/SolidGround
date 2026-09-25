using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// <see cref="AoiNormalizationOptions.MinimumFetchEnvelopeSide"/> (SolidGround Issue #23): every basis widens a
/// too-small fetch envelope, symmetrically about its own centre, to exactly the configured minimum on any axis
/// that falls short, using the same conservative meters-per-degree factors <c>PadAroundLatitudes</c> already
/// uses (longitude at the extreme latitude, latitude at the equator — see
/// docs/architecture/aoi-normalization-and-clipping.md's "Minimum fetch envelope, verified 2026-09-20"
/// section). Never touches clip geometry: only <see cref="NormalizedAoi.FetchEnvelope"/> and
/// <see cref="NormalizedAoi.MinimumSideExpansion"/> are affected.
/// </summary>
public sealed class AoiNormalizerMinimumFetchEnvelopeSideTests
{
    private const double Latitude = 41.591194d;
    private const double Longitude = -93.603806d;
    private static readonly LinearDistance DefaultMinimum = LinearDistance.Meters(110d);

    [Fact]
    public void BoundingBoxNarrowerThanTheMinimumOnBothAxesExpandsSymmetricallyToExactlyTheMinimum()
    {
        Wgs84BoundingBoxAoi bbox = TinyBoxAround(Longitude, Latitude, halfSideMeters: 5d);

        NormalizedAoi result = AoiNormalizer.Normalize(bbox);

        AssertExpandedToExactlyTheMinimum(result, bbox.WestLongitude, bbox.EastLongitude, bbox.SouthLatitude, bbox.NorthLatitude);
    }

    [Fact]
    public void RadiusNarrowerThanTheMinimumExpandsSymmetricallyToExactlyTheMinimum()
    {
        Wgs84RadiusAoi radiusAoi = new(Latitude, Longitude, LinearDistance.Meters(1d));

        NormalizedAoi result = AoiNormalizer.Normalize(radiusAoi);

        // The pre-expansion envelope is the radius padded around the center at the center's own latitude
        // (NormalizeRadius's own factors), not the bbox/geographic-parcel conservative factors -- but
        // AssertExpandedToExactlyTheMinimum only needs the pre-expansion bounds, which it re-derives from the
        // recorded WidthBefore/HeightBefore rather than recomputing NormalizeRadius's own padding.
        Assert.True(result.MinimumSideExpansion.Applied);
        Assert.Equal(DefaultMinimum, result.MinimumSideExpansion.MinimumSide);
        AssertSymmetricAboutCenter(Longitude, Latitude, result);
        AssertAfterSidesEqualTheMinimum(result);
    }

    [Fact]
    public void GeographicParcelNarrowerThanTheMinimumExpandsSymmetricallyToExactlyTheMinimum()
    {
        double halfSideDegreesLon = 5d / Wgs84Ellipsoid.MetersPerDegreeLongitude(Latitude);
        double halfSideDegreesLat = 5d / Wgs84Ellipsoid.MetersPerDegreeLatitude(Latitude);
        string wkt =
            $"POLYGON ((" +
            $"{Longitude - halfSideDegreesLon} {Latitude - halfSideDegreesLat}, " +
            $"{Longitude + halfSideDegreesLon} {Latitude - halfSideDegreesLat}, " +
            $"{Longitude + halfSideDegreesLon} {Latitude + halfSideDegreesLat}, " +
            $"{Longitude - halfSideDegreesLon} {Latitude + halfSideDegreesLat}, " +
            $"{Longitude - halfSideDegreesLon} {Latitude - halfSideDegreesLat}))";
        ParcelGeometryAoi parcelAoi = new(ParcelGeometryFormat.Wkt, wkt, GeographicReference());

        NormalizedAoi result = AoiNormalizer.Normalize(parcelAoi);

        Assert.Equal(FetchEnvelopeBasis.GeographicParcelEnvelope, result.Basis);
        AssertExpandedToExactlyTheMinimum(
            result, Longitude - halfSideDegreesLon, Longitude + halfSideDegreesLon, Latitude - halfSideDegreesLat, Latitude + halfSideDegreesLat);
    }

    [Fact]
    public void ProjectedParcelNarrowerThanTheMinimumExpandsSymmetricallyToExactlyTheMinimum()
    {
        // The same fixed mapping AoiNormalizerTests uses for its own projected-parcel test: 10 projected
        // units become 0.0001 degrees, so this parcel's transformed WGS 84 envelope (well under a meter) is
        // far below the default 110 m minimum on both axes.
        ParcelGeometryAoi parcelAoi = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
            ProjectedReference());
        RecordingHorizontalTransform transform = new(source => new Coordinate2D((source.X / 100000d) - 90d, (source.Y / 100000d) + 38d));

        NormalizedAoi result = AoiNormalizer.Normalize(parcelAoi, parcelToWgs84: transform);

        Assert.Equal(FetchEnvelopeBasis.TransformedParcelEnvelope, result.Basis);
        Assert.True(result.MinimumSideExpansion.Applied);
        AssertAfterSidesEqualTheMinimum(result);
    }

    [Fact]
    public void ABoundingBoxAlreadyAtOrAboveTheMinimumOnBothAxesIsUnchanged()
    {
        // ~121.8 m x 154.8 m, the same fixed CLI test box (CliFetchAndRunCommandTests.Bbox): already at or
        // above the 110 m minimum on both axes, so normalizing it must be a no-op.
        Wgs84BoundingBoxAoi bbox = new(-93.6045d, 41.5906d, -93.6031d, 41.5917d);

        NormalizedAoi result = AoiNormalizer.Normalize(bbox);

        Assert.Equal(bbox, result.FetchEnvelope);
        Assert.False(result.MinimumSideExpansion.Applied);
        Assert.Equal(result.MinimumSideExpansion.WidthBefore, result.MinimumSideExpansion.WidthAfter);
        Assert.Equal(result.MinimumSideExpansion.HeightBefore, result.MinimumSideExpansion.HeightAfter);
        Assert.Equal(DefaultMinimum, result.MinimumSideExpansion.MinimumSide);
    }

    [Fact]
    public void ZeroMinimumFetchEnvelopeSideDisablesExpansionEvenForATinyEnvelope()
    {
        Wgs84BoundingBoxAoi bbox = TinyBoxAround(Longitude, Latitude, halfSideMeters: 5d);
        AoiNormalizationOptions options = new() { MinimumFetchEnvelopeSide = LinearDistance.Zero };

        NormalizedAoi result = AoiNormalizer.Normalize(bbox, options);

        Assert.Equal(bbox, result.FetchEnvelope);
        Assert.False(result.MinimumSideExpansion.Applied);
        Assert.Equal(LinearDistance.Zero, result.MinimumSideExpansion.MinimumSide);
    }

    [Fact]
    public void AoiNormalizationOptionsRejectsANegativeMinimumFetchEnvelopeSide()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AoiNormalizationOptions { MinimumFetchEnvelopeSide = new LinearDistance(-1d, LengthUnit.Meter) });
    }

    [Fact]
    public void MinimumSideExpansionNearThePoleStillTriggersTheAntimeridianAndPoleGuard()
    {
        // Valid on its own (no margin is applied, so the un-expanded envelope never needs the guard at all)
        // but far too small on both axes at this extreme latitude for AoiNormalizer to widen to the default
        // 110 m minimum without leaving the supported range -- proving the new expansion step reuses the
        // identical guard PadAroundLatitudes already enforces, rather than skipping validation for itself.
        Wgs84BoundingBoxAoi bbox = new(-93.6042d, 89.9994d, -93.6032d, 89.9998d);

        AoiNormalizationException exception = Assert.Throws<AoiNormalizationException>(() => AoiNormalizer.Normalize(bbox));
        Assert.Contains("pole", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizingTheSameAoiTwiceProducesAnIdenticalFetchEnvelopeAndExpansion()
    {
        Wgs84BoundingBoxAoi bbox = TinyBoxAround(Longitude, Latitude, halfSideMeters: 5d);

        NormalizedAoi first = AoiNormalizer.Normalize(bbox);
        NormalizedAoi second = AoiNormalizer.Normalize(bbox);

        Assert.Equal(first.FetchEnvelope, second.FetchEnvelope);
        Assert.Equal(first.MinimumSideExpansion, second.MinimumSideExpansion);
    }

    // ---- shared assertions and helpers -----------------------------------------------------------------

    private static Wgs84BoundingBoxAoi TinyBoxAround(double longitude, double latitude, double halfSideMeters)
    {
        double halfWidthDegrees = halfSideMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude);
        double halfHeightDegrees = halfSideMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);
        return new Wgs84BoundingBoxAoi(longitude - halfWidthDegrees, latitude - halfHeightDegrees, longitude + halfWidthDegrees, latitude + halfHeightDegrees);
    }

    /// <summary>
    /// Asserts that a too-small envelope was expanded symmetrically about its own original center to exactly
    /// <see cref="DefaultMinimum"/> on both axes, using AoiNormalizer's own documented factors (longitude at
    /// the extreme original latitude, latitude at the equator) so this check is independent of, but consistent
    /// with, the production formula.
    /// </summary>
    private static void AssertExpandedToExactlyTheMinimum(
        NormalizedAoi result, double originalWest, double originalEast, double originalSouth, double originalNorth)
    {
        Assert.True(result.MinimumSideExpansion.Applied);
        Assert.Equal(DefaultMinimum, result.MinimumSideExpansion.MinimumSide);

        double originalCenterLongitude = (originalWest + originalEast) / 2d;
        double originalCenterLatitude = (originalSouth + originalNorth) / 2d;
        AssertSymmetricAboutCenter(originalCenterLongitude, originalCenterLatitude, result);

        double latitudeForLongitudeFactor = Math.Abs(originalSouth) >= Math.Abs(originalNorth) ? originalSouth : originalNorth;
        double metersPerDegreeLongitude = Wgs84Ellipsoid.MetersPerDegreeLongitude(latitudeForLongitudeFactor);
        double metersPerDegreeLatitude = Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);

        double widthAfterMeters = (result.FetchEnvelope.EastLongitude - result.FetchEnvelope.WestLongitude) * metersPerDegreeLongitude;
        double heightAfterMeters = (result.FetchEnvelope.NorthLatitude - result.FetchEnvelope.SouthLatitude) * metersPerDegreeLatitude;
        Assert.Equal(110d, widthAfterMeters, 6);
        Assert.Equal(110d, heightAfterMeters, 6);

        AssertAfterSidesEqualTheMinimum(result);
    }

    private static void AssertSymmetricAboutCenter(double originalCenterLongitude, double originalCenterLatitude, NormalizedAoi result)
    {
        double resultCenterLongitude = (result.FetchEnvelope.WestLongitude + result.FetchEnvelope.EastLongitude) / 2d;
        double resultCenterLatitude = (result.FetchEnvelope.SouthLatitude + result.FetchEnvelope.NorthLatitude) / 2d;
        Assert.Equal(originalCenterLongitude, resultCenterLongitude, 9);
        Assert.Equal(originalCenterLatitude, resultCenterLatitude, 9);
    }

    private static void AssertAfterSidesEqualTheMinimum(NormalizedAoi result)
    {
        Assert.Equal(110d, result.MinimumSideExpansion.WidthAfter.ToMeters(), 6);
        Assert.Equal(110d, result.MinimumSideExpansion.HeightAfter.ToMeters(), 6);
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    /// <summary>A forward-only test double, duplicated from AoiNormalizerTests's own private type (that one is private to its class).</summary>
    private sealed class RecordingHorizontalTransform : IHorizontalCoordinateTransform
    {
        private readonly Func<Coordinate2D, Coordinate2D> forward;

        public RecordingHorizontalTransform(Func<Coordinate2D, Coordinate2D> forward)
        {
            this.forward = forward;
            Definition = new HorizontalTransformationDefinition(
                new HorizontalReference("EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing),
                new HorizontalReference("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude),
                new CoordinateOperationDefinition("PROJJSON", "forward operation"),
                new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
                "test-double-engine",
                "1.0");
        }

        public HorizontalTransformationDefinition Definition { get; }

        public Coordinate2D Forward(Coordinate2D source) => forward(source);

        public Coordinate2D Inverse(Coordinate2D target) => throw new NotSupportedException("This test double is forward-only.");
    }
}
