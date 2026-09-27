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

    /// <summary>This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public AutoGeoidCountyParcelSource(HttpClient httpClient, CountyParcelRegistry registry, string? geoidOverride)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.geoidOverride = string.IsNullOrWhiteSpace(geoidOverride) ? null : geoidOverride;
        countyLookup = new CensusCountyLookup(httpClient);
    }

    /// <exception cref="AutoGeoidCountyParcelSourceException">
    /// No GEOID override is configured and <paramref name="query"/> is not a <see cref="ParcelPointQuery"/>; or
    /// the underlying <see cref="CensusCountyLookup.FindCountyGeoidAsync"/> call raised a
    /// <see cref="CensusCountyLookupException"/> (wrapped, never rethrown raw).
    /// </exception>
    /// <exception cref="ParcelBoundarySourceException">
    /// Propagated unchanged from the underlying <see cref="CountyParcelRegistrySource.FindAsync"/> call once a
    /// GEOID is known.
    /// </exception>
    public async ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string geoid;
        if (geoidOverride is { } configured)
        {
            geoid = configured;
        }
        else if (query is ParcelPointQuery pointQuery)
        {
            try
            {
                geoid = await countyLookup.FindCountyGeoidAsync(pointQuery.Latitude, pointQuery.Longitude, cancellationToken).ConfigureAwait(false);
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
        }
        else
        {
            throw new AutoGeoidCountyParcelSourceException(
                "No countyGeoidOverride is configured, and this query is not a point query, so SolidGround cannot automatically resolve a county GEOID for it.");
        }

        return await new CountyParcelRegistrySource(httpClient, registry, geoid).FindAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
