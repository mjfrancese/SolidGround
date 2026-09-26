using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.Esri;

/// <summary>
/// Geocodes an address through Esri's World Geocoding Service <c>findAddressCandidates</c> endpoint with
/// <c>forStorage=true</c>, a keyed opt-in provider (<c>ARCGIS_API_KEY</c>). Esri transports its key only in
/// an <c>Authorization: Bearer</c> header, so <see cref="BuildRequestUri"/> never takes a key parameter at
/// all -- the same header-only-key safety property <see cref="Geocodio.GeocodioGeocoder"/> has. Esri's own
/// docs never confirm whether this endpoint's outer HTTP transport status ever varies from 200, or whether
/// Esri always answers 200 OK with the real status only in a body <c>error</c> object, so this type parses
/// defensively for both possibilities: see <see cref="ParseBody"/>.
/// </summary>
public sealed class EsriGeocoder : IAddressGeocoder
{
    public const string ProviderName = "Esri";

    /// <summary>
    /// Esri's response schema carries no per-candidate or per-response attribution field. Source:
    /// https://developers.arcgis.com/documentation/esri-and-data-attribution/no-map/ (retrieved 2026-09-25),
    /// whose own "no map" examples show exactly this phrase. Attached to every candidate this provider
    /// returns.
    /// </summary>
    public const string AttributionNotice = "Powered by Esri";

    private readonly HttpClient httpClient;
    private readonly IEsriApiKeyProvider apiKeyProvider;
    private readonly EsriGeocoderOptions options;

