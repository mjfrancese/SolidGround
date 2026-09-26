namespace SolidGround.Core.Sources.Geocodio;

/// <summary>Configurable limits and endpoint for <see cref="GeocodioGeocoder"/>. Every property validates on both construction and <c>with</c>-expression mutation.</summary>
public sealed record GeocodioGeocoderOptions
{
    /// <summary>Geocodio's current v2 geocoding endpoint (v2 shipped 2026-06-05 per Geocodio's own changelog; the legacy v1.x endpoint is not used). Verified against Geocodio's API documentation, retrieved 2026-09-25.</summary>
    public static readonly Uri DefaultEndpointUri = new("https://api.geocod.io/v2/geocode", UriKind.Absolute);

    /// <summary>SolidGround needs a handful of candidates for a human to confirm, not Geocodio's documented "no limit" server default.</summary>
    public const int DefaultMaximumCandidates = 5;

    private readonly Uri endpointUri = DefaultEndpointUri;
    private readonly int maximumCandidates = DefaultMaximumCandidates;

    /// <summary>The geocode endpoint to call. Defaults to <see cref="DefaultEndpointUri"/>.</summary>
    public Uri EndpointUri
    {
        get => endpointUri;
        init => endpointUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The <c>limit</c> query parameter value. Must be positive. Defaults to <see cref="DefaultMaximumCandidates"/>.</summary>
    public int MaximumCandidates
    {
        get => maximumCandidates;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum candidates must be positive.");
            }

            maximumCandidates = value;
        }
    }
}
