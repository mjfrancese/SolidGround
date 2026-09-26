namespace SolidGround.Core.Sources.Census;

/// <summary>Configurable limits and endpoint for <see cref="CensusGeocoder"/>. Every property validates on both construction and <c>with</c>-expression mutation.</summary>
public sealed record CensusGeocoderOptions
{
    /// <summary>
    /// The <c>locations/onelineaddress</c> endpoint -- not <c>geographies/onelineaddress</c>, which needs an
    /// extra required <c>vintage</c> parameter and returns a geography-layer payload this provider does not
    /// need. Verified against the Census Geocoder API documentation, retrieved 2026-09-25.
    /// </summary>
    public static readonly Uri DefaultEndpointUri = new("https://geocoding.geo.census.gov/geocoder/locations/onelineaddress", UriKind.Absolute);

    /// <summary>
    /// The stable benchmark name. The Census Geocoder's own <c>/geocoder/benchmarks</c> response has been
    /// observed to mark two different benchmarks <c>"isDefault":true</c> simultaneously, so "the default" is
    /// not a reliable choice; this pins the named value explicitly.
    /// </summary>
    public const string DefaultBenchmark = "Public_AR_Current";

    /// <summary>
    /// The Census Geocoder's documented address length limit: its 400 response for a missing/blank address
    /// states "Address cannot be empty and cannot exceed 100 characters". This provider enforces that figure
    /// client-side, before sending, by direct analogy to that message's own wording.
    /// </summary>
    public const int DefaultMaximumAddressLength = 100;

    private readonly Uri endpointUri = DefaultEndpointUri;
    private readonly string benchmark = DefaultBenchmark;
    private readonly int maximumAddressLength = DefaultMaximumAddressLength;

    /// <summary>The onelineaddress endpoint to call. Defaults to <see cref="DefaultEndpointUri"/>.</summary>
    public Uri EndpointUri
    {
        get => endpointUri;
        init => endpointUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The <c>benchmark</c> query parameter value. Defaults to <see cref="DefaultBenchmark"/>. Cannot be blank.</summary>
    public string Benchmark
    {
        get => benchmark;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            benchmark = value;
        }
    }

    /// <summary>The maximum address length accepted before a request is sent. Must be positive. Defaults to <see cref="DefaultMaximumAddressLength"/>.</summary>
    public int MaximumAddressLength
    {
        get => maximumAddressLength;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum address length must be positive.");
            }

            maximumAddressLength = value;
        }
    }
}
