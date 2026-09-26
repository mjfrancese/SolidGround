using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.Census;

/// <summary>
/// Geocodes an address through the US Census Bureau's public, keyless Geocoder
/// (<c>geocoder.locations.onelineaddress</c>). This is SolidGround's default <see cref="IAddressGeocoder"/>:
/// free, keyless, and federal-public-domain. The Census Geocoder's response schema carries no per-candidate
/// ranking score or precision label, and no attribution field of its own -- see <see cref="AttributionNotice"/>.
/// Every URI surfaced by this type outside the outgoing request is redacted through
/// <see cref="SensitiveQueryRedactor"/>, even though this provider sends no API key of its own, for
/// consistency with the other two providers and in case a caller-configured address ever happens to look
/// like a sensitive query parameter.
/// </summary>
public sealed class CensusGeocoder : IAddressGeocoder
{
    public const string ProviderName = "Census";

    /// <summary>
    /// The Census Bureau Data API Terms of Service's required attribution notice, verbatim. Source:
    /// https://www.census.gov/data/developers/about/terms-of-service.html (retrieved 2026-09-25). Attached to
    /// every candidate this provider returns, because the Census Geocoder's response schema has no
    /// per-candidate (or per-response) attribution field of its own. Whether this general census.gov terms
    /// page contractually binds the separate geocoding.geo.census.gov host is unresolved by any primary
    /// source found; the notice is shown anyway, out of caution -- see docs/architecture/address-geocoding.md.
    /// </summary>
    public const string AttributionNotice =
        "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau.";

    private readonly HttpClient httpClient;
    private readonly CensusGeocoderOptions options;

