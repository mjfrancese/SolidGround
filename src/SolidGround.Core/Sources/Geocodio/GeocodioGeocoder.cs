using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.Geocodio;

/// <summary>
/// Geocodes an address through Geocodio's v2 <c>/geocode</c> endpoint, a keyed opt-in provider
/// (<c>GEOCODIO_API_KEY</c>). Geocodio transports its key only in an <c>Authorization: Bearer</c> header, so
/// <see cref="BuildRequestUri"/> never takes a key parameter at all -- unlike a query-string-transported key,
/// there is no "build the URI with a null key" branch to get right, because the key is structurally never
/// part of the request URI.
/// </summary>
public sealed class GeocodioGeocoder : IAddressGeocoder
{
    public const string ProviderName = "Geocodio";

    private readonly HttpClient httpClient;
    private readonly IGeocodioApiKeyProvider apiKeyProvider;
    private readonly GeocodioGeocoderOptions options;

    /// <summary>Creates a geocoder over an externally owned <see cref="HttpClient"/>. This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public GeocodioGeocoder(HttpClient httpClient, IGeocodioApiKeyProvider apiKeyProvider, GeocodioGeocoderOptions? options = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        this.options = options ?? new GeocodioGeocoderOptions();
    }

    /// <exception cref="GeocodioGeocoderAuthorizationException">No API key is configured, or Geocodio rejected the request (HTTP 403).</exception>
    /// <exception cref="GeocodioGeocoderRequestValidationException">Geocodio rejected the request as malformed or ambiguous (HTTP 422).</exception>
    /// <exception cref="GeocodioGeocoderQuotaException">Geocodio reported a rate limit (HTTP 429).</exception>
    /// <exception cref="GeocodioGeocoderNoCandidatesException">Geocodio reported an empty results array.</exception>
    /// <exception cref="GeocodioGeocoderServerException">Geocodio reported a server-side error.</exception>
    /// <exception cref="GeocodioGeocoderNetworkException">A transport-level failure or timeout occurred.</exception>
    /// <exception cref="GeocodioGeocoderUnexpectedResponseException">The response could not be classified or parsed.</exception>
    public async ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri requestUri = BuildRequestUri(request.Address);
        string redactedRequestUri = SensitiveQueryRedactor.RedactUri(requestUri, SensitiveQueryParameterNames.KnownFamilies);

        ApiKey? apiKey = apiKeyProvider.GetApiKey();
        if (apiKey is null)
        {
            throw new GeocodioGeocoderAuthorizationException(
                "No Geocodio API key is configured. Set GEOCODIO_API_KEY in the process environment, or supply an " +
                "IGeocodioApiKeyProvider that returns one, before requesting this provider.",
                redactedRequestUri,
                GeocodioGeocoderAuthorizationFailure.ApiKeyMissing,
                statusCode: null,
                serverMessage: null);
        }

        HttpResponseMessage response = await SendGetAsync(requestUri, redactedRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
        try
        {
            return await HandleResponseAsync(response, redactedRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Uri BuildRequestUri(string address) => new(
        $"{options.EndpointUri}?q={Uri.EscapeDataString(address)}&limit={options.MaximumCandidates.ToString(CultureInfo.InvariantCulture)}",
        UriKind.Absolute);

    /// <summary>
    /// Sends one GET request with a Bearer-authenticated header and translates a transport-level failure into
    /// <see cref="GeocodioGeocoderNetworkException"/>. Every catch below redacts <c>ex.Message</c> and
    /// rebuilds a new exception of the same concrete type from the redacted text before attaching it as
    /// <see cref="Exception.InnerException"/> -- it never attaches the original caught exception. This
    /// mirrors <c>OpenTopographyUsgs1mSource.SendGetAsync</c>'s own established fix for the same leak:
    /// <see cref="Exception.ToString()"/> recurses into <see cref="Exception.InnerException"/>, so an
    /// unredacted original exception's own <c>Message</c> -- which a caller-attached
    /// <see cref="DelegatingHandler"/> could have built by embedding the request URI -- would leak the key
    /// through the outer, publicly-thrown exception's own <see cref="Exception.ToString()"/> even when its
    /// outer <c>Message</c> is correctly redacted.
    /// </summary>
    private async ValueTask<HttpResponseMessage> SendGetAsync(Uri requestUri, string redactedRequestUri, ApiKey apiKey, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.RawValue);

        try
        {
            return await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies, apiKey);
            Exception redactedInner = ex is TaskCanceledException
                ? new TaskCanceledException(redactedDetail)
                : new OperationCanceledException(redactedDetail);
            throw new GeocodioGeocoderNetworkException(
                "The request to Geocodio did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies, apiKey);
            throw new GeocodioGeocoderNetworkException(
                $"The request to Geocodio failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies, apiKey);
            throw new GeocodioGeocoderNetworkException(
                $"An I/O error occurred while communicating with Geocodio: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    private static async ValueTask<AddressGeocodeAcquisition> HandleResponseAsync(HttpResponseMessage response, string redactedRequestUri, ApiKey apiKey, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return ParseSuccess(body, redactedRequestUri, apiKey);
        }

        throw ClassifyNonSuccessResponse(response, body, redactedRequestUri, apiKey);
    }

    private static AddressGeocodeAcquisition ParseSuccess(string body, string redactedRequestUri, ApiKey apiKey)
    {
        GeocodioResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GeocodioResponseDto>(body);
        }
        catch (JsonException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.KnownFamilies, apiKey);
            throw new GeocodioGeocoderUnexpectedResponseException(
                $"Geocodio returned a 200 response that could not be parsed as JSON: {redactedDetail}",
                redactedRequestUri,
                HttpStatusCode.OK,
                null);
        }

        if (dto?.Results is not List<GeocodioResultDto> results)
        {
            throw new GeocodioGeocoderUnexpectedResponseException(
                "Geocodio's 200 response did not carry the documented results array.",
                redactedRequestUri,
                HttpStatusCode.OK,
                null);
        }

        if (results.Count == 0)
        {
            throw new GeocodioGeocoderNoCandidatesException(
                "Geocodio reported no results for the requested address. Try a more specific or differently formatted address.",
                redactedRequestUri);
        }

        var candidates = new List<AddressGeocodeCandidate>(results.Count);
        foreach (GeocodioResultDto result in results)
        {
            if (result.Location is not { Lat: double lat, Lng: double lng })
            {
                throw new GeocodioGeocoderUnexpectedResponseException(
                    "Geocodio returned a result missing its documented location.lat/location.lng fields.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (!double.IsFinite(lat) || lat is < -90d or > 90d || !double.IsFinite(lng) || lng is < -180d or > 180d)
            {
                throw new GeocodioGeocoderUnexpectedResponseException(
                    "Geocodio returned a result whose location.lat/location.lng fields were not a finite WGS 84 latitude/longitude pair.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (result.FormattedAddress is not string formattedAddress || string.IsNullOrWhiteSpace(formattedAddress))
            {
                throw new GeocodioGeocoderUnexpectedResponseException(
                    "Geocodio returned a result missing its documented formatted_address field.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (result.Source is not string source || string.IsNullOrWhiteSpace(source))
            {
                throw new GeocodioGeocoderUnexpectedResponseException(
                    "Geocodio returned a result missing its documented source field, which this provider uses as the candidate's attribution.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            if (result.AccuracyType is not null && string.IsNullOrWhiteSpace(result.AccuracyType))
            {
                throw new GeocodioGeocoderUnexpectedResponseException(
                    "Geocodio returned a result whose documented accuracy_type field was present but blank.",
                    redactedRequestUri,
                    HttpStatusCode.OK,
                    null);
            }

            // accuracy/accuracy_type preserve Geocodio's own wire values verbatim, including null when the
            // provider omits them; source is always the wire value, never a hardcoded constant -- unlike
            // Census and Esri, whose schemas carry no per-candidate attribution field of their own. Geocodio
            // guarantees results[] is always pre-sorted most-accurate-first, so wire order is preserved as-is.
            candidates.Add(new AddressGeocodeCandidate(lat, lng, formattedAddress, source, result.AccuracyType, result.Accuracy));
        }

        return new AddressGeocodeAcquisition(candidates);
    }

    private static AddressGeocoderException ClassifyNonSuccessResponse(HttpResponseMessage response, string body, string redactedRequestUri, ApiKey apiKey)
    {
        HttpStatusCode statusCode = response.StatusCode;
        string? serverMessage = ExtractErrorMessage(body, apiKey);
        string describedServerMessage = serverMessage ?? "(no error detail returned)";

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return new GeocodioGeocoderAuthorizationException(
                "Geocodio rejected the request (HTTP 403): an invalid API key, a daily-limit condition, and a " +
                $"permission restriction are all reported this way. Server message: {describedServerMessage}",
                redactedRequestUri,
                GeocodioGeocoderAuthorizationFailure.Rejected,
                statusCode,
                serverMessage);
        }

        if (statusCode == HttpStatusCode.UnprocessableEntity)
        {
            return new GeocodioGeocoderRequestValidationException(
                $"Geocodio rejected the request as malformed or ambiguous (HTTP 422). Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return new GeocodioGeocoderQuotaException(
                $"Geocodio reported a rate limit (HTTP 429). Wait and retry later, or reduce request frequency. " +
                $"Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage,
                GetHeaderValue(response, "X-RateLimit-Remaining"),
                GetHeaderValue(response, "X-RateLimit-Limit"),
                GetHeaderValue(response, "X-RateLimit-Period"));
        }

        int statusCodeValue = (int)statusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            string statusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
            return new GeocodioGeocoderServerException(
                $"Geocodio reported a server-side error (HTTP {statusText}). This is not a problem with the request; " +
                $"retry later. Server message: {describedServerMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        string undocumentedStatusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
        return new GeocodioGeocoderUnexpectedResponseException(
            $"Geocodio returned an undocumented status code (HTTP {undocumentedStatusText}). Server message: {describedServerMessage}",
            redactedRequestUri,
            statusCode,
            serverMessage);
    }

    private static string? GetHeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    /// <summary>
    /// Redacts the raw response body first -- exactly the order <c>OpenTopographyUsgs1mSource.BuildServerMessage</c>
    /// already uses -- then attempts to parse the redacted text as Geocodio's documented
    /// <c>{"error": "..."}</c> envelope for a cleaner message, falling back to the whole redacted text when it
    /// does not parse as that shape. Redacting first means a key echoed anywhere in the body can never survive
    /// even if the JSON-shape parse below fails for an unrelated reason.
    /// </summary>
    private static string? ExtractErrorMessage(string body, ApiKey apiKey)
    {
        string redactedBody = SensitiveQueryRedactor.RedactText(body, SensitiveQueryParameterNames.KnownFamilies, apiKey);
        try
        {
            GeocodioErrorResponseDto? dto = JsonSerializer.Deserialize<GeocodioErrorResponseDto>(redactedBody);
            if (dto?.Error is { Length: > 0 } error)
            {
                return error;
            }
        }
        catch (JsonException)
        {
            // The body may not be the documented error envelope at all. Fall through to the redacted raw-text
            // fallback below -- still safe, because redaction already ran on the raw text above.
        }

        return string.IsNullOrWhiteSpace(redactedBody) ? null : redactedBody;
    }

    private sealed class GeocodioResponseDto
    {
        [JsonPropertyName("results")]
        public List<GeocodioResultDto>? Results { get; set; }
    }

    private sealed class GeocodioResultDto
    {
        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("location")]
        public GeocodioLocationDto? Location { get; set; }

        [JsonPropertyName("accuracy")]
        public double? Accuracy { get; set; }

        [JsonPropertyName("accuracy_type")]
        public string? AccuracyType { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }
    }

    private sealed class GeocodioLocationDto
    {
        [JsonPropertyName("lat")]
        public double? Lat { get; set; }

        [JsonPropertyName("lng")]
        public double? Lng { get; set; }
    }

    private sealed class GeocodioErrorResponseDto
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