    /// <summary>Creates a geocoder over an externally owned <see cref="HttpClient"/>. This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public EsriGeocoder(HttpClient httpClient, IEsriApiKeyProvider apiKeyProvider, EsriGeocoderOptions? options = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        this.options = options ?? new EsriGeocoderOptions();
    }

    /// <exception cref="EsriGeocoderAuthorizationException">No API key is configured, or Esri reported <c>error.code</c> 403 or 499.</exception>
    /// <exception cref="EsriGeocoderRequestValidationException">Esri reported <c>error.code</c> 400.</exception>
    /// <exception cref="EsriGeocoderNoCandidatesException">Esri reported an empty candidates array.</exception>
    /// <exception cref="EsriGeocoderServerException">Esri reported a server-side error.</exception>
    /// <exception cref="EsriGeocoderNetworkException">A transport-level failure or timeout occurred.</exception>
    /// <exception cref="EsriGeocoderUnexpectedResponseException">The response could not be classified or parsed.</exception>
    public async ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri requestUri = BuildRequestUri(request.Address);
        string redactedRequestUri = SensitiveQueryRedactor.RedactUri(requestUri, SensitiveQueryParameterNames.Esri);

        ApiKey? apiKey = apiKeyProvider.GetApiKey();
        if (apiKey is null)
        {
            throw new EsriGeocoderAuthorizationException(
                "No Esri API key is configured. Set ARCGIS_API_KEY in the process environment, or supply an " +
                "IEsriApiKeyProvider that returns one, before requesting this provider.",
                redactedRequestUri,
                EsriGeocoderAuthorizationFailure.ApiKeyMissing,
                errorCode: null,
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
        $"{options.EndpointUri}?f=json&SingleLine={Uri.EscapeDataString(address)}&forStorage=true" +
        $"&outFields=Addr_type&maxLocations={options.MaximumCandidates.ToString(CultureInfo.InvariantCulture)}",
        UriKind.Absolute);

    /// <summary>
    /// Sends one GET request with a Bearer-authenticated header and translates a transport-level failure into
    /// <see cref="EsriGeocoderNetworkException"/>. Every catch below redacts <c>ex.Message</c> and rebuilds a
    /// new exception of the same concrete type from the redacted text before attaching it as
    /// <see cref="Exception.InnerException"/> -- it never attaches the original caught exception, for the
    /// identical reason <see cref="Geocodio.GeocodioGeocoder.GeocodeAsync"/>'s own equivalent method does not.
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
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri, apiKey);
            Exception redactedInner = ex is TaskCanceledException
                ? new TaskCanceledException(redactedDetail)
                : new OperationCanceledException(redactedDetail);
            throw new EsriGeocoderNetworkException(
                "The request to Esri did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri, apiKey);
            throw new EsriGeocoderNetworkException(
                $"The request to Esri failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri, apiKey);
            throw new EsriGeocoderNetworkException(
                $"An I/O error occurred while communicating with Esri: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    private static async ValueTask<AddressGeocodeAcquisition> HandleResponseAsync(HttpResponseMessage response, string redactedRequestUri, ApiKey apiKey, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseBody(body, response.StatusCode, redactedRequestUri, apiKey);
    }

    /// <summary>
    /// Always attempts to read a body <c>error</c> object first, regardless of the outer transport status
    /// (Esri has been documented to report a real failure -- <c>error.code</c> 403/499/... -- inside an outer
    /// HTTP 200), then a <c>candidates</c> array, and only falls back to classifying by the outer transport
    /// status when neither shape parses at all.
    /// </summary>
    private static AddressGeocodeAcquisition ParseBody(string body, HttpStatusCode transportStatusCode, string redactedRequestUri, ApiKey apiKey)
    {
        EsriResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<EsriResponseDto>(body);
        }
        catch (JsonException)
        {
            dto = null;
        }

        if (dto?.Error is { } error)
        {
            throw ClassifyErrorBody(error, body, redactedRequestUri, apiKey);
        }

        if (dto?.Candidates is List<EsriCandidateDto> candidatesDto)
        {
            return ParseCandidates(candidatesDto, redactedRequestUri);
        }

        string? serverMessage = ExtractRawMessage(body, apiKey);
        string describedServerMessage = serverMessage ?? "(no error detail returned)";
        int statusCodeValue = (int)transportStatusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            string statusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
            throw new EsriGeocoderServerException(
                $"Esri reported a server-side error (HTTP {statusText}) with no recognizable error or candidates body. " +
                $"This is not a problem with the request; retry later. Server message: {describedServerMessage}",
                redactedRequestUri,
                errorCode: null,
                transportStatusCode,
                serverMessage);
        }

        string undocumentedStatusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
        throw new EsriGeocoderUnexpectedResponseException(
            $"Esri returned a response (HTTP {undocumentedStatusText}) this provider does not recognize as either an " +
            $"error object or a candidates array. Server message: {describedServerMessage}",
            redactedRequestUri,
            errorCode: null,
            transportStatusCode,
            serverMessage);
    }

    private static AddressGeocoderException ClassifyErrorBody(EsriErrorDto error, string rawBody, string redactedRequestUri, ApiKey apiKey)
    {
        string rawMessage = error.Message ?? rawBody;
        string redactedMessage = SensitiveQueryRedactor.RedactText(rawMessage, SensitiveQueryParameterNames.Esri, apiKey);

        if (error.Code is 400)
        {
            return new EsriGeocoderRequestValidationException(
                $"Esri rejected the request as malformed (error code 400). Server message: {redactedMessage}",
                redactedRequestUri,
                400,
                redactedMessage);
        }

        if (error.Code is 403)
        {
            return new EsriGeocoderAuthorizationException(
                "Esri rejected the request: the configured token lacks the privilege to store results (error code " +
                $"403; this provider always sends forStorage=true). Server message: {redactedMessage}",
                redactedRequestUri,
                EsriGeocoderAuthorizationFailure.InsufficientPrivilege,
                403,
                redactedMessage);
        }

        if (error.Code is 499)
        {
            return new EsriGeocoderAuthorizationException(
                "Esri reported that a token is required (error code 499). This should be unreachable: this provider " +
                $"already checks for a configured key before sending any request. Server message: {redactedMessage}",
                redactedRequestUri,
                EsriGeocoderAuthorizationFailure.TokenRequired,
                499,
                redactedMessage);
        }

        if (error.Code is int serverErrorCode and (500 or 504))
        {
            string codeText = serverErrorCode.ToString(CultureInfo.InvariantCulture);
            return new EsriGeocoderServerException(
                $"Esri reported a server-side error (error code {codeText}). This is not a problem with the request; " +
                $"retry later. Server message: {redactedMessage}",
                redactedRequestUri,
                serverErrorCode,
                transportStatusCode: null,
                redactedMessage);
        }

        string undocumentedCodeText = error.Code is int codeValue ? codeValue.ToString(CultureInfo.InvariantCulture) : "(none)";
        return new EsriGeocoderUnexpectedResponseException(
            $"Esri returned an undocumented error code ({undocumentedCodeText}). Server message: {redactedMessage}",
            redactedRequestUri,
            error.Code,
            transportStatusCode: null,
            redactedMessage);
    }

    private static string? ExtractRawMessage(string body, ApiKey apiKey)
    {
        string redactedBody = SensitiveQueryRedactor.RedactText(body, SensitiveQueryParameterNames.Esri, apiKey);
        return string.IsNullOrWhiteSpace(redactedBody) ? null : redactedBody;
    }

    private static AddressGeocodeAcquisition ParseCandidates(List<EsriCandidateDto> candidatesDto, string redactedRequestUri)
    {
        if (candidatesDto.Count == 0)
        {
            throw new EsriGeocoderNoCandidatesException(
                "Esri reported no address candidates for the requested address. Try a more specific or differently formatted address.",
                redactedRequestUri);
        }

        var candidates = new List<AddressGeocodeCandidate>(candidatesDto.Count);
        foreach (EsriCandidateDto candidateDto in candidatesDto)
        {
            if (candidateDto.Location is not { X: double x, Y: double y })
            {
                throw new EsriGeocoderUnexpectedResponseException(
                    "Esri returned a candidate missing its documented location.x/location.y fields.",
                    redactedRequestUri,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }

            if (!double.IsFinite(y) || y is < -90d or > 90d || !double.IsFinite(x) || x is < -180d or > 180d)
            {
                throw new EsriGeocoderUnexpectedResponseException(
                    "Esri returned a candidate whose location.x/location.y fields were not a finite WGS 84 longitude/latitude pair.",
                    redactedRequestUri,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }

            if (candidateDto.Address is not string address || string.IsNullOrWhiteSpace(address))
            {
                throw new EsriGeocoderUnexpectedResponseException(
                    "Esri returned a candidate missing its documented address field.",
                    redactedRequestUri,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }

            if (candidateDto.Score is not double score)
            {
                throw new EsriGeocoderUnexpectedResponseException(
                    "Esri returned a candidate missing its documented score field.",
                    redactedRequestUri,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }

            string? precisionLabel = candidateDto.Attributes?.AddrType;
            if (precisionLabel is not null && string.IsNullOrWhiteSpace(precisionLabel))
            {
                throw new EsriGeocoderUnexpectedResponseException(
                    "Esri returned a candidate whose documented attributes.Addr_type field was present but blank.",
                    redactedRequestUri,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }

            candidates.Add(new AddressGeocodeCandidate(y, x, address, AttributionNotice, precisionLabel, score));
        }

        // Esri's docs never state candidates[] is returned in score order (unlike Geocodio's explicit
        // guarantee): sort by score descending with a stable sort, so a bug that trusted wire order would be
        // caught by a test fixture deliberately listed out of score order.
        return new AddressGeocodeAcquisition([.. candidates.OrderByDescending(candidate => candidate.Score)]);
    }

    private sealed class EsriResponseDto
    {
        [JsonPropertyName("candidates")]
        public List<EsriCandidateDto>? Candidates { get; set; }

        [JsonPropertyName("error")]
        public EsriErrorDto? Error { get; set; }
    }

    private sealed class EsriCandidateDto
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("location")]
        public EsriLocationDto? Location { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("attributes")]
        public EsriAttributesDto? Attributes { get; set; }
    }

    private sealed class EsriLocationDto
    {
        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }
    }

    private sealed class EsriAttributesDto
    {
        [JsonPropertyName("Addr_type")]
        public string? AddrType { get; set; }
    }

    private sealed class EsriErrorDto
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
