using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.LocalParcelFile;

namespace SolidGround.Tests;

/// <summary>
/// Contract tests for <see cref="NearbyParcelBoundaryFinder"/>'s own two-tier orchestration: tier 1 (the
/// existing exact point-in-parcel <see cref="ParcelPointQuery"/>) always runs first; tier 2 (a
/// <see cref="ParcelNearbyQuery"/>) only ever runs when tier 1 returns zero candidates. Most cases use a
/// <see cref="FakeParcelBoundarySource"/> test double so the exact candidates/distances/ordering are fully
/// controlled and the tier-routing itself (which query was actually sent, how many times) is directly
/// observable; one end-to-end case uses the real <see cref="LocalParcelFileSource"/> against the committed
/// synthetic fixture to prove the real geometry path resolves too. See
/// docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier".
/// </summary>
public sealed class NearbyParcelBoundaryFinderTests
{
    private const double QueryLatitude = 41.5910d;
    private const double QueryLongitude = -93.6040d;

    [Fact]
    public async Task AnExactHitNeverTriggersTheNearbyTier()
    {
        ParcelBoundaryCandidate containing = BuildCandidate(SquareWkt(QueryLatitude, QueryLongitude, 0.0005d), "SYNTHETIC-CONTAINING");
        var source = new FakeParcelBoundarySource(exactResult: [containing], nearbyResult: [containing]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 30d, TestContext.Current.CancellationToken);

        Assert.Single(source.Queries);
        Assert.IsType<ParcelPointQuery>(source.Queries[0]);
        Assert.False(acquisition.UsedNearbyTier);
        ParcelProximityCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Equal(0d, candidate.DistanceMeters);
        Assert.Same(containing, candidate.Candidate);
    }

