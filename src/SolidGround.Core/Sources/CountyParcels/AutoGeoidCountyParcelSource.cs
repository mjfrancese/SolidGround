using SolidGround.Core.Sources.Census;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>
/// Wraps <see cref="CountyParcelRegistrySource"/> with the same automatic-GEOID behavior
/// <c>SolidGround.Cli.Commands.ParcelCommand</c> already gives the CLI's own <c>parcel</c> command: a
/// configured <paramref name="geoidOverride"/> wins when non-blank; otherwise the GEOID is resolved from a
/// <see cref="ParcelPointQuery"/>'s own coordinates via <see cref="CensusCountyLookup.FindCountyGeoidAsync"/>.
/// Exists for a caller -- <c>SolidGround.Revit</c>'s interactive dialog (SolidGround Issue #31, PH3-4) is the
/// first -- that must construct one <see cref="IParcelBoundarySource"/> before the first query's own
/// coordinates are known (unlike the CLI's own <c>ParcelCommand</c>, which already has the resolved point in
/// hand before it ever constructs a source). See
/// docs/architecture/revit-interactive-dialog.md's "Settings interaction: prefill, not override".
/// </summary>
public sealed class AutoGeoidCountyParcelSource : IParcelBoundarySource
{
    private readonly HttpClient httpClient;
    private readonly CountyParcelRegistry registry;
    private readonly string? geoidOverride;
    private readonly CensusCountyLookup countyLookup;

    /// <summary>
    /// The last WGS 84 point this instance resolved a GEOID for with no configured override, and the GEOID it
    /// resolved -- reused verbatim the next time <see cref="FindAsync"/> is asked about the exact same point,
    /// instead of calling <see cref="CensusCountyLookup.FindCountyGeoidAsync"/> again. Exists because the
    /// nearby-parcel fallback tier (<see cref="NearbyParcelBoundaryFinder"/>) calls this type's own
    /// <see cref="FindAsync"/> twice in a row for the identical coordinates whenever tier 1 misses -- once for
    /// a <see cref="ParcelPointQuery"/>, once for a <see cref="ParcelNearbyQuery"/> -- and without this, that
    /// miss-then-fallback path would cost Census two network round trips for one point instead of one. See
    /// docs/architecture/parcel-boundary-sources.md's "Per-source implementation". Single-slot and
    /// instance-scoped: this type's only production caller (<c>SolidGround.Revit</c>'s interactive dialog)
    /// drives one instance sequentially, never concurrently, so an exact-equality miss for a genuinely
    /// different point simply falls through and re-resolves normally.
    /// </summary>
    private (double Latitude, double Longitude, string Geoid)? lastResolvedGeoid;

    /// <summary>This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public AutoGeoidCountyParcelSource(HttpClient httpClient, CountyParcelRegistry registry, string? geoidOverride)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.geoidOverride = string.IsNullOrWhiteSpace(geoidOverride) ? null : geoidOverride;
        countyLookup = new CensusCountyLookup(httpClient);
    }

    /// <exception cref="AutoGeoidCountyParcelSourceException">
    /// No GEOID override is configured and <paramref name="query"/> is neither a <see cref="ParcelPointQuery"/>
    /// nor a <see cref="ParcelNearbyQuery"/>; or the underlying <see cref="CensusCountyLookup.FindCountyGeoidAsync"/>
    /// call raised a <see cref="CensusCountyLookupException"/> (wrapped, never rethrown raw).
    /// </exception>
    /// <exception cref="ParcelBoundarySourceException">
    /// Propagated unchanged from the underlying <see cref="CountyParcelRegistrySource.FindAsync"/> call once a
    /// GEOID is known.
    /// </exception>
    public async ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Both query shapes that carry a WGS 84 point -- ParcelPointQuery (tier 1) and ParcelNearbyQuery (the
        // nearby-parcel fallback tier's own tier 2, SolidGround Issue #31 follow-up) -- resolve a GEOID from
        // the identical coordinates the same way; ParcelAddressQuery carries no coordinates at all.
        (double Latitude, double Longitude)? point = query switch
        {
            ParcelPointQuery pointQuery => (pointQuery.Latitude, pointQuery.Longitude),
            ParcelNearbyQuery nearbyQuery => (nearbyQuery.Latitude, nearbyQuery.Longitude),
            _ => null,
        };

        string geoid;
        if (geoidOverride is { } configured)
        {
            geoid = configured;
        }
        else if (point is { } coordinates)
        {
            if (lastResolvedGeoid is { } cached && cached.Latitude == coordinates.Latitude && cached.Longitude == coordinates.Longitude)
            {
                // Exact double equality is safe here specifically because NearbyParcelBoundaryFinder passes
                // its own identical, unmutated latitude/longitude locals into both the tier-1 ParcelPointQuery
                // and the tier-2 ParcelNearbyQuery -- never a recomputed or rounded value.
                geoid = cached.Geoid;
            }
            else
            {
                try
                {
                    geoid = await countyLookup.FindCountyGeoidAsync(coordinates.Latitude, coordinates.Longitude, cancellationToken).ConfigureAwait(false);
                }
                catch (CensusCountyLookupException ex)
                {
                    // Deliberately does not catch OperationCanceledException: the caller's own token firing during
                    // FindCountyGeoidAsync must still propagate raw, unwrapped -- exactly as CensusCountyLookup's
                    // own SendGetAsync already rethrows it -- so a caller such as SolidGroundDialogViewModel that
                    // specifically catches OperationCanceledException for a timeout still observes one.
                    throw new AutoGeoidCountyParcelSourceException(
                        $"Could not automatically resolve a county GEOID for this point: {ex.Message}", ex);
                }

                lastResolvedGeoid = (coordinates.Latitude, coordinates.Longitude, geoid);
            }
        }
        else
        {
            throw new AutoGeoidCountyParcelSourceException(
                "No countyGeoidOverride is configured, and this query carries no point coordinates, so SolidGround cannot automatically resolve a county GEOID for it.");
        }

        return await new CountyParcelRegistrySource(httpClient, registry, geoid).FindAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
