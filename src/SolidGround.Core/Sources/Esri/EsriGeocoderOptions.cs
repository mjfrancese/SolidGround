namespace SolidGround.Core.Sources.Esri;

/// <summary>Configurable limits and endpoint for <see cref="EsriGeocoder"/>. Every property validates on both construction and <c>with</c>-expression mutation.</summary>
public sealed record EsriGeocoderOptions
{
    /// <summary>
    /// Esri's standard <c>findAddressCandidates</c> endpoint -- Esri's own recommended default; the
    /// "enhanced" <c>geocode.arcgis.com</c> variant is for FedRAMP/HIPAA/job-request needs SolidGround does
    /// not have. Verified against Esri's World Geocoding Service documentation, retrieved 2026-09-25.
    /// </summary>
    public static readonly Uri DefaultEndpointUri = new(
        "https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates", UriKind.Absolute);

    /// <summary>
    /// SolidGround only needs a handful of ranked candidates for a human to confirm, not Esri's documented
    /// default/maximum of 50 -- <see cref="MaximumCandidates"/> directly controls the billed quantity under
    /// <c>forStorage=true</c>.
    /// </summary>
    public const int DefaultMaximumCandidates = 5;

    /// <summary>Esri's documented maximum for a single <c>findAddressCandidates</c> request ("up to 50 candidates").</summary>
    public const int MaximumAllowedCandidates = 50;

    private readonly Uri endpointUri = DefaultEndpointUri;
    private readonly int maximumCandidates = DefaultMaximumCandidates;

    /// <summary>The findAddressCandidates endpoint to call. Defaults to <see cref="DefaultEndpointUri"/>.</summary>
    public Uri EndpointUri
    {
        get => endpointUri;
        init => endpointUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The <c>maxLocations</c> query parameter value. Must be between 1 and <see cref="MaximumAllowedCandidates"/>. Defaults to <see cref="DefaultMaximumCandidates"/>.</summary>
    public int MaximumCandidates
    {
        get => maximumCandidates;
        init
        {
            if (value is < 1 or > MaximumAllowedCandidates)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum candidates must be between 1 and 50.");
            }

            maximumCandidates = value;
        }
    }
}
