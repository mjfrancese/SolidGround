using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Configurable limits and endpoint for <see cref="OpenTopographyUsgs1mSource"/>. Every property validates
/// on both construction and <c>with</c>-expression mutation.
/// </summary>
public sealed record OpenTopographyUsgs1mSourceOptions
{
    /// <summary>The usgsdem endpoint verified against OpenTopography's OpenAPI definition on 2026-09-16.</summary>
    public static readonly Uri DefaultEndpointUri = new("https://portal.opentopography.org/API/usgsdem");

    /// <summary>OpenTopography's documented USGS1m per-request area limit, in square kilometers.</summary>
    public const double DefaultMaximumAreaSquareKilometers = 250d;

    /// <summary>A conservative response size cap. OpenTopography does not document a maximum response size.</summary>
    public const long DefaultMaximumResponseBytes = 256L * 1024 * 1024;

    private readonly Uri endpointUri = DefaultEndpointUri;
    private readonly double maximumAreaSquareKilometers = DefaultMaximumAreaSquareKilometers;
    private readonly long maximumResponseBytes = DefaultMaximumResponseBytes;
    private readonly VerticalReference declaredVerticalReference = new("NAVD88", LengthUnit.Meter);

    /// <summary>The usgsdem endpoint to call. Defaults to <see cref="DefaultEndpointUri"/>.</summary>
    public Uri EndpointUri
    {
        get => endpointUri;
        init => endpointUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The maximum approximate AOI area, in square kilometers, accepted before a request is sent. Must be
    /// finite and positive. Defaults to <see cref="DefaultMaximumAreaSquareKilometers"/>, OpenTopography's
    /// documented USGS1m per-request limit.
    /// </summary>
    public double MaximumAreaSquareKilometers
    {
        get => maximumAreaSquareKilometers;
        init
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum area must be finite and positive.");
            }

            maximumAreaSquareKilometers = value;
        }
    }

    /// <summary>
    /// The maximum response byte count read before a response is treated as truncated. Must be positive.
    /// Defaults to <see cref="DefaultMaximumResponseBytes"/>.
    /// </summary>
    /// <remarks>
    /// Exceeding this on a <b>200 (OK)</b> response, or a response with an <b>undocumented status code</b>,
    /// fails with <see cref="OpenTopographyUnexpectedResponseException"/>. Exceeding it on a <b>documented
    /// non-200 status</b> (401, 403, 429, 400, or 5xx) does not change which exception type is thrown: the
    /// status code is still classified exactly as it would be for a complete body, and a truncation note is
    /// appended to that exception's <c>ServerMessage</c> instead. See
    /// <c>docs/architecture/opentopography-usgs1m-source.md</c>'s "Response body truncation" section.
    /// </remarks>
    public long MaximumResponseBytes
    {
        get => maximumResponseBytes;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum response size must be positive.");
            }

            maximumResponseBytes = value;
        }
    }

    /// <summary>
    /// The vertical reference OpenTopography's USGS 1 m dataset is declared to use. Neither the AAIGrid
    /// data response nor the GeoTIFF metadata response OpenTopography returns for a USGS 1 m request
    /// carries a vertical reference, so SolidGround declares one from the dataset's own published
    /// documentation instead of assuming or omitting it; see
    /// <c>docs/architecture/opentopography-usgs1m-source.md</c>'s "Declared vertical reference" section.
    /// Defaults to NAVD88 height in metres.
    /// </summary>
    public VerticalReference DeclaredVerticalReference
    {
        get => declaredVerticalReference;
        init => declaredVerticalReference = value ?? throw new ArgumentNullException(nameof(value));
    }
}