    /// <summary>Creates a geocoder over an externally owned <see cref="HttpClient"/>. This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public CensusGeocoder(HttpClient httpClient, CensusGeocoderOptions? options = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? new CensusGeocoderOptions();
    }

    /// <exception cref="CensusGeocoderRequestValidationException">The address exceeds the configured maximum length, or the Census Geocoder rejected the request as malformed.</exception>
    /// <exception cref="CensusGeocoderNoCandidatesException">The Census Geocoder reported no address matches.</exception>
    /// <exception cref="CensusGeocoderServerException">The Census Geocoder reported a server-side error.</exception>
    /// <exception cref="CensusGeocoderNetworkException">A transport-level failure or timeout occurred.</exception>
    /// <exception cref="CensusGeocoderUnexpectedResponseException">The response could not be classified or parsed.</exception>
    public async ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri requestUri = BuildRequestUri(request.Address);
        string redactedRequestUri = SensitiveQueryRedactor.RedactUri(requestUri, SensitiveQueryParameterNames.KnownFamilies);

        if (request.Address.Length > options.MaximumAddressLength)
        {
            string addressLengthText = request.Address.Length.ToString(CultureInfo.InvariantCulture);
            string maximumLengthText = options.MaximumAddressLength.ToString(CultureInfo.InvariantCulture);
            throw new CensusGeocoderRequestValidationException(
                $"The address is {addressLengthText} characters long, which exceeds the Census Geocoder's documented " +
                $"{maximumLengthText}-character limit (\"Address cannot be empty and cannot exceed {maximumLengthText} " +
                "characters\"). Shorten the address before requesting this provider.",
                redactedRequestUri,
                statusCode: null,
                serverMessage: null);
        }

        HttpResponseMessage response = await SendGetAsync(requestUri, redactedRequestUri, cancellationToken).ConfigureAwait(false);
        try
        {
            return await HandleResponseAsync(response, redactedRequestUri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Uri BuildRequestUri(string address) => new(
        $"{options.EndpointUri}?address={Uri.EscapeDataString(address)}&benchmark={Uri.EscapeDataString(options.Benchmark)}&format=json",
        UriKind.Absolute);

    /// <summary>
    /// Sends one GET request and translates a transport-level failure into <see cref="CensusGeocoderNetworkException"/>.
    /// The original caught exception is never attached as <see cref="Exception.InnerException"/>: a
    /// caller-attached <see cref="DelegatingHandler"/> could throw an exception whose own <c>Message</c>
    /// embeds the request URI, and <see cref="Exception.ToString()"/> recurses into
    /// <see cref="Exception.InnerException"/>, so redacting only the outer message would not be enough -- the
    /// same pattern <c>OpenTopographyUsgs1mSource.SendGetAsync</c> already uses.
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
            throw new CensusGeocoderNetworkException(
                "The request to the Census Geocoder did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusGeocoderNetworkException(
                $"The request to the Census Geocoder failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusGeocoderNetworkException(
                $"An I/O error occurred while communicating with the Census Geocoder: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    private static async ValueTask<AddressGeocodeAcquisition> HandleResponseAsync(HttpResponseMessage response, string redactedRequestUri, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return ParseSuccess(body, redactedRequestUri);
        }

        throw ClassifyNonSuccessResponse(response.StatusCode, body, redactedRequestUri);
    }

    private static AddressGeocodeAcquisition ParseSuccess(string body, string redactedRequestUri)
    {
        CensusResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CensusResponseDto>(body);
        }
        catch (JsonException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies);
            throw new CensusGeocoderUnexpectedResponseException(
                $"The Census Geocoder returned a 200 response that could not be parsed as JSON: {redactedDetail}",
                redactedRequestUri,
                HttpStatusCode.OK,
                null);
        }

        if (dto?.Result?.AddressMatches is not List<CensusAddressMatchDto> matches)
        {
            throw new CensusGeocoderUnexpectedResponseException(
                "The Census Geocoder's 200 response did not carry the documented result.addressMatches shape.",
                redactedRequestUri,
                HttpStatusCode.OK,
                null);
        }

        if (matches.Count == 0)
        {
            throw new CensusGeocoderNoCandidatesException(
                "The Census Geocoder reported no address matches for the requested address. Try a more specific or differently formatted address.",
                redactedRequestUri);
        }

        var candidates = new List<AddressGeocodeCandidate>(matches.Count);
        foreach (CensusAddressMatchDto match in matches)
        {
            if (match.Coordinates is not { X: double x, Y: double y })
            {
                throw new CensusGeocoderUnexpectedResponseException(
                    "The Census Geocoder returned an address match missing its documented coordinates.x/coordinates.y fields.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (!double.IsFinite(y) || y is < -90d or > 90d || !double.IsFinite(x) || x is < -180d or > 180d)
            {
                throw new CensusGeocoderUnexpectedResponseException(
                    "The Census Geocoder returned an address match whose coordinates.x/coordinates.y fields were not a finite WGS 84 longitude/latitude pair.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (match.MatchedAddress is not string matchedAddress || string.IsNullOrWhiteSpace(matchedAddress))
            {
                throw new CensusGeocoderUnexpectedResponseException(
                    "The Census Geocoder returned an address match missing its documented matchedAddress field.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            // The Census Geocoder's schema has no ranking score or precision label of its own: array order is
            // the only ranking signal (preserved here, unchanged), and both stay null.
            candidates.Add(new AddressGeocodeCandidate(y, x, matchedAddress, AttributionNotice));
        }

        return new AddressGeocodeAcquisition(candidates);
    }

    private static AddressGeocoderException ClassifyNonSuccessResponse(HttpStatusCode statusCode, string body, string redactedRequestUri)
    {
        string? serverMessage = ExtractErrorMessage(body);
        string describedServerMessage = serverMessage ?? "(no error detail returned)";

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return new CensusGeocoderRequestValidationException(
                $"The Census Geocoder rejected the request as malformed (HTTP 400). Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        int statusCodeValue = (int)statusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            string statusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
            return new CensusGeocoderServerException(
                $"The Census Geocoder reported a server-side error (HTTP {statusText}). This is not a problem with the " +
                $"request; retry later. Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        string undocumentedStatusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
        return new CensusGeocoderUnexpectedResponseException(
            $"The Census Geocoder returned an undocumented status code (HTTP {undocumentedStatusText}). Server message: {describedServerMessage}",
            redactedRequestUri,
            statusCode,
            serverMessage);
    }

    /// <summary>
    /// Redacts the raw response body first -- the same order <c>GeocodioGeocoder.ExtractErrorMessage</c>
    /// already uses -- then attempts to parse the redacted text as the Census Geocoder's documented
    /// <c>{"errors": ["..."], "status": "NNN"}</c> envelope for a cleaner message, falling back to the whole
    /// redacted text when it does not parse as that shape. Census sends no API key of its own, so there is no
    /// secret to pass to <see cref="SensitiveQueryRedactor.RedactText"/>; the "name=value" pass still runs
    /// unconditionally, which is what protects a body that happens to echo a sensitive-looking parameter this
    /// provider never sent itself.
    /// </summary>
    private static string? ExtractErrorMessage(string body)
    {
        string redactedBody = SensitiveQueryRedactor.RedactText(body, SensitiveQueryParameterNames.KnownFamilies);
        try
        {
            CensusErrorResponseDto? dto = JsonSerializer.Deserialize<CensusErrorResponseDto>(redactedBody);
            if (dto?.Errors is { Count: > 0 } errors)
            {
                return string.Join(" ", errors);
            }
        }
        catch (JsonException)
        {
            // The body may not be the documented error envelope at all (an undocumented status code might
            // return plain text, or no body). Fall through to the redacted raw-text fallback below -- still
            // safe, because redaction already ran on the raw text above.
        }

        return string.IsNullOrWhiteSpace(redactedBody) ? null : redactedBody;
    }

    private sealed class CensusResponseDto
    {
        [JsonPropertyName("result")]
        public CensusResultDto? Result { get; set; }
    }

    private sealed class CensusResultDto
    {
        [JsonPropertyName("addressMatches")]
        public List<CensusAddressMatchDto>? AddressMatches { get; set; }
    }

    private sealed class CensusAddressMatchDto
    {
        [JsonPropertyName("matchedAddress")]
        public string? MatchedAddress { get; set; }

        [JsonPropertyName("coordinates")]
        public CensusCoordinatesDto? Coordinates { get; set; }
    }

    private sealed class CensusCoordinatesDto
    {
        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }
    }

    private sealed class CensusErrorResponseDto
    {
        [JsonPropertyName("errors")]
        public List<string>? Errors { get; set; }
    }
}
