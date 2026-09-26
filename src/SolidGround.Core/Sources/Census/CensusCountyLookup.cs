using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SolidGround.Core.Aois;
using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.Census;

/// <summary>Configurable endpoint, benchmark, and vintage for <see cref="CensusCountyLookup"/>. Every property validates on both construction and <c>init</c> mutation.</summary>
public sealed class CensusCountyLookupOptions
{
    public static readonly Uri DefaultEndpointUri =
        new("https://geocoding.geo.census.gov/geocoder/geographies/coordinates", UriKind.Absolute);

    /// <summary>Reuses <see cref="CensusGeocoderOptions.DefaultBenchmark"/> rather than a second literal.</summary>
    public const string DefaultVintage = "Current_Current";

    private readonly Uri endpointUri = DefaultEndpointUri;
    private readonly string benchmark = CensusGeocoderOptions.DefaultBenchmark;
    private readonly string vintage = DefaultVintage;

    public Uri EndpointUri
    {
        get => endpointUri;
        init => endpointUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Benchmark
    {
        get => benchmark;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            benchmark = value;
        }
    }

    public string Vintage
    {
        get => vintage;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            vintage = value;
        }
    }
}

/// <summary>
/// Resolves the 5-digit Census county GEOID (STATE+COUNTY, zero-padded) containing a WGS 84 point, via the
/// keyless <c>geographies/coordinates</c> endpoint with <c>layers=Counties</c> pinned explicitly. Sibling to
/// <see cref="CensusGeocoder"/>: same host, same redaction discipline, same transport-exception shape. Feeds
/// <see cref="Sources.CountyParcels.CountyParcelRegistrySource"/>'s <c>geoid</c> constructor parameter directly
/// -- the GEOID string needs no reformatting (Census's GEOID is already exactly the 5-ASCII-digit shape
/// <c>CountyParcelRegistry</c> requires). See docs/architecture/census-county-lookup.md.
/// </summary>
public sealed class CensusCountyLookup
{
    private static readonly Regex GeoidPattern = new(@"\A\d{5}\z");

    private readonly HttpClient httpClient;
    private readonly CensusCountyLookupOptions options;

