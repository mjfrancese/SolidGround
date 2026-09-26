using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidGround.Core.Aois;
using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>
/// Resolves a parcel boundary by querying one county's ArcGIS REST <c>FeatureServer</c>/<c>MapServer</c>
/// layer, as registered in a <see cref="CountyParcelRegistry"/> keyed by 5-digit Census county GEOID. Every
/// registered county is treated as an unauthenticated public service: this source sends no API key and never
/// will unless a future registry entry adds one. See docs/architecture/parcel-boundary-sources.md for the full
/// request/response contract.
/// </summary>
public sealed class CountyParcelRegistrySource : IParcelBoundarySource
{
    /// <summary>The <c>resultRecordCount</c> sent with every address query, bounding a single request's own cost against a county-wide layer.</summary>
    public const int MaximumAddressMatches = 25;

    /// <summary>The maximum accepted length of a <see cref="ParcelAddressQuery.SearchText"/>, checked before any request is built.</summary>
    public const int MaximumAddressSearchTextLength = 200;

    private readonly HttpClient httpClient;
    private readonly CountyParcelRegistry registry;
    private readonly string geoid;

    /// <summary>Creates a source bound to one county's registry entry. This type never disposes <paramref name="httpClient"/>; its owner remains responsible for its lifetime.</summary>
    public CountyParcelRegistrySource(HttpClient httpClient, CountyParcelRegistry registry, string geoid)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(geoid);
        this.geoid = geoid;
    }

    /// <exception cref="CountyParcelRegistryUnregisteredGeoidException">The configured GEOID is not registered. No HTTP request is sent.</exception>
    /// <exception cref="CountyParcelRegistryRequestValidationException">The county service rejected the request, or a local validation rule (the address search text length bound) rejected it first.</exception>
    /// <exception cref="CountyParcelRegistryServerException">The county service reported a server-side error.</exception>
    /// <exception cref="CountyParcelRegistryNetworkException">A transport-level failure or timeout occurred.</exception>
    /// <exception cref="CountyParcelRegistryUnexpectedResponseException">The response could not be classified or parsed.</exception>
    public async ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!registry.EntriesByGeoid.TryGetValue(geoid, out CountyParcelRegistryEntry? entry))
        {
            throw new CountyParcelRegistryUnregisteredGeoidException(
                $"GEOID '{geoid}' is not registered in the county parcel registry loaded from '{registry.SourcePath}'. Add an entry for this county before requesting it.",
                geoid,
                registry.SourcePath);
        }

        Uri requestUri = BuildRequestUri(entry, query);
        string redactedRequestUri = SensitiveQueryRedactor.RedactUri(requestUri, SensitiveQueryParameterNames.Esri);

        HttpResponseMessage response = await SendGetAsync(requestUri, redactedRequestUri, cancellationToken).ConfigureAwait(false);
        try
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseBody(body, response.StatusCode, redactedRequestUri, entry);
        }
        finally
        {
            response.Dispose();
        }
    }

    // ---- Request construction -------------------------------------------------------------------------

    private static Uri BuildRequestUri(CountyParcelRegistryEntry entry, ParcelBoundaryQuery query)
    {
        string outFields = BuildOutFieldsList(entry.FieldMap);
        string baseQuery = BuildLayerQueryPathAndFixedParameters(entry, outFields);

        string queryText = query switch
        {
            ParcelPointQuery pointQuery =>
                $"{baseQuery}&geometryType=esriGeometryPoint&geometry={FormatCoordinate(pointQuery.Longitude)}," +
                $"{FormatCoordinate(pointQuery.Latitude)}&inSR=4326&where=1%3D1",
            ParcelAddressQuery addressQuery =>
                $"{baseQuery}&resultRecordCount={MaximumAddressMatches.ToString(CultureInfo.InvariantCulture)}" +
                $"&where={BuildAddressWhereClause(entry.FieldMap.SitusAddress, addressQuery.SearchText)}",
            _ => throw new ArgumentOutOfRangeException(nameof(query), query, "Unsupported parcel boundary query type."),
        };

        return new Uri(queryText, UriKind.Absolute);
    }

    /// <summary>
    /// Composes <c>"{serviceBaseUrl's scheme/host/path}/{layerIndex}/query?{...fixed parameters}"</c> by
    /// reading <paramref name="entry"/>'s <see cref="CountyParcelRegistryEntry.ServiceBaseUrl"/> through
    /// <see cref="Uri"/> itself, never by raw string concatenation of that configured value.
    /// <see cref="CountyParcelRegistry.Load"/> only requires <c>serviceBaseUrl</c> to be an absolute
    /// <c>https</c> URL; it does not forbid one that already carries its own query string (for example a
    /// hypothetical future per-entry <c>?token=...</c>, named as a possible later extension in
    /// docs/architecture/parcel-boundary-sources.md). Deriving the path with
    /// <see cref="Uri.GetLeftPart(UriPartial)"/> and folding any such existing query in front of this method's
    /// own parameters keeps the request targeting the intended <c>/{layerIndex}/query</c> resource with every
    /// parameter -- the entry's own and this method's -- staying distinct, instead of a naive concatenation
    /// silently merging a trailing configured parameter with this method's leading one into one corrupted
    /// value.
    /// </summary>
    private static string BuildLayerQueryPathAndFixedParameters(CountyParcelRegistryEntry entry, string outFields)
    {
        var serviceUri = new Uri(entry.ServiceBaseUrl, UriKind.Absolute);
        string layerQueryPath = $"{serviceUri.GetLeftPart(UriPartial.Path)}/{entry.LayerIndex.ToString(CultureInfo.InvariantCulture)}/query";
        string leadingParameters = serviceUri.Query.Length > 0 ? $"{serviceUri.Query[1..]}&" : string.Empty;
        return $"{layerQueryPath}?{leadingParameters}f=json&outFields={outFields}&returnGeometry=true&outSR=4326&spatialRel=esriSpatialRelIntersects";
    }

    /// <summary>Every non-null <see cref="CountyParcelFieldMap"/> value, comma-joined, deduplicated (so <c>book</c>/<c>page</c> mapped to the same field is sent once) -- never <c>*</c>.</summary>
    private static string BuildOutFieldsList(CountyParcelFieldMap fieldMap)
    {
        string?[] values =
        [
            fieldMap.ParcelId, fieldMap.SitusAddress, fieldMap.Subdivision, fieldMap.Lot, fieldMap.Block,
            fieldMap.Plat, fieldMap.Book, fieldMap.Page, fieldMap.LegalDescription, fieldMap.ReportedAcres,
            fieldMap.Zoning, fieldMap.StableParcelId,
        ];

        List<string> distinctFields = [];
        foreach (string? value in values)
        {
            if (value is not null && !distinctFields.Contains(value, StringComparer.Ordinal))
            {
                distinctFields.Add(value);
            }
        }

        return string.Join(',', distinctFields);
    }

    private static string FormatCoordinate(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds <c>UPPER(field) LIKE UPPER('%escaped%') ESCAPE '\'</c>, then percent-encodes the fully composed
    /// value exactly once, mirroring <c>EsriGeocoder.BuildRequestUri</c>'s own <see cref="Uri.EscapeDataString(string)"/>
    /// precedent. Escape order matters: first double every literal <c>\</c> (the chosen <c>ESCAPE</c>
    /// character), then escape every literal <c>%</c>/<c>_</c>, then double every single quote last, so an
    /// escaped <c>\'</c>-shaped sequence is never re-mangled by the quote-doubling step.
    /// </summary>
    private static string BuildAddressWhereClause(string situsAddressField, string searchText)
    {
        if (searchText.Length > MaximumAddressSearchTextLength)
        {
            throw new CountyParcelRegistryRequestValidationException(
                $"The address search text is {searchText.Length.ToString(CultureInfo.InvariantCulture)} character(s) long, " +
                $"which exceeds the maximum of {MaximumAddressSearchTextLength.ToString(CultureInfo.InvariantCulture)}.",
                redactedRequestUri: null,
                errorCode: null,
                serverMessage: null);
        }

        string escaped = searchText
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal);

        string likeClause = $"UPPER({situsAddressField}) LIKE UPPER('%{escaped}%') ESCAPE '\\'";
        return Uri.EscapeDataString(likeClause);
    }

    // ---- Transport --------------------------------------------------------------------------------------

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
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri);
            Exception redactedInner = ex is TaskCanceledException
                ? new TaskCanceledException(redactedDetail)
                : new OperationCanceledException(redactedDetail);
            throw new CountyParcelRegistryNetworkException(
                "The request to the county parcel service did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri);
            throw new CountyParcelRegistryNetworkException(
                $"The request to the county parcel service failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            string redactedDetail = SensitiveQueryRedactor.RedactText(ex.Message, SensitiveQueryParameterNames.Esri);
            throw new CountyParcelRegistryNetworkException(
                $"An I/O error occurred while communicating with the county parcel service: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    // ---- Response handling ------------------------------------------------------------------------------

    private static ParcelBoundaryAcquisition ParseBody(string body, HttpStatusCode transportStatusCode, string redactedRequestUri, CountyParcelRegistryEntry entry)
    {
        EsriQueryResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<EsriQueryResponseDto>(body);
        }
        catch (JsonException)
        {
            dto = null;
        }

        if (dto?.Error is { } error)
        {
            throw ClassifyErrorBody(error, body, redactedRequestUri);
        }

        if (dto?.Features is List<EsriQueryFeatureDto> features)
        {
            List<ParcelBoundaryCandidate> candidates = new(features.Count);
            for (int i = 0; i < features.Count; i++)
            {
                candidates.Add(BuildCandidate(features[i], i, entry, redactedRequestUri));
            }

            return new ParcelBoundaryAcquisition(candidates, dto.ExceededTransferLimit ?? false);
        }

        string redactedBody = SensitiveQueryRedactor.RedactText(body, SensitiveQueryParameterNames.Esri);
        string describedServerMessage = string.IsNullOrWhiteSpace(redactedBody) ? "(no error detail returned)" : redactedBody;
        int statusCodeValue = (int)transportStatusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            throw new CountyParcelRegistryServerException(
                $"The county parcel service reported a server-side error (HTTP {statusCodeValue.ToString(CultureInfo.InvariantCulture)}) with no recognizable " +
                $"error or features body. This is not a problem with the request; retry later. Server message: {describedServerMessage}",
                redactedRequestUri,
                errorCode: null,
                transportStatusCode,
                describedServerMessage);
        }

        throw new CountyParcelRegistryUnexpectedResponseException(
            $"The county parcel service returned a response (HTTP {statusCodeValue.ToString(CultureInfo.InvariantCulture)}) this source does not recognize " +
            $"as either an error object or a features array. Server message: {describedServerMessage}",
            redactedRequestUri,
            errorCode: null,
            transportStatusCode,
            describedServerMessage);
    }

    private static CountyParcelRegistryException ClassifyErrorBody(EsriQueryErrorDto error, string rawBody, string redactedRequestUri)
    {
        string rawMessage = error.Message ?? rawBody;
        string redactedMessage = SensitiveQueryRedactor.RedactText(rawMessage, SensitiveQueryParameterNames.Esri);

        if (error.Code is int clientCode and >= 400 and <= 499)
        {
            return new CountyParcelRegistryRequestValidationException(
                $"The county parcel service rejected the request (error code {clientCode.ToString(CultureInfo.InvariantCulture)}). Server message: {redactedMessage}",
                redactedRequestUri,
                clientCode,
                redactedMessage);
        }

        if (error.Code is int serverCode and >= 500 and <= 599)
        {
            return new CountyParcelRegistryServerException(
                $"The county parcel service reported a server-side error (error code {serverCode.ToString(CultureInfo.InvariantCulture)}). This is not a " +
                $"problem with the request; retry later. Server message: {redactedMessage}",
                redactedRequestUri,
                serverCode,
                transportStatusCode: null,
                redactedMessage);
        }

        string undocumentedCodeText = error.Code is int codeValue ? codeValue.ToString(CultureInfo.InvariantCulture) : "(none)";
        return new CountyParcelRegistryUnexpectedResponseException(
            $"The county parcel service returned an undocumented error code ({undocumentedCodeText}). Server message: {redactedMessage}",
            redactedRequestUri,
            error.Code,
            transportStatusCode: null,
            redactedMessage);
    }

    private static ParcelBoundaryCandidate BuildCandidate(EsriQueryFeatureDto feature, int featureIndex, CountyParcelRegistryEntry entry, string redactedRequestUri)
    {
        Dictionary<string, JsonElement> attributes = feature.Attributes ?? throw new CountyParcelRegistryUnexpectedResponseException(
            $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} has no attributes.",
            redactedRequestUri, errorCode: null, transportStatusCode: null, serverMessage: null);

        string parcelId = ReadRequiredString(attributes, entry.FieldMap.ParcelId, featureIndex, redactedRequestUri);
        string situsAddress = ReadRequiredString(attributes, entry.FieldMap.SitusAddress, featureIndex, redactedRequestUri);
        string? subdivision = ReadOptionalString(attributes, entry.FieldMap.Subdivision);
        string? lot = ReadOptionalString(attributes, entry.FieldMap.Lot);
        string? block = ReadOptionalString(attributes, entry.FieldMap.Block);
        string? plat = ReadOptionalString(attributes, entry.FieldMap.Plat);
        string? book = ReadOptionalString(attributes, entry.FieldMap.Book);
        string? page = ReadOptionalString(attributes, entry.FieldMap.Page);
        string? legalDescription = ReadOptionalString(attributes, entry.FieldMap.LegalDescription);
        string? zoning = ReadOptionalString(attributes, entry.FieldMap.Zoning);
        string? stableParcelId = ReadOptionalString(attributes, entry.FieldMap.StableParcelId);
        double? reportedAcres = ReadOptionalDouble(attributes, entry.FieldMap.ReportedAcres);

        if (feature.Geometry?.Rings is not List<List<List<double>>> rings)
        {
            throw new CountyParcelRegistryUnexpectedResponseException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} has no polygon geometry (missing 'rings').",
                redactedRequestUri, errorCode: null, transportStatusCode: null, serverMessage: null);
        }

        List<IReadOnlyList<(double X, double Y)>> ringTuples = new(rings.Count);
        foreach (List<List<double>> ring in rings)
        {
            List<(double X, double Y)> points = new(ring.Count);
            foreach (List<double> point in ring)
            {
                if (point.Count < 2)
                {
                    throw new CountyParcelRegistryUnexpectedResponseException(
                        $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} has a ring position with fewer than 2 ordinates.",
                        redactedRequestUri, errorCode: null, transportStatusCode: null, serverMessage: null);
                }

                // A third (Z) ordinate, if present, is intentionally dropped: PolygonalRegion is a 2D contract.
                points.Add((point[0], point[1]));
            }

            ringTuples.Add(points);
        }

        PolygonalRegion boundary;
        try
        {
            boundary = EsriJsonPolygonReader.Read(ringTuples, ParcelBoundaryWgs84.Reference, featureIndex);
        }
        catch (ParcelGeometryException ex)
        {
            throw new CountyParcelRegistryUnexpectedResponseException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)}'s geometry could not be interpreted: {ex.Message}",
                redactedRequestUri, errorCode: null, transportStatusCode: null, serverMessage: null, ex);
        }

        double areaSquareMeters = ParcelBoundaryWgs84.ComputeAreaSquareMeters(boundary);
        bool bookPageAreUnconfirmedProxies = book is not null || page is not null;

        return new ParcelBoundaryCandidate(
            boundary,
            parcelId,
            areaSquareMeters,
            ParcelBoundarySourceKind.CountyRegistry,
            $"{entry.DisplayName} (GEOID {entry.Geoid})",
            entry.LicenseDisclaimerText,
            situsAddress: situsAddress,
            subdivision: subdivision,
            lot: lot,
            block: block,
            plat: plat,
            book: book,
            page: page,
            bookPageAreUnconfirmedProxies: bookPageAreUnconfirmedProxies,
            legalDescription: legalDescription,
            reportedAcres: reportedAcres,
            zoning: zoning,
            stableParcelId: stableParcelId);
    }

    private static string ReadRequiredString(Dictionary<string, JsonElement> attributes, string fieldName, int featureIndex, string redactedRequestUri)
    {
        string? value = ReadOptionalString(attributes, fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CountyParcelRegistryUnexpectedResponseException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} is missing a value for the mapped field '{fieldName}'.",
                redactedRequestUri, errorCode: null, transportStatusCode: null, serverMessage: null);
        }

        return value;
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement> attributes, string? fieldName)
    {
        if (fieldName is null || !attributes.TryGetValue(fieldName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    /// <summary>Accepts either a JSON number or a numeric string: Esri sometimes returns a numeric-typed field as a JSON number.</summary>
    private static double? ReadOptionalDouble(Dictionary<string, JsonElement> attributes, string? fieldName)
    {
        if (fieldName is null || !attributes.TryGetValue(fieldName, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double numberValue))
        {
            return numberValue;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    // ---- Wire DTOs ---------------------------------------------------------------------------------------

    private sealed class EsriQueryResponseDto
    {
        [JsonPropertyName("features")]
        public List<EsriQueryFeatureDto>? Features { get; set; }

        [JsonPropertyName("error")]
        public EsriQueryErrorDto? Error { get; set; }

        [JsonPropertyName("exceededTransferLimit")]
        public bool? ExceededTransferLimit { get; set; }
    }

    private sealed class EsriQueryFeatureDto
    {
        [JsonPropertyName("attributes")]
        public Dictionary<string, JsonElement>? Attributes { get; set; }

        [JsonPropertyName("geometry")]
        public EsriQueryGeometryDto? Geometry { get; set; }
    }

    private sealed class EsriQueryGeometryDto
    {
        [JsonPropertyName("rings")]
        public List<List<List<double>>>? Rings { get; set; }
    }

    private sealed class EsriQueryErrorDto
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
