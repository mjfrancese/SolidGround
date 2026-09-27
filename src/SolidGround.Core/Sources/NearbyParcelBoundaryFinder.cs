using SolidGround.Core.Aois;

namespace SolidGround.Core.Sources;

/// <summary>
/// Finds the parcel(s) for a WGS 84 point in two tiers, so a geocoded point that lands a few meters outside
/// its true parcel (Census interpolates along the street centreline, so its own point commonly lands in the
/// street frontage or right-of-way) still resolves to that parcel: tier 1 is the existing exact point-in-
/// parcel query (<see cref="ParcelPointQuery"/>); only when that returns zero candidates does tier 2 run, a
/// nearby query within a search radius (<see cref="ParcelNearbyQuery"/>), ranked by increasing true distance
/// from the point to each candidate's boundary (<see cref="ParcelBoundaryProximity.DistanceMeters"/>) and
/// limited to that same radius. See docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback
/// tier" for the full design record and citations.
/// </summary>
public static class NearbyParcelBoundaryFinder
{
    /// <summary>
    /// The documented default search radius for the nearby tier, in meters. Not a survey-grade figure --
    /// chosen to comfortably cover a geocoded point's typical street-frontage/right-of-way offset from its
    /// true parcel while still being narrow enough that a distant, unrelated parcel is never offered as a
    /// candidate.
    /// </summary>
    public const double DefaultRadiusMeters = 30d;

    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="latitude"/>/<paramref name="longitude"/> is out of the WGS 84 range, or <paramref name="radiusMeters"/> is not finite and positive.</exception>
    /// <exception cref="ParcelBoundarySourceException">Propagated unchanged from either underlying <see cref="IParcelBoundarySource.FindAsync"/> call.</exception>
    public static async ValueTask<ParcelProximityAcquisition> FindAsync(
        IParcelBoundarySource source,
        double latitude,
        double longitude,
        double radiusMeters = DefaultRadiusMeters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "Search radius must be finite and positive.");
        }

        ParcelBoundaryAcquisition exact = await source.FindAsync(new ParcelPointQuery(latitude, longitude), cancellationToken).ConfigureAwait(false);
        if (exact.Candidates.Count > 0)
        {
            // Every exact-tier candidate contains (or touches) the point by construction (ParcelPointQuery's
            // own Intersects contract), so its distance is always exactly 0 -- computing it via
            // ParcelBoundaryProximity.DistanceMeters here would just re-derive the same 0 through the
            // Intersects fast path, at the cost of one more NTS call per candidate for no observable
            // difference; tier 2 never runs at all in this case, not merely "runs but its result is unused".
            List<ParcelProximityCandidate> containing = new(exact.Candidates.Count);
            foreach (ParcelBoundaryCandidate candidate in exact.Candidates)
            {
                containing.Add(new ParcelProximityCandidate(candidate, 0d));
            }

            return new ParcelProximityAcquisition(containing, usedNearbyTier: false, resultSetTruncated: exact.ResultSetTruncated);
        }

        ParcelBoundaryAcquisition nearby = await source.FindAsync(new ParcelNearbyQuery(latitude, longitude, radiusMeters), cancellationToken).ConfigureAwait(false);

        List<ParcelProximityCandidate> withinRadius = [];
        foreach (ParcelBoundaryCandidate candidate in nearby.Candidates)
        {
            double distanceMeters = ParcelBoundaryProximity.DistanceMeters(candidate.Boundary, latitude, longitude);
            if (distanceMeters <= radiusMeters)
            {
                withinRadius.Add(new ParcelProximityCandidate(candidate, distanceMeters));
            }
        }

        // OrderBy is a documented stable sort: two candidates at the identical distance keep the order the
        // source itself returned them in, rather than an arbitrary one.
        List<ParcelProximityCandidate> ordered = [.. withinRadius.OrderBy(candidate => candidate.DistanceMeters)];
        // Re-check finding, minor, fixed: nearby.ResultSetTruncated used to be read only implicitly (never at
        // all, in fact) -- this forwards it so a caller (SolidGroundDialogViewModel.NearbyTierNoticeText) can
        // tell the operator the shown list might be missing a closer match. See ParcelProximityAcquisition.ResultSetTruncated.
        return new ParcelProximityAcquisition(ordered, usedNearbyTier: true, resultSetTruncated: nearby.ResultSetTruncated);
    }
}