    [Fact]
    public async Task AZeroExactResultFallsBackToTheNearbyTierAndReturnsItsCandidateWithADistanceAndTheFlagSet()
    {
        // North square offset by 0.0005 deg from the query point's own latitude: not containing (tier 1 is
        // empty), but well within the passed 60 m radius (~50 m away at this latitude).
        ParcelBoundaryCandidate nearby = BuildCandidate(SquareWkt(QueryLatitude + 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-NEARBY");
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [nearby]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 60d, TestContext.Current.CancellationToken);

        Assert.Equal(2, source.Queries.Count);
        Assert.IsType<ParcelPointQuery>(source.Queries[0]);
        ParcelNearbyQuery nearbyQuery = Assert.IsType<ParcelNearbyQuery>(source.Queries[1]);
        Assert.Equal(QueryLatitude, nearbyQuery.Latitude);
        Assert.Equal(QueryLongitude, nearbyQuery.Longitude);
        Assert.Equal(60d, nearbyQuery.RadiusMeters);

        Assert.True(acquisition.UsedNearbyTier);
        ParcelProximityCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Same(nearby, candidate.Candidate);
        Assert.True(candidate.DistanceMeters > 0d, "A nearby (non-containing) candidate must have a positive distance.");
        Assert.InRange(candidate.DistanceMeters, 40d, 60d);
    }

    [Fact]
    public async Task ACandidateFartherThanTheRadiusIsDroppedLeavingZeroCandidatesButTheFlagStillSet()
    {
        ParcelBoundaryCandidate tooFar = BuildCandidate(SquareWkt(QueryLatitude + 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-TOO-FAR");
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [tooFar]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 10d, TestContext.Current.CancellationToken);

        Assert.True(acquisition.UsedNearbyTier);
        Assert.Empty(acquisition.Candidates);
    }

    [Fact]
    public async Task TwoOrMoreNearbyCandidatesAreOrderedByIncreasingDistance()
    {
        ParcelBoundaryCandidate near = BuildCandidate(SquareWkt(QueryLatitude + 0.0002d, QueryLongitude, 0.00002d), "SYNTHETIC-NEAR");
        ParcelBoundaryCandidate far = BuildCandidate(SquareWkt(QueryLatitude + 0.0008d, QueryLongitude, 0.00002d), "SYNTHETIC-FAR");
        // Deliberately returned "far, near" -- the finder must re-order by true distance, not merely preserve
        // whatever order the source itself returned when the distances actually differ.
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [far, near]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 200d, TestContext.Current.CancellationToken);

        Assert.Equal(2, acquisition.Candidates.Count);
        Assert.Same(near, acquisition.Candidates[0].Candidate);
        Assert.Same(far, acquisition.Candidates[1].Candidate);
        Assert.True(acquisition.Candidates[0].DistanceMeters < acquisition.Candidates[1].DistanceMeters);
    }

    [Fact]
    public async Task TiedDistancesAreBrokenStablyByTheSourcesOwnReturnedOrder()
    {
        // South and north squares equidistant from the query point (same |dy|, dx = 0 for both) -- an exact,
        // not merely approximate, tie: both candidates' computed distance uses the identical
        // Wgs84Ellipsoid.MetersPerDegreeLatitude(QueryLatitude) factor against the identical magnitude offset.
        ParcelBoundaryCandidate south = BuildCandidate(SquareWkt(QueryLatitude - 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-SOUTH");
        ParcelBoundaryCandidate north = BuildCandidate(SquareWkt(QueryLatitude + 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-NORTH");
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [south, north]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 60d, TestContext.Current.CancellationToken);

        Assert.Equal(2, acquisition.Candidates.Count);
        Assert.Equal(acquisition.Candidates[0].DistanceMeters, acquisition.Candidates[1].DistanceMeters);
        // Stable sort: south (the source's own first-returned candidate) stays first among the tied pair.
        Assert.Same(south, acquisition.Candidates[0].Candidate);
        Assert.Same(north, acquisition.Candidates[1].Candidate);
    }

    [Fact]
    public async Task ResolvesThroughTheRealLocalParcelFileSourceWhenThePointIsAFewMetersOutsideTheFixtureParcel()
    {
        // The committed fixture's own ~40x40 m square parcel (docs/architecture/parcel-boundary-sources.md
        // "Fixtures"); a point ~2.5 m east of its east edge, within the default 30 m radius.
        var source = new LocalParcelFileSource(new LocalParcelFileOptions
        {
            Path = FixturePath(),
            SourceLabel = "Test Local File",
            LicenseDisclaimerText = "Test disclaimer.",
        });
        const double queryLatitude = 41.591194d;
        const double queryLongitude = -93.603564371d + 0.00003d; // ~2.5 m east of the parcel's own east edge

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, queryLatitude, queryLongitude, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(acquisition.UsedNearbyTier);
        ParcelProximityCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.Candidate.ParcelId);
        Assert.InRange(candidate.DistanceMeters, 2.0d, 3.0d);
    }

    [Fact]
    public async Task ANullSourceIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => NearbyParcelBoundaryFinder.FindAsync(null!, QueryLatitude, QueryLongitude, cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ANonPositiveRadiusIsRejected()
    {
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: []);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => NearbyParcelBoundaryFinder.FindAsync(source, QueryLatitude, QueryLongitude, radiusMeters: 0d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public void DefaultRadiusMetersIsTheDocumentedThirtyMeters()
    {
        Assert.Equal(30d, NearbyParcelBoundaryFinder.DefaultRadiusMeters);
    }

    // ------------------------------------------------------------------------------------------------
    // Re-check finding, minor, fixed: the nearby tier's own ResultSetTruncated signal used to be read only
    // from ParcelBoundaryAcquisition.Candidates (this class's own "nearby" local) and never inspected on the
    // acquisition itself, so a county service reporting exceededTransferLimit on the envelope query could
    // silently omit the operator's true closest parcel with no indication the shown list might be incomplete.
    // See docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier".
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANearbyTierResultSetTruncatedSignalIsForwardedToTheProximityAcquisition()
    {
        ParcelBoundaryCandidate nearby = BuildCandidate(SquareWkt(QueryLatitude + 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-NEARBY-TRUNCATED");
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [nearby], nearbyResultSetTruncated: true);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 60d, TestContext.Current.CancellationToken);

        Assert.True(acquisition.UsedNearbyTier);
        Assert.True(acquisition.ResultSetTruncated);
    }

    [Fact]
    public async Task ANonTruncatedNearbyTierResultLeavesResultSetTruncatedFalse()
    {
        ParcelBoundaryCandidate nearby = BuildCandidate(SquareWkt(QueryLatitude + 0.0005d, QueryLongitude, 0.00005d), "SYNTHETIC-NEARBY-NOT-TRUNCATED");
        var source = new FakeParcelBoundarySource(exactResult: [], nearbyResult: [nearby]);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 60d, TestContext.Current.CancellationToken);

        Assert.True(acquisition.UsedNearbyTier);
        Assert.False(acquisition.ResultSetTruncated);
    }

    [Fact]
    public async Task AnExactTierResultSetTruncatedSignalIsForwardedTooEvenThoughTheNearbyTierNeverRuns()
    {
        // Symmetry, not the primary risk this finding named (a single point-in-parcel hit is the ordinary
        // case): exact.ResultSetTruncated is forwarded through the tier-1 return path exactly like the
        // tier-2 path above, rather than only ever being read on the nearby-tier branch.
        ParcelBoundaryCandidate containing = BuildCandidate(SquareWkt(QueryLatitude, QueryLongitude, 0.0005d), "SYNTHETIC-CONTAINING-TRUNCATED");
        var source = new FakeParcelBoundarySource(exactResult: [containing], nearbyResult: [], exactResultSetTruncated: true);

        ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
            source, QueryLatitude, QueryLongitude, radiusMeters: 30d, TestContext.Current.CancellationToken);

        Assert.False(acquisition.UsedNearbyTier);
        Assert.True(acquisition.ResultSetTruncated);
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private static string SquareWkt(double centerLatitude, double centerLongitude, double halfWidthDegrees)
    {
        double minX = centerLongitude - halfWidthDegrees;
        double maxX = centerLongitude + halfWidthDegrees;
        double minY = centerLatitude - halfWidthDegrees;
        double maxY = centerLatitude + halfWidthDegrees;
        return $"POLYGON(({minX} {minY}, {maxX} {minY}, {maxX} {maxY}, {minX} {maxY}, {minX} {minY}))";
    }

    private static ParcelBoundaryCandidate BuildCandidate(string wkt, string parcelId)
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wkt, GeographicReference());
        return new ParcelBoundaryCandidate(
            boundary,
            parcelId,
            computedAreaSquareMeters: 100d,
            ParcelBoundarySourceKind.LocalParcelFile,
            sourceIdentity: "Test Source",
            licenseDisclaimerText: "Test disclaimer.");
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "local-parcel-file-standard-schema-synthetic.geojson");

    /// <summary>
    /// A fully controlled <see cref="IParcelBoundarySource"/> test double: returns a caller-scripted result for
    /// a <see cref="ParcelPointQuery"/> (tier 1) and a different one for a <see cref="ParcelNearbyQuery"/>
    /// (tier 2), and records every query it actually receives, in order. <paramref name="exactResultSetTruncated"/>/
    /// <paramref name="nearbyResultSetTruncated"/> default to <see langword="false"/> so every pre-existing call
    /// site keeps compiling unchanged.
    /// </summary>
    private sealed class FakeParcelBoundarySource(
        IReadOnlyList<ParcelBoundaryCandidate> exactResult,
        IReadOnlyList<ParcelBoundaryCandidate> nearbyResult,
        bool exactResultSetTruncated = false,
        bool nearbyResultSetTruncated = false) : IParcelBoundarySource
    {
        public List<ParcelBoundaryQuery> Queries { get; } = [];

        public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return query switch
            {
                ParcelPointQuery => ValueTask.FromResult(new ParcelBoundaryAcquisition(exactResult, exactResultSetTruncated)),
                ParcelNearbyQuery => ValueTask.FromResult(new ParcelBoundaryAcquisition(nearbyResult, nearbyResultSetTruncated)),
                _ => throw new ArgumentOutOfRangeException(nameof(query), query, "Unsupported query type for this test double."),
            };
        }
    }
}