    /// <summary>Creates a lookup over an externally owned <see cref="HttpClient"/>. This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public CensusCountyLookup(HttpClient httpClient, CensusCountyLookupOptions? options = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? new CensusCountyLookupOptions();
    }

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="latitude"/> or <paramref name="longitude"/> is not a finite, in-range WGS 84 coordinate. Thrown before any HTTP call.</exception>
    /// <exception cref="CensusCountyLookupNoCountyException">No county contains the point (a 200 response with an empty or Counties-less <c>geographies</c> object).</exception>
    /// <exception cref="CensusCountyLookupServerException">HTTP 5xx.</exception>
    /// <exception cref="CensusCountyLookupNetworkException">Transport-level failure or timeout.</exception>
    /// <exception cref="CensusCountyLookupUnexpectedResponseException">Any other non-200 status, or a 200 body that does not parse into the documented shape, or whose GEOID is not exactly 5 digits.</exception>
    public async ValueTask<string> FindCountyGeoidAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));

        Uri requestUri = BuildRequestUri(latitude, longitude);
        string redactedRequestUri = SensitiveQueryRedactor.RedactUri(requestUri, SensitiveQueryParameterNames.KnownFamilies);

        HttpResponseMessage response = await SendGetAsync(requestUri, redactedRequestUri, cancellationToken).ConfigureAwait(false);
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK
                ? ParseSuccess(body, redactedRequestUri)
                : throw ClassifyNonSuccessResponse(response.StatusCode, body, redactedRequestUri);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Uri BuildRequestUri(double latitude, double longitude) => new(
        $"{options.EndpointUri}?x={longitude.ToString("R", CultureInfo.InvariantCulture)}" +
        $"&y={latitude.ToString("R", CultureInfo.InvariantCulture)}" +
        $"&benchmark={Uri.EscapeDataString(options.Benchmark)}&vintage={Uri.EscapeDataString(options.Vintage)}" +
        "&layers=Counties&format=json",
        UriKind.Absolute);

    /// <summary>
    /// Sends one GET request and translates a transport-level failure into
    /// <see cref="CensusCountyLookupNetworkException"/>. Mirrors <see cref="CensusGeocoder"/>'s own
    /// <c>SendGetAsync</c> exactly, including never attaching the original caught exception as
    /// <see cref="Exception.InnerException"/> (a caller-attached <see cref="DelegatingHandler"/> could embed
    /// the request URI in its own message).
    /// </summary>
    private async ValueTask<HttpResponseMessage> SendGetAsync(Uri requestUri, string redactedRequestUri, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            Exception redactedInner = ex is TaskCanceledException
                ? new TaskCanceledException(redactedDetail)
                : new OperationCanceledException(redactedDetail);
            throw new CensusCountyLookupNetworkException(
                "The request to the Census county lookup endpoint did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusCountyLookupNetworkException(
                $"The request to the Census county lookup endpoint failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusCountyLookupNetworkException(
                $"An I/O error occurred while communicating with the Census county lookup endpoint: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    /// <summary>
    /// Every step below uses <c>TryGetProperty</c> plus an explicit <see cref="JsonValueKind"/> check, never a
    /// bare <c>GetProperty</c> call, mirroring <see cref="Sources.LocalParcelFile.LocalParcelFileSource"/>'s and
    /// <see cref="Aois.ParcelGeometryParser"/>'s own discipline, so no framework exception
    /// (<see cref="KeyNotFoundException"/>/<see cref="InvalidOperationException"/>) can escape past this
    /// method's own documented exception contract.
    /// </summary>
    private static string ParseSuccess(string body, string redactedRequestUri)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusCountyLookupUnexpectedResponseException(
                $"The Census county lookup endpoint returned a 200 response that could not be parsed as JSON: {redactedDetail}",
                redactedRequestUri,
                HttpStatusCode.OK,
                null);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("result", out JsonElement result)
                || result.ValueKind != JsonValueKind.Object)
            {
                throw new CensusCountyLookupUnexpectedResponseException(
                    "The Census county lookup endpoint's 200 response did not carry the documented result object.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (!result.TryGetProperty("geographies", out JsonElement geographies) || geographies.ValueKind != JsonValueKind.Object)
            {
                throw new CensusCountyLookupUnexpectedResponseException(
                    "The Census county lookup endpoint's 200 response did not carry the documented result.geographies object.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (!geographies.TryGetProperty("Counties", out JsonElement counties)
                || counties.ValueKind != JsonValueKind.Array
                || counties.GetArrayLength() == 0)
            {
                throw new CensusCountyLookupNoCountyException(
                    "The Census county lookup endpoint reported no county containing the requested point.",
                    redactedRequestUri);
            }

            JsonElement firstCounty = counties[0];
            if (firstCounty.ValueKind != JsonValueKind.Object
                || !firstCounty.TryGetProperty("GEOID", out JsonElement geoidElement)
                || geoidElement.ValueKind != JsonValueKind.String)
            {
                throw new CensusCountyLookupUnexpectedResponseException(
                    "The Census county lookup endpoint returned a Counties[0] entry missing its documented string GEOID field.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            string geoid = geoidElement.GetString()!;
            if (!GeoidPattern.IsMatch(geoid))
            {
                throw new CensusCountyLookupUnexpectedResponseException(
                    "The Census county lookup endpoint returned a GEOID that was not exactly 5 digits.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            return geoid;
        }
    }

    /// <summary>
    /// Collapses to two buckets: HTTP 5xx classifies as <see cref="CensusCountyLookupServerException"/>;
    /// anything else non-200 (400 included) classifies as <see cref="CensusCountyLookupUnexpectedResponseException"/>.
    /// The <c>geographies/coordinates</c> error envelope was not independently live-verified during this
    /// endpoint's own research, and there is no operator-facing "bad input" concept at this call site --
    /// latitude/longitude are already constructor-validated, and benchmark/vintage are fixed constants this
    /// type controls -- so no request-validation exception type exists here.
    /// </summary>
    private static CensusCountyLookupException ClassifyNonSuccessResponse(HttpStatusCode statusCode, string body, string redactedRequestUri)
    {
        string redactedBody = SensitiveQueryRedactor.RedactText(body, SensitiveQueryParameterNames.KnownFamilies);
        string describedServerMessage = string.IsNullOrWhiteSpace(redactedBody) ? "(no error detail returned)" : redactedBody;

        int statusCodeValue = (int)statusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            string statusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
            return new CensusCountyLookupServerException(
                $"The Census county lookup endpoint reported a server-side error (HTTP {statusText}). This is not a problem with " +
                $"the request; retry later. Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                redactedBody.Length == 0 ? null : redactedBody);
        }

        string undocumentedStatusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
        return new CensusCountyLookupUnexpectedResponseException(
            $"The Census county lookup endpoint returned an undocumented status code (HTTP {undocumentedStatusText}). Server message: {describedServerMessage}",
            redactedRequestUri,
            statusCode,
            redactedBody.Length == 0 ? null : redactedBody);
    }
}
