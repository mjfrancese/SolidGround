using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>Identifies how a successful acquisition located its coordinate reference metadata.</summary>
public enum OpenTopographyReferenceSource
{
    /// <summary>A sibling .prj entry supplied the WKT text.</summary>
    PrjSidecar,

    /// <summary>An .aux.xml entry's &lt;SRS&gt; element supplied the WKT text.</summary>
    AuxXmlSidecar,

    /// <summary>
    /// The horizontal reference was synthesized from the GeoKeys of a second, otherwise identical request
    /// with <c>outputFormat=GTiff</c>, because the data response itself (a bare AAIGrid body) carried no
    /// <c>.prj</c> or <c>.aux.xml</c> sidecar. See
    /// docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified 2026-09-19" and
    /// "GeoKey to WKT synthesis" sections.
    /// </summary>
    GeoTiffGeoKeys,
}

/// <summary>
/// Evidence about the GeoTIFF metadata response requested -- an otherwise identical request with
/// <c>outputFormat=GTiff</c> -- to recover a bare AAIGrid response's coordinate reference metadata from its
/// GeoKeys. Present only when the acquisition's <see cref="OpenTopographyResponseEvidence.ReferenceSource"/>
/// is <see cref="OpenTopographyReferenceSource.GeoTiffGeoKeys"/>. Every string here is already redacted, the
/// same way every string on <see cref="OpenTopographyResponseEvidence"/> itself is. See
/// docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified 2026-09-19" section.
/// </summary>
public sealed record OpenTopographyMetadataRequestEvidence
{
    public OpenTopographyMetadataRequestEvidence(
        string redactedRequestUri,
        HttpStatusCode statusCode,
        string? contentType,
        string? contentDispositionFileName,
        long responseByteCount,
        int projectedCoordinateSystemCode,
        string? citation,
        string rasterType,
        long imageWidth,
        long imageLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedRequestUri);
        if (responseByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(responseByteCount), responseByteCount, "Response byte count cannot be negative.");
        }

        if (rasterType is not ("PixelIsArea" or "PixelIsPoint"))
        {
            throw new ArgumentOutOfRangeException(nameof(rasterType), rasterType, "Raster type must be \"PixelIsArea\" or \"PixelIsPoint\".");
        }

        if (imageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), imageWidth, "Image width must be positive.");
        }

        if (imageLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageLength), imageLength, "Image length must be positive.");
        }

        RedactedRequestUri = redactedRequestUri;
        StatusCode = statusCode;
        ContentType = contentType;
        ContentDispositionFileName = contentDispositionFileName;
        ResponseByteCount = responseByteCount;
        ProjectedCoordinateSystemCode = projectedCoordinateSystemCode;
        Citation = citation;
        RasterType = rasterType;
        ImageWidth = imageWidth;
        ImageLength = imageLength;
    }

    /// <summary>The metadata request's URI with its "API_Key" query parameter redacted. Never blank.</summary>
    public string RedactedRequestUri { get; }

    public HttpStatusCode StatusCode { get; }
    public string? ContentType { get; }
    public string? ContentDispositionFileName { get; }
    public long ResponseByteCount { get; }

    /// <summary>GeoKey 3072 (ProjectedCSTypeGeoKey): the EPSG projected coordinate system code the GeoTIFF metadata declared.</summary>
    public int ProjectedCoordinateSystemCode { get; }

    /// <summary>GeoKey 1026 (GTCitationGeoKey), redacted, when the GeoTIFF metadata carried one.</summary>
    public string? Citation { get; }

    /// <summary>GeoKey 1025 (GTRasterTypeGeoKey), rendered as <c>"PixelIsArea"</c> or <c>"PixelIsPoint"</c>.</summary>
    public string RasterType { get; }

    /// <summary>Tag 256 (ImageWidth) from the GeoTIFF metadata.</summary>
    public long ImageWidth { get; }

    /// <summary>Tag 257 (ImageLength) from the GeoTIFF metadata.</summary>
    public long ImageLength { get; }
}

/// <summary>
/// Raw, inspectable evidence about how an <see cref="OpenTopographyUsgs1mSource"/> response was
/// interpreted. Retained so a caller can audit or diagnose an acquisition without re-requesting it.
/// </summary>
public sealed record OpenTopographyResponseEvidence
{
    public OpenTopographyResponseEvidence(
        string redactedRequestUri,
        HttpStatusCode statusCode,
        string? contentType,
        string? contentDispositionFileName,
        IReadOnlyList<string> archiveEntryNames,
        OpenTopographyReferenceSource referenceSource,
        string wellKnownText,
        long responseByteCount,
        ReferenceOrigin horizontalReferenceOrigin,
        ReferenceOrigin verticalReferenceOrigin,
        OpenTopographyMetadataRequestEvidence? metadataRequest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedRequestUri);
        ArgumentNullException.ThrowIfNull(archiveEntryNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(wellKnownText);
        if (!Enum.IsDefined(referenceSource))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceSource), referenceSource, "Unsupported reference source kind.");
        }

        if (responseByteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(responseByteCount), responseByteCount, "Response byte count cannot be negative.");
        }

        if (!Enum.IsDefined(horizontalReferenceOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalReferenceOrigin), horizontalReferenceOrigin, "Unsupported reference origin kind.");
        }

        if (!Enum.IsDefined(verticalReferenceOrigin))
        {
            throw new ArgumentOutOfRangeException(nameof(verticalReferenceOrigin), verticalReferenceOrigin, "Unsupported reference origin kind.");
        }

        RedactedRequestUri = redactedRequestUri;
        StatusCode = statusCode;
        ContentType = contentType;
        ContentDispositionFileName = contentDispositionFileName;
        ArchiveEntryNames = archiveEntryNames;
        ReferenceSource = referenceSource;
        WellKnownText = wellKnownText;
        ResponseByteCount = responseByteCount;
        HorizontalReferenceOrigin = horizontalReferenceOrigin;
        VerticalReferenceOrigin = verticalReferenceOrigin;
        MetadataRequest = metadataRequest;
    }

    public string RedactedRequestUri { get; }
    public HttpStatusCode StatusCode { get; }
    public string? ContentType { get; }
    public string? ContentDispositionFileName { get; }
    public IReadOnlyList<string> ArchiveEntryNames { get; }
    public OpenTopographyReferenceSource ReferenceSource { get; }
    public string WellKnownText { get; }
    public long ResponseByteCount { get; }

    /// <summary>
    /// Where the horizontal reference actually came from: the data response itself
    /// (<see cref="ReferenceOrigin.SourceResponse"/>, for a zip archive's <c>.prj</c> or <c>.aux.xml</c>
    /// sidecar) or a separate metadata response for an identical request
    /// (<see cref="ReferenceOrigin.SourceMetadataResponse"/>, for GeoTIFF GeoKeys).
    /// </summary>
    public ReferenceOrigin HorizontalReferenceOrigin { get; }

    /// <summary>
    /// Where the vertical reference actually came from. This source only ever produces
    /// <see cref="ReferenceOrigin.SourceResponse"/> (carried by a <c>.prj</c> or <c>.aux.xml</c> sidecar) or
    /// <see cref="ReferenceOrigin.DatasetDocumentation"/> (declared from
    /// <see cref="OpenTopographyUsgs1mSourceOptions.DeclaredVerticalReference"/> because neither USGS 1 m
    /// response carries one): see docs/architecture/opentopography-usgs1m-source.md's "Declared vertical
    /// reference" section.
    /// </summary>
    public ReferenceOrigin VerticalReferenceOrigin { get; }

    /// <summary>
    /// Evidence about the GeoTIFF metadata request, present only when <see cref="ReferenceSource"/> is
    /// <see cref="OpenTopographyReferenceSource.GeoTiffGeoKeys"/>; null for every single-request path.
    /// </summary>
    public OpenTopographyMetadataRequestEvidence? MetadataRequest { get; }
}

/// <summary>Couples a successful OpenTopography acquisition with the evidence used to interpret its response.</summary>
public sealed record OpenTopographyUsgs1mAcquisition
{
    public OpenTopographyUsgs1mAcquisition(ElevationAcquisition acquisition, OpenTopographyResponseEvidence evidence)
    {
        Acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
    }

    public ElevationAcquisition Acquisition { get; }
    public OpenTopographyResponseEvidence Evidence { get; }
}

/// <summary>
/// Acquires USGS 1 meter bare-earth elevation data from OpenTopography's <c>usgsdem</c> endpoint as an
/// AAIGrid response. The endpoint accepts the API key only as the "API_Key" query parameter (there is no
/// header transport), so the key must travel in the request URI; every URI surfaced by this type outside
/// the outgoing request(s) is redacted through <see cref="OpenTopographyRedaction.RedactUri(Uri)"/>, which
/// is the only form safe to log or display.
/// </summary>
/// <remarks>
/// This source preserves whatever horizontal and vertical reference metadata the response actually
/// carries. When the response already carries its own metadata (a zip archive with a <c>.prj</c> or
/// <c>.aux.xml</c> sidecar), this source sends exactly one HTTP request, with origins
/// <see cref="ReferenceOrigin.SourceResponse"/>/<see cref="ReferenceOrigin.SourceResponse"/>. USGS 1 m's own
/// response has been observed, 2026-09-19, to carry no such sidecar: it returns a bare AAIGrid text body
/// instead. For that shape, this source sends a second, otherwise identical request with
/// <c>outputFormat=GTiff</c> and reads only that response's TIFF directory and GeoKeys -- never a pixel,
/// never a decompression -- to recover the horizontal reference and to verify the two responses describe
/// the identical grid, then declares the vertical reference from the dataset's own published documentation
/// (origins <see cref="ReferenceOrigin.SourceMetadataResponse"/>/<see cref="ReferenceOrigin.DatasetDocumentation"/>).
/// See docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified 2026-09-19" and
/// "GeoKey to WKT synthesis" sections. Every acquisition therefore costs either one or two API calls against
/// the configured key's daily quota. This source never retries with a different dataset or output format,
/// and it never sends more than the two requests documented above, in any failure path.
/// </remarks>
public sealed class OpenTopographyUsgs1mSource : IElevationSource
{
    /// <summary>The <c>datasetName</c> value this source always requests.</summary>
    public const string DatasetName = "USGS1m";

    /// <summary>The <c>outputFormat</c> value the data request always sends. OpenTopography's default is GTiff, so this must always be sent explicitly.</summary>
    public const string OutputFormat = "AAIGrid";

    /// <summary>
    /// The <c>outputFormat</c> value the second, metadata-only request sends when the data response carried
    /// no reference metadata of its own. See docs/architecture/opentopography-usgs1m-source.md's
    /// "Two-request contract, verified 2026-09-19" section.
    /// </summary>
    public const string MetadataOutputFormat = "GTiff";

    /// <summary>
    /// The maximum allowed disagreement, in the raster's own horizontal units, between the AAIGrid data
    /// response's lower-left corner and the one derived from the GeoTIFF metadata response's tiepoint and
    /// pixel scale. See docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified
    /// 2026-09-19" section for why a small tolerance -- rather than exact equality -- is required here even
    /// though every other same-grid check compares its values exactly.
    /// </summary>
    public const double GridAgreementTolerance = 1e-6;

    /// <summary>
    /// The minimum latitude, in degrees north, at which a UTM zone 19N GeoTIFF result is accepted. Zone 19N
    /// (EPSG 26919, 3749, 3726, and 6348) is the one <see cref="NorthAmericanUtmWellKnownText"/> zone that is
    /// not entirely within the conterminous United States: it also covers Puerto Rico (roughly 17.9-18.5
    /// degrees north), where the declared NAVD88 vertical reference
    /// (<see cref="OpenTopographyUsgs1mSourceOptions.DeclaredVerticalReference"/>) is not established -- see
    /// docs/architecture/opentopography-usgs1m-source.md's "CONUS scope and its rationale" section. A request
    /// bounding box entirely south of the Florida Keys (roughly 24.5 degrees north) cannot be a conterminous
    /// United States location, so <see cref="ValidateGeoTiffMetadata"/> rejects a zone 19N result there rather
    /// than assume; every conterminous United States latitude zone 19N also reaches (for example Maine)
    /// remains accepted.
    /// </summary>
    public const double MinimumConusLatitudeForZone19N = 24.5;

    private const string DatasetNameParameter = "datasetName";
    private const string SouthParameter = "south";
    private const string NorthParameter = "north";
    private const string WestParameter = "west";
    private const string EastParameter = "east";
    private const string OutputFormatParameter = "outputFormat";
    private const string ApiKeyParameter = "API_Key";
    private const double KilometersPerDegreeOfLatitude = 111.32d;

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] GzipSignature = [0x1F, 0x8B];
    private static readonly byte[] LittleEndianTiffSignature = [0x49, 0x49, 0x2A, 0x00];
    private static readonly byte[] BigEndianTiffSignature = [0x4D, 0x4D, 0x00, 0x2A];

    private readonly HttpClient httpClient;
    private readonly IOpenTopographyApiKeyProvider apiKeyProvider;
    private readonly OpenTopographyUsgs1mSourceOptions options;

    /// <summary>
    /// Creates a source over an externally owned <see cref="HttpClient"/>. This type never disposes
    /// <paramref name="httpClient"/>; its owner remains responsible for its lifetime.
    /// </summary>
    public OpenTopographyUsgs1mSource(
        HttpClient httpClient,
        IOpenTopographyApiKeyProvider apiKeyProvider,
        OpenTopographyUsgs1mSourceOptions? options = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        this.options = options ?? new OpenTopographyUsgs1mSourceOptions();
    }

    /// <summary>Acquires elevation data, discarding the diagnostic evidence <see cref="AcquireDetailedAsync"/> retains.</summary>
    public async ValueTask<ElevationAcquisition> AcquireAsync(ElevationSourceRequest request, CancellationToken cancellationToken = default)
    {
        OpenTopographyUsgs1mAcquisition detailed = await AcquireDetailedAsync(request, cancellationToken).ConfigureAwait(false);
        return detailed.Acquisition;
    }

    /// <summary>
    /// Acquires elevation data along with the response evidence used to interpret it. Sends one HTTP request
    /// when the response already carries its own reference metadata, or two -- the second requesting
    /// <see cref="MetadataOutputFormat"/> for the identical area -- when it does not; see this type's own
    /// remarks.
    /// </summary>
    /// <exception cref="OpenTopographyRequestValidationException">
    /// The area of interest is not a WGS 84 bounding box, its bounds are invalid, its approximate area
    /// exceeds the configured limit, or OpenTopography rejected the request as malformed.
    /// </exception>
    /// <exception cref="OpenTopographyAuthorizationException">No API key is configured, or OpenTopography rejected it or denied dataset access.</exception>
    /// <exception cref="OpenTopographyQuotaException">OpenTopography appears to have reported a rate limit or quota condition.</exception>
    /// <exception cref="OpenTopographyNoDataException">OpenTopography reported no data for the requested area.</exception>
    /// <exception cref="OpenTopographyServerException">OpenTopography reported a server-side error.</exception>
    /// <exception cref="OpenTopographyNetworkException">A transport-level failure or timeout occurred, on either request.</exception>
    /// <exception cref="OpenTopographyUnexpectedResponseException">A response could not be classified.</exception>
    /// <exception cref="OpenTopographySourceMetadataException">Neither response carried usable, mutually agreeing coordinate reference metadata.</exception>
    public async ValueTask<OpenTopographyUsgs1mAcquisition> AcquireDetailedAsync(
        ElevationSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AreaOfInterest is not Wgs84BoundingBoxAoi box)
        {
            throw new OpenTopographyRequestValidationException(
                "OpenTopography's usgsdem endpoint accepts only a WGS 84 bounding box area of interest. " +
                "Convert a radius or parcel area of interest to a WGS 84 bounding box before requesting this source.",
                OpenTopographyRedaction.RedactUri(options.EndpointUri));
        }

        // Defensive: Wgs84BoundingBoxAoi's own constructor already enforces south < north and west < east,
        // so this branch is not currently reachable through the public AOI contract. It is kept because the
        // request-validation exception is explicitly specified to cover invalid bounds.
        if (box.SouthLatitude >= box.NorthLatitude || box.WestLongitude >= box.EastLongitude)
        {
            throw new OpenTopographyRequestValidationException(
                "The requested bounding box is invalid: south and west bounds must be less than north and east bounds.",
                OpenTopographyRedaction.RedactUri(BuildRequestUri(box, apiKey: null, OutputFormat)));
        }

        double approximateAreaSquareKilometers = ApproximateAreaSquareKilometers(box);
        if (approximateAreaSquareKilometers > options.MaximumAreaSquareKilometers)
        {
            string observedArea = approximateAreaSquareKilometers.ToString("F1", CultureInfo.InvariantCulture);
            string maximumArea = options.MaximumAreaSquareKilometers.ToString("F1", CultureInfo.InvariantCulture);
            throw new OpenTopographyRequestValidationException(
                $"The requested area is approximately {observedArea} square kilometers, which exceeds the configured " +
                $"limit of {maximumArea} square kilometers (USGS1m's documented per-request limit is 250 square " +
                "kilometers). Request a smaller area.",
                OpenTopographyRedaction.RedactUri(BuildRequestUri(box, apiKey: null, OutputFormat)));
        }

        OpenTopographyApiKey? apiKey = apiKeyProvider.GetApiKey();
        if (apiKey is null)
        {
            throw new OpenTopographyAuthorizationException(
                "No OpenTopography API key is configured. Set OPENTOPOGRAPHY_API_KEY in the process environment, " +
                "or supply an IOpenTopographyApiKeyProvider that returns one, before requesting this source.",
                OpenTopographyRedaction.RedactUri(BuildRequestUri(box, apiKey: null, OutputFormat)),
                OpenTopographyAuthorizationFailure.ApiKeyMissing,
                statusCode: null,
                serverMessage: null);
        }

        Uri requestUri = BuildRequestUri(box, apiKey, OutputFormat);
        string redactedRequestUri = OpenTopographyRedaction.RedactUri(requestUri);

        HttpResponseMessage response = await SendGetAsync(requestUri, redactedRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
        try
        {
            return await HandleResponseAsync(response, redactedRequestUri, apiKey, box, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private Uri BuildRequestUri(Wgs84BoundingBoxAoi box, OpenTopographyApiKey? apiKey, string outputFormat)
    {
        var builder = new StringBuilder(options.EndpointUri.ToString());
        builder.Append('?').Append(DatasetNameParameter).Append('=').Append(DatasetName);
        builder.Append('&').Append(SouthParameter).Append('=').Append(FormatCoordinate(box.SouthLatitude));
        builder.Append('&').Append(NorthParameter).Append('=').Append(FormatCoordinate(box.NorthLatitude));
        builder.Append('&').Append(WestParameter).Append('=').Append(FormatCoordinate(box.WestLongitude));
        builder.Append('&').Append(EastParameter).Append('=').Append(FormatCoordinate(box.EastLongitude));
        builder.Append('&').Append(OutputFormatParameter).Append('=').Append(outputFormat);
        if (apiKey is not null)
        {
            builder.Append('&').Append(ApiKeyParameter).Append('=').Append(Uri.EscapeDataString(apiKey.RawValue));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static string FormatCoordinate(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double ApproximateAreaSquareKilometers(Wgs84BoundingBoxAoi box)
    {
        double meanLatitudeRadians = double.DegreesToRadians((box.SouthLatitude + box.NorthLatitude) / 2d);
        double widthKilometers = (box.EastLongitude - box.WestLongitude) * KilometersPerDegreeOfLatitude * Math.Cos(meanLatitudeRadians);
        double heightKilometers = (box.NorthLatitude - box.SouthLatitude) * KilometersPerDegreeOfLatitude;
        return Math.Abs(widthKilometers * heightKilometers);
    }

    /// <summary>
    /// Sends one GET request and translates a transport-level failure (a connection error or a timeout) into
    /// <see cref="OpenTopographyNetworkException"/>, exactly the same way regardless of which of this
    /// source's (at most two) requests it is sending. Never disposes the returned response on the success
    /// path; the caller owns its lifetime from there.
    /// </summary>
    private async ValueTask<HttpResponseMessage> SendGetAsync(
        Uri requestUri, string redactedRequestUri, OpenTopographyApiKey apiKey, CancellationToken cancellationToken)
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
            // ex.Message is never interpolated into the outer message here (it is a fixed string), but ex
            // itself must still never be attached as InnerException. This type's own doc comment above notes
            // the HttpClient is "externally owned", so a caller-attached DelegatingHandler (logging, retry,
            // or telemetry are idiomatic .NET extension points) can throw a cancellation exception whose own
            // Message embeds the request URI, and Exception.ToString() recurses into InnerException.Message,
            // so the raw key would otherwise survive in OpenTopographyNetworkException.ToString() even though
            // the outer Message never mentions it. Redact ex.Message and rebuild an exception of the same
            // concrete type from the redacted text, exactly as the WKT catches below already do, preserving
            // the documented InnerException type (TaskCanceledException vs. OperationCanceledException)
            // without resurrecting the leak.
            string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
            Exception redactedInner = ex is TaskCanceledException
                ? new TaskCanceledException(redactedDetail)
                : new OperationCanceledException(redactedDetail);
            throw new OpenTopographyNetworkException(
                "The request to OpenTopography did not complete before it timed out. Check network connectivity and retry.",
                redactedRequestUri,
                redactedInner);
        }
        catch (HttpRequestException ex)
        {
            // The original ex is never attached as InnerException, for the same reason the WKT catches below
            // never attach their own original ex: its Message getter would still return the raw, unredacted
            // text (which can embed the request URI, and therefore the key, when a caller-configured
            // transport handler throws it), and AGENTS.md's "never leaks the key" guarantee covers the whole
            // exception chain, not just the outermost message. A new HttpRequestException built from the
            // already-redacted text preserves the documented InnerException type without the leak.
            string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
            throw new OpenTopographyNetworkException(
                $"The request to OpenTopography failed before a response was received: {redactedDetail} Check network connectivity and retry.",
                redactedRequestUri,
                new HttpRequestException(redactedDetail));
        }
        catch (IOException ex)
        {
            // Same rationale as the HttpRequestException catch above.
            string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
            throw new OpenTopographyNetworkException(
                $"An I/O error occurred while communicating with OpenTopography: {redactedDetail} Retry the request.",
                redactedRequestUri,
                new IOException(redactedDetail));
        }
    }

    private async ValueTask<OpenTopographyUsgs1mAcquisition> HandleResponseAsync(
        HttpResponseMessage response,
        string redactedRequestUri,
        OpenTopographyApiKey apiKey,
        Wgs84BoundingBoxAoi box,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return await HandleSuccessAsync(response, redactedRequestUri, apiKey, box, cancellationToken).ConfigureAwait(false);
        }

        throw await ClassifyNonSuccessResponseAsync(response, redactedRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Classifies any non-200 response into the matching <see cref="OpenTopographyException"/>, without
    /// throwing it, so both the data request and the metadata request can reuse the identical classification
    /// logic while attaching their own respective redacted request URI. Shared by
    /// <see cref="HandleResponseAsync"/> (the data request) and <see cref="HandleBareAaiGridAsync"/> (the
    /// metadata request).
    /// </summary>
    private async ValueTask<OpenTopographyException> ClassifyNonSuccessResponseAsync(
        HttpResponseMessage response,
        string redactedRequestUri,
        OpenTopographyApiKey apiKey,
        CancellationToken cancellationToken)
    {
        HttpStatusCode statusCode = response.StatusCode;

        if (statusCode == HttpStatusCode.NoContent)
        {
            return new OpenTopographyNoDataException(
                "OpenTopography reported no elevation data for the requested area (HTTP 204 No Data). " +
                "Choose a different area, or confirm the dataset covers this location.",
                redactedRequestUri,
                statusCode);
        }

        (byte[] rawBody, bool bodyTruncated) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        string rawBodyText = DecodeUtf8(rawBody);
        string serverMessage = BuildServerMessage(rawBodyText, apiKey, bodyTruncated);
        if (bodyTruncated)
        {
            serverMessage = AppendTruncationNote(serverMessage);
        }

        bool isQuotaEligibleStatus = statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest;
        if (isQuotaEligibleStatus && (statusCode == HttpStatusCode.TooManyRequests || MentionsQuota(rawBodyText)))
        {
            string statusText = ((int)statusCode).ToString(CultureInfo.InvariantCulture);
            return new OpenTopographyQuotaException(
                $"OpenTopography reported what appears to be a rate limit or quota condition (HTTP {statusText}). " +
                "OpenTopography does not document a rate-limit response shape, so this classification is best effort. " +
                $"Wait and retry later, or reduce request frequency. Server message: {serverMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            OpenTopographyAuthorizationFailure failure = MentionsDatasetAccess(rawBodyText)
                ? OpenTopographyAuthorizationFailure.DatasetAccessDenied
                : OpenTopographyAuthorizationFailure.ApiKeyRejected;
            string message = failure == OpenTopographyAuthorizationFailure.DatasetAccessDenied
                ? $"OpenTopography denied access to the USGS1m dataset (HTTP 401). USGS 1 m access requires academic " +
                  $"authorization or an enterprise API key. Server message: {serverMessage}"
                : $"OpenTopography rejected the configured API key (HTTP 401). Verify OPENTOPOGRAPHY_API_KEY is current " +
                  $"and correctly registered. Server message: {serverMessage}";
            return new OpenTopographyAuthorizationException(message, redactedRequestUri, failure, statusCode, serverMessage);
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return new OpenTopographyAuthorizationException(
                $"OpenTopography denied access to the requested resource (HTTP 403). USGS 1 m access requires academic " +
                $"authorization or an enterprise API key. Server message: {serverMessage}",
                redactedRequestUri,
                OpenTopographyAuthorizationFailure.DatasetAccessDenied,
                statusCode,
                serverMessage);
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return new OpenTopographyRequestValidationException(
                $"OpenTopography rejected the request as malformed (HTTP 400). Review the requested area and " +
                $"parameters. Server message: {serverMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        int statusCodeValue = (int)statusCode;
        if (statusCodeValue is >= 500 and <= 599)
        {
            string statusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
            return new OpenTopographyServerException(
                $"OpenTopography reported a server-side error (HTTP {statusText}). This is not a problem with the " +
                $"request; retry later. Server message: {serverMessage}",
                redactedRequestUri,
                statusCode,
                serverMessage);
        }

        string undocumentedStatusText = statusCodeValue.ToString(CultureInfo.InvariantCulture);
        return new OpenTopographyUnexpectedResponseException(
            $"OpenTopography returned an undocumented status code (HTTP {undocumentedStatusText}) not described by " +
            $"its OpenAPI definition. Server message: {serverMessage}",
            redactedRequestUri,
            statusCode,
            serverMessage);
    }

    private async ValueTask<OpenTopographyUsgs1mAcquisition> HandleSuccessAsync(
        HttpResponseMessage response,
        string redactedRequestUri,
        OpenTopographyApiKey apiKey,
        Wgs84BoundingBoxAoi box,
        CancellationToken cancellationToken)
    {
        string? contentType = response.Content.Headers.ContentType?.ToString();
        string? contentDispositionFileName = response.Content.Headers.ContentDisposition?.FileName;

        (byte[] body, bool bodyTruncated) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        if (bodyTruncated)
        {
            throw new OpenTopographyUnexpectedResponseException(
                ByteLimitExceededMessage(),
                redactedRequestUri,
                response.StatusCode,
                null);
        }

        if (StartsWith(body, ZipSignature))
        {
            return ParseZipResponse(body, redactedRequestUri, response.StatusCode, contentType, contentDispositionFileName, apiKey);
        }

        if (StartsWith(body, GzipSignature))
        {
            throw new OpenTopographyUnexpectedResponseException(
                "OpenTopography returned a gzip-compressed response. This source only understands a bare AAIGrid " +
                "text body or a zip archive containing one. Confirm outputFormat=AAIGrid was sent, or report this as " +
                "a new OpenTopography response packaging.",
                redactedRequestUri,
                response.StatusCode,
                null);
        }

        string text = DecodeUtf8(body);
        string firstToken = FirstToken(text);
        if (firstToken.Equals("ncols", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleBareAaiGridAsync(
                text, box, apiKey, redactedRequestUri, response.StatusCode, contentType, contentDispositionFileName,
                body.LongLength, cancellationToken).ConfigureAwait(false);
        }

        string snippet = CollapseWhitespace(StripTags(OpenTopographyRedaction.RedactText(text, apiKey)));
        string snippetContentType = OpenTopographyRedaction.RedactText(contentType ?? "(none)", apiKey);
        throw new OpenTopographyUnexpectedResponseException(
            $"OpenTopography returned a 200 response this source does not recognize as a zip archive or a bare " +
            $"AAIGrid body (content type '{snippetContentType}'). Observed content: {snippet}",
            redactedRequestUri,
            response.StatusCode,
            snippet);
    }

    /// <summary>
    /// Handles a bare AAIGrid data response that carried no reference metadata of its own (the observed USGS
    /// 1 m behaviour): reads only the header to learn the grid's shape and anchor, requests
    /// <see cref="MetadataOutputFormat"/> for the identical area, reads only that response's TIFF directory
    /// and GeoKeys, verifies the two responses describe the identical grid, synthesizes WKT from the
    /// GeoKeys' EPSG code and the declared vertical reference, and finally parses the original AAIGrid text
    /// with that reference. See docs/architecture/opentopography-usgs1m-source.md's "Two-request contract,
    /// verified 2026-09-19" and "GeoKey to WKT synthesis" sections.
    /// </summary>
    private async ValueTask<OpenTopographyUsgs1mAcquisition> HandleBareAaiGridAsync(
        string text,
        Wgs84BoundingBoxAoi box,
        OpenTopographyApiKey apiKey,
        string redactedRequestUri,
        HttpStatusCode statusCode,
        string? contentType,
        string? contentDispositionFileName,
        long responseByteCount,
        CancellationToken cancellationToken)
    {
        AaiGridHeader header;
        try
        {
            header = AaiGridParser.ReadHeader(new StringReader(text));
        }
        catch (FormatException ex)
        {
            string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
            throw new OpenTopographySourceMetadataException(
                $"OpenTopography's AAIGrid response header could not be parsed: {redactedDetail}",
                redactedRequestUri,
                new FormatException(redactedDetail));
        }

        Uri metadataRequestUri = BuildRequestUri(box, apiKey, MetadataOutputFormat);
        string redactedMetadataRequestUri = OpenTopographyRedaction.RedactUri(metadataRequestUri);

        HttpResponseMessage metadataResponse = await SendGetAsync(metadataRequestUri, redactedMetadataRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
        try
        {
            if (metadataResponse.StatusCode != HttpStatusCode.OK)
            {
                throw await ClassifyNonSuccessResponseAsync(metadataResponse, redactedMetadataRequestUri, apiKey, cancellationToken).ConfigureAwait(false);
            }

            string? metadataContentType = metadataResponse.Content.Headers.ContentType?.ToString();
            string? metadataContentDispositionFileName = metadataResponse.Content.Headers.ContentDisposition?.FileName;

            (byte[] metadataBody, bool metadataBodyTruncated) = await ReadBodyAsync(metadataResponse, cancellationToken).ConfigureAwait(false);
            if (metadataBodyTruncated)
            {
                throw new OpenTopographyUnexpectedResponseException(
                    ByteLimitExceededMessage(),
                    redactedMetadataRequestUri,
                    metadataResponse.StatusCode,
                    null);
            }

            if (!StartsWith(metadataBody, LittleEndianTiffSignature) && !StartsWith(metadataBody, BigEndianTiffSignature))
            {
                throw new OpenTopographyUnexpectedResponseException(
                    "OpenTopography's metadata request (outputFormat=GTiff) did not return a response this source " +
                    "recognizes as GeoTIFF; expected a GeoTIFF metadata response.",
                    redactedMetadataRequestUri,
                    metadataResponse.StatusCode,
                    null);
            }

            GeoTiffMetadata metadata;
            try
            {
                metadata = GeoTiffMetadataReader.Read(metadataBody);
            }
            catch (FormatException ex)
            {
                string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's GeoTIFF metadata response could not be parsed: {redactedDetail}",
                    redactedMetadataRequestUri,
                    new FormatException(redactedDetail));
            }

            ValidateGeoTiffMetadata(metadata, box, redactedMetadataRequestUri, apiKey);
            EnsureGridsAgree(header, metadata, redactedMetadataRequestUri);

            EnsureOptionalIdentifierDoesNotEchoApiKey("GeoTIFF citation (GeoKey 1026)", metadata.Citation, apiKey, redactedMetadataRequestUri);
            EnsureOptionalIdentifierDoesNotEchoApiKey("GeoTIFF geographic citation (GeoKey 2049)", metadata.GeographicCitation, apiKey, redactedMetadataRequestUri);
            EnsureOptionalIdentifierDoesNotEchoApiKey("GeoTIFF projected citation (GeoKey 3073)", metadata.ProjectedCitation, apiKey, redactedMetadataRequestUri);

            int epsgCode = metadata.ProjectedCoordinateSystemCode!.Value;
            string wkt = NorthAmericanUtmWellKnownText.Create(epsgCode, options.DeclaredVerticalReference);

            WellKnownTextReference parsedReference;
            try
            {
                parsedReference = WellKnownTextReferenceParser.Parse(wkt);
            }
            catch (FormatException ex)
            {
                // NorthAmericanUtmWellKnownText.Create is only ever supposed to produce WKT that
                // WellKnownTextReferenceParser.Parse accepts; reaching this catch is a SolidGround defect,
                // not a malformed OpenTopography response, but it still must not surface as a raw,
                // undocumented exception type.
                throw new OpenTopographySourceMetadataException(
                    $"SolidGround's synthesized coordinate reference WKT for EPSG:{epsgCode.ToString(CultureInfo.InvariantCulture)} " +
                    $"could not be parsed: {ex.Message}",
                    redactedMetadataRequestUri,
                    ex);
            }

            ElevationGrid grid;
            try
            {
                grid = AaiGridParser.Parse(new StringReader(text), parsedReference.Horizontal, parsedReference.Vertical!);
            }
            catch (FormatException ex)
            {
                string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's AAIGrid response could not be parsed: {redactedDetail}",
                    redactedRequestUri,
                    new FormatException(redactedDetail));
            }

            var acquisition = new ElevationAcquisition(grid, new ElevationSourceMetadata("OpenTopography", DatasetName));

            string? redactedCitation = metadata.Citation is null ? null : OpenTopographyRedaction.RedactText(metadata.Citation, apiKey);
            var metadataRequestEvidence = new OpenTopographyMetadataRequestEvidence(
                redactedMetadataRequestUri,
                metadataResponse.StatusCode,
                metadataContentType is null ? null : OpenTopographyRedaction.RedactText(metadataContentType, apiKey),
                metadataContentDispositionFileName is null ? null : OpenTopographyRedaction.RedactText(metadataContentDispositionFileName, apiKey),
                metadataBody.LongLength,
                epsgCode,
                redactedCitation,
                metadata.RasterType == 2 ? "PixelIsPoint" : "PixelIsArea",
                metadata.ImageWidth,
                metadata.ImageLength);

            var evidence = new OpenTopographyResponseEvidence(
                redactedRequestUri,
                statusCode,
                contentType is null ? null : OpenTopographyRedaction.RedactText(contentType, apiKey),
                contentDispositionFileName is null ? null : OpenTopographyRedaction.RedactText(contentDispositionFileName, apiKey),
                [],
                OpenTopographyReferenceSource.GeoTiffGeoKeys,
                OpenTopographyRedaction.RedactText(wkt, apiKey, maximumLength: int.MaxValue),
                responseByteCount,
                ReferenceOrigin.SourceMetadataResponse,
                ReferenceOrigin.DatasetDocumentation,
                metadataRequestEvidence);

            return new OpenTopographyUsgs1mAcquisition(acquisition, evidence);
        }
        finally
        {
            metadataResponse.Dispose();
        }
    }

    /// <summary>
    /// Validates that a GeoTIFF metadata response carries every GeoKey and tag SolidGround needs, and that
    /// each one is a value this source supports. See docs/architecture/opentopography-usgs1m-source.md's
    /// "GeoKey to WKT synthesis" section for why exactly this set is required. Also rejects a UTM zone 19N
    /// result when <paramref name="box"/> lies entirely outside the conterminous United States, because zone
    /// 19N also covers Puerto Rico; see <see cref="MinimumConusLatitudeForZone19N"/> and "CONUS scope and its
    /// rationale" in the same design note.
    /// </summary>
    private static void ValidateGeoTiffMetadata(GeoTiffMetadata metadata, Wgs84BoundingBoxAoi box, string redactedMetadataRequestUri, OpenTopographyApiKey apiKey)
    {
        if (metadata.ModelType is not 1)
        {
            throw new OpenTopographySourceMetadataException(
                $"GeoTIFF GeoKey 1024 (GTModelTypeGeoKey) is {DescribeNullableUshort(metadata.ModelType)}; " +
                "SolidGround requires a projected model (1).",
                redactedMetadataRequestUri);
        }

        if (metadata.ProjectedCoordinateSystemCode is not ushort projectedCode)
        {
            throw new OpenTopographySourceMetadataException(
                "GeoTIFF GeoKey 3072 (ProjectedCSTypeGeoKey) is missing; SolidGround requires an EPSG projected " +
                "coordinate system code.",
                redactedMetadataRequestUri);
        }

        if (!NorthAmericanUtmWellKnownText.IsSupported(projectedCode))
        {
            string codeText = projectedCode.ToString(CultureInfo.InvariantCulture);
            throw new OpenTopographySourceMetadataException(
                $"EPSG:{codeText} is not a supported projected coordinate system. SolidGround supports UTM zones " +
                "10N to 19N (the conterminous United States) on NAD83, NAD83(HARN), NAD83(NSRS2007), and " +
                "NAD83(2011); other zones and families are not verified and fail rather than assume.",
                redactedMetadataRequestUri);
        }

        if (NorthAmericanUtmWellKnownText.ZoneOf(projectedCode) == 19 && box.NorthLatitude < MinimumConusLatitudeForZone19N)
        {
            string codeText = projectedCode.ToString(CultureInfo.InvariantCulture);
            string thresholdText = MinimumConusLatitudeForZone19N.ToString(CultureInfo.InvariantCulture);
            throw new OpenTopographySourceMetadataException(
                $"EPSG:{codeText} is UTM zone 19N, which also covers Puerto Rico outside the conterminous " +
                $"United States; the request's bounding box lies entirely south of {thresholdText} degrees " +
                "north, so SolidGround's declared NAVD88 vertical reference is not established there and this " +
                "fails rather than assume.",
                redactedMetadataRequestUri);
        }

        if (metadata.LinearUnitsCode is ushort linearUnitsCode && linearUnitsCode != 9001)
        {
            throw new OpenTopographySourceMetadataException(
                $"GeoTIFF GeoKey 3076 (ProjLinearUnitsGeoKey) is {linearUnitsCode.ToString(CultureInfo.InvariantCulture)}; " +
                "SolidGround requires metre (9001) or an absent key.",
                redactedMetadataRequestUri);
        }

        if (metadata.RasterType is not (1 or 2))
        {
            throw new OpenTopographySourceMetadataException(
                $"GeoTIFF GeoKey 1025 (GTRasterTypeGeoKey) is {DescribeNullableUshort(metadata.RasterType)}; " +
                "SolidGround requires PixelIsArea (1) or PixelIsPoint (2).",
                redactedMetadataRequestUri);
        }

        if (metadata.ModelPixelScale is null)
        {
            throw new OpenTopographySourceMetadataException(
                "GeoTIFF tag 33550 (ModelPixelScaleTag) is missing.",
                redactedMetadataRequestUri);
        }

        if (metadata.ModelTiepoint is null)
        {
            throw new OpenTopographySourceMetadataException(
                "GeoTIFF tag 33922 (ModelTiepointTag) is missing.",
                redactedMetadataRequestUri);
        }

        if (metadata.NoDataText is null)
        {
            throw new OpenTopographySourceMetadataException(
                "GeoTIFF tag 42113 (GDAL_NODATA) is missing.",
                redactedMetadataRequestUri);
        }

        if (!double.TryParse(metadata.NoDataText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            string redactedNoDataText = OpenTopographyRedaction.RedactText(metadata.NoDataText, apiKey);
            throw new OpenTopographySourceMetadataException(
                $"GeoTIFF tag 42113 (GDAL_NODATA) value '{redactedNoDataText}' could not be parsed as an " +
                "invariant-culture number.",
                redactedMetadataRequestUri);
        }
    }

    /// <summary>
    /// Verifies the AAIGrid data response and the GeoTIFF metadata response describe the identical grid:
    /// matching dimensions, matching cell size, matching NODATA sentinel, and a lower-left corner that
    /// agrees within <see cref="GridAgreementTolerance"/>. Assumes <see cref="ValidateGeoTiffMetadata"/> has
    /// already confirmed <paramref name="metadata"/> carries every value this method reads. See
    /// docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified 2026-09-19"
    /// section for the corner-derivation formula.
    /// </summary>
    private static void EnsureGridsAgree(AaiGridHeader header, GeoTiffMetadata metadata, string redactedMetadataRequestUri)
    {
        if (metadata.ImageWidth != (uint)header.ColumnCount)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's ImageWidth ({metadata.ImageWidth.ToString(CultureInfo.InvariantCulture)}) " +
                $"does not match the AAIGrid response's ncols ({header.ColumnCount.ToString(CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        if (metadata.ImageLength != (uint)header.RowCount)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's ImageLength ({metadata.ImageLength.ToString(CultureInfo.InvariantCulture)}) " +
                $"does not match the AAIGrid response's nrows ({header.RowCount.ToString(CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        (double scaleX, double scaleY, _) = metadata.ModelPixelScale!.Value;
        if (scaleX != header.CellSize)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's ModelPixelScale X ({scaleX.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"does not match the AAIGrid response's cellsize ({header.CellSize.ToString("R", CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        if (scaleY != header.CellSize)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's ModelPixelScale Y ({scaleY.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"does not match the AAIGrid response's cellsize ({header.CellSize.ToString("R", CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        (double tiepointI, double tiepointJ, _, double tiepointX, double tiepointY, _) = metadata.ModelTiepoint!.Value;
        bool pixelIsPoint = metadata.RasterType == 2;
        double halfCellShiftX = pixelIsPoint ? scaleX / 2d : 0d;
        double halfCellShiftY = pixelIsPoint ? scaleY / 2d : 0d;
        double upperLeftX = tiepointX - (tiepointI * scaleX) - halfCellShiftX;
        double upperLeftY = tiepointY + (tiepointJ * scaleY) + halfCellShiftY;
        double geoTiffLowerLeftX = upperLeftX;
        double geoTiffLowerLeftY = upperLeftY - (metadata.ImageLength * scaleY);

        double aaiHalfCellShift = header.AnchorConvention == GridAnchorConvention.CellCenter ? header.CellSize / 2d : 0d;
        double aaiGridLowerLeftX = header.AnchorX - aaiHalfCellShift;
        double aaiGridLowerLeftY = header.AnchorY - aaiHalfCellShift;

        if (Math.Abs(geoTiffLowerLeftX - aaiGridLowerLeftX) > GridAgreementTolerance)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's derived lower-left X ({geoTiffLowerLeftX.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"does not agree with the AAIGrid response's lower-left X ({aaiGridLowerLeftX.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"within the configured tolerance ({GridAgreementTolerance.ToString("R", CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        if (Math.Abs(geoTiffLowerLeftY - aaiGridLowerLeftY) > GridAgreementTolerance)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's derived lower-left Y ({geoTiffLowerLeftY.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"does not agree with the AAIGrid response's lower-left Y ({aaiGridLowerLeftY.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"within the configured tolerance ({GridAgreementTolerance.ToString("R", CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }

        double metadataNoData = double.Parse(metadata.NoDataText!, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (metadataNoData != header.NoDataValue)
        {
            throw new OpenTopographySourceMetadataException(
                $"The GeoTIFF metadata's GDAL_NODATA ({metadataNoData.ToString("R", CultureInfo.InvariantCulture)}) " +
                $"does not match the AAIGrid response's NODATA_value ({header.NoDataValue.ToString("R", CultureInfo.InvariantCulture)}).",
                redactedMetadataRequestUri);
        }
    }

    private static string DescribeNullableUshort(ushort? value) =>
        value is ushort actual ? actual.ToString(CultureInfo.InvariantCulture) : "absent";

    private static OpenTopographyUsgs1mAcquisition ParseZipResponse(
        byte[] body,
        string redactedRequestUri,
        HttpStatusCode statusCode,
        string? contentType,
        string? contentDispositionFileName,
        OpenTopographyApiKey apiKey)
    {
        try
        {
            using var zipStream = new MemoryStream(body, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Entry names are server-controlled zip metadata, not request/response body text, but
            // OpenTopography is documented to echo a submitted key back in other response shapes, so they
            // are redacted here, once, before being used to build any exception message or the response
            // evidence below.
            List<string> entryNames = [.. archive.Entries.Select(entry => OpenTopographyRedaction.RedactText(entry.FullName, apiKey))];
            List<ZipArchiveEntry> ascEntries = [.. archive.Entries.Where(entry => entry.FullName.EndsWith(".asc", StringComparison.OrdinalIgnoreCase))];

            if (ascEntries.Count != 1)
            {
                string countText = ascEntries.Count.ToString(CultureInfo.InvariantCulture);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's zip response contained {countText} .asc entries, but exactly one is required. " +
                    $"Entries: {DescribeEntries(entryNames)}.",
                    redactedRequestUri);
            }

            ZipArchiveEntry ascEntry = ascEntries[0];
            List<ZipArchiveEntry> prjEntries = [.. archive.Entries.Where(entry => entry.FullName.EndsWith(".prj", StringComparison.OrdinalIgnoreCase))];
            if (prjEntries.Count > 1)
            {
                string prjCountText = prjEntries.Count.ToString(CultureInfo.InvariantCulture);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's zip response contained {prjCountText} .prj entries, but at most one is " +
                    $"supported. Entries: {DescribeEntries(entryNames)}.",
                    redactedRequestUri);
            }

            ZipArchiveEntry? prjEntry = prjEntries.Count == 1 ? prjEntries[0] : null;

            string wellKnownText;
            OpenTopographyReferenceSource referenceSource;
            if (prjEntry is not null)
            {
                wellKnownText = ReadEntryText(prjEntry);
                referenceSource = OpenTopographyReferenceSource.PrjSidecar;
            }
            else
            {
                List<ZipArchiveEntry> auxEntries = [.. archive.Entries.Where(entry => entry.FullName.EndsWith(".aux.xml", StringComparison.OrdinalIgnoreCase))];
                if (auxEntries.Count > 1)
                {
                    string auxCountText = auxEntries.Count.ToString(CultureInfo.InvariantCulture);
                    throw new OpenTopographySourceMetadataException(
                        $"OpenTopography's zip response contained {auxCountText} .aux.xml entries, but at most one " +
                        $"is supported. Entries: {DescribeEntries(entryNames)}.",
                        redactedRequestUri);
                }

                ZipArchiveEntry? auxEntry = auxEntries.Count == 1 ? auxEntries[0] : null;
                if (auxEntry is null)
                {
                    throw new OpenTopographySourceMetadataException(
                        "OpenTopography's zip response carried no .prj or .aux.xml sidecar with coordinate reference " +
                        $"metadata. Entries: {DescribeEntries(entryNames)}. SolidGround will not assume a coordinate " +
                        "reference system.",
                        redactedRequestUri);
                }

                string auxText = ReadEntryText(auxEntry);
                string redactedAuxEntryName = OpenTopographyRedaction.RedactText(auxEntry.FullName, apiKey);
                wellKnownText = ExtractSrsElement(auxText) ?? throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's .aux.xml sidecar '{redactedAuxEntryName}' contained no <SRS> element. " +
                    $"Entries: {DescribeEntries(entryNames)}.",
                    redactedRequestUri);
                referenceSource = OpenTopographyReferenceSource.AuxXmlSidecar;
            }

            if (string.IsNullOrWhiteSpace(wellKnownText))
            {
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's coordinate reference sidecar was empty. Entries: {DescribeEntries(entryNames)}.",
                    redactedRequestUri);
            }

            WellKnownTextReference reference;
            try
            {
                reference = WellKnownTextReferenceParser.Parse(wellKnownText);
            }
            catch (FormatException ex)
            {
                // WellKnownTextReferenceParser is deliberately generic and OpenTopography-agnostic (it has no
                // concept of an API key), and its messages can quote arbitrary text straight out of the
                // (fully server-controlled) WKT sidecar, such as an unrecognized unit or datum name. Redact
                // ex.Message here, at the OpenTopography-aware call site, the same way BuildServerMessage
                // already redacts response body text. The original ex is never attached as InnerException:
                // its own Message getter would still return the raw, unredacted text, and AGENTS.md's "never
                // leaks the key" guarantee covers the whole exception chain, not just the outermost message.
                // A new exception of the same type, built from the already-redacted text, preserves the
                // failure kind for diagnosis without resurrecting the leak.
                string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's coordinate reference metadata could not be parsed as WKT: {redactedDetail}",
                    redactedRequestUri,
                    new FormatException(redactedDetail));
            }
            catch (ArgumentException ex)
            {
                // Defense in depth: WellKnownTextReferenceParser.Parse itself rejects a blank name or datum
                // with FormatException, but if a future edge case still lets one reach HorizontalReference's
                // or VerticalReference's own validation, that surfaces as ArgumentException. Wrap it the same
                // way as a FormatException so an undocumented exception type never escapes this source's
                // documented OpenTopographyException hierarchy, redacting ex.Message (and rebuilding a
                // same-type inner exception from the redacted text, for the same reason as above) so the key
                // cannot survive anywhere in the exception chain.
                string redactedDetail = OpenTopographyRedaction.RedactText(ex.Message, apiKey);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's coordinate reference metadata could not be parsed as WKT: {redactedDetail}",
                    redactedRequestUri,
                    new ArgumentException(redactedDetail));
            }

            // A legitimate WKT coordinate reference system or datum name can never equal or contain the
            // caller's own high-entropy API key. This class's own doc comment above already documents that
            // OpenTopography has been observed to echo a rejected key back in other response shapes, and this
            // server-controlled WKT sidecar is no more trustworthy than those. Reject it here, before it can
            // reach the returned ElevationGrid's public HorizontalReference/VerticalReference: those are plain
            // records with no ToString() override (unlike OpenTopographyApiKey), so their properties would
            // otherwise carry the raw key, verbatim, into the successful, caller-facing acquisition result
            // (and from there into provenance and exports) rather than just a discardable diagnostic.
            EnsureIdentifierDoesNotEchoApiKey("coordinate reference system name", reference.Horizontal.CoordinateReferenceSystem, apiKey, redactedRequestUri);
            EnsureIdentifierDoesNotEchoApiKey("horizontal datum name", reference.Horizontal.Datum, apiKey, redactedRequestUri);

            if (reference.Vertical is null)
            {
                throw new OpenTopographySourceMetadataException(
                    "OpenTopography's coordinate reference metadata described a horizontal reference but no vertical " +
                    "coordinate system. SolidGround requires both to interpret elevation values.",
                    redactedRequestUri);
            }

            EnsureIdentifierDoesNotEchoApiKey("vertical datum name", reference.Vertical.Datum, apiKey, redactedRequestUri);

            ElevationGrid grid;
            try
            {
                using Stream ascStream = ascEntry.Open();
                using var ascReader = new StreamReader(ascStream, Encoding.UTF8);
                grid = AaiGridParser.Parse(ascReader, reference.Horizontal, reference.Vertical);
            }
            catch (FormatException ex)
            {
                string redactedAscEntryName = OpenTopographyRedaction.RedactText(ascEntry.FullName, apiKey);
                throw new OpenTopographySourceMetadataException(
                    $"OpenTopography's raster entry '{redactedAscEntryName}' could not be parsed: {ex.Message}",
                    redactedRequestUri,
                    ex);
            }

            var acquisition = new ElevationAcquisition(grid, new ElevationSourceMetadata("OpenTopography", DatasetName));
            // The evidence record is explicitly documented as something a caller may log or inspect, so every
            // field it stores must already be redacted; parsing above intentionally used the unredacted
            // wellKnownText, and wellKnownText's redacted copy disables truncation (maximumLength:
            // int.MaxValue) because, unlike a bounded exception message, this evidence is meant to remain a
            // complete, non-secret record of what the response actually carried. This single-request path
            // carries its own reference metadata end to end, so both origins are SourceResponse and there is
            // no metadata request to report.
            var evidence = new OpenTopographyResponseEvidence(
                redactedRequestUri,
                statusCode,
                contentType is null ? null : OpenTopographyRedaction.RedactText(contentType, apiKey),
                contentDispositionFileName is null ? null : OpenTopographyRedaction.RedactText(contentDispositionFileName, apiKey),
                entryNames,
                referenceSource,
                OpenTopographyRedaction.RedactText(wellKnownText, apiKey, maximumLength: int.MaxValue),
                body.LongLength,
                ReferenceOrigin.SourceResponse,
                ReferenceOrigin.SourceResponse,
                null);

            return new OpenTopographyUsgs1mAcquisition(acquisition, evidence);
        }
        catch (InvalidDataException ex)
        {
            throw new OpenTopographySourceMetadataException(
                "OpenTopography's response had zip magic bytes but could not be read as a valid zip archive. " +
                "The response may be truncated or corrupted.",
                redactedRequestUri,
                ex);
        }
    }

    /// <summary>
    /// Reads the response body up to <see cref="OpenTopographyUsgs1mSourceOptions.MaximumResponseBytes"/>.
    /// Never throws on its own when the cap is reached: the caller already knows the response's status
    /// code and must decide, using that status code, which exception type to raise for a body that was
    /// cut off. This keeps a status-appropriate exception (for example <see cref="OpenTopographyAuthorizationException"/>
    /// for a 401) from being pre-empted by a generic <see cref="OpenTopographyUnexpectedResponseException"/>
    /// purely because a caller-configured limit happened to be smaller than that response's error body.
    /// </summary>
    private async ValueTask<(byte[] Body, bool Truncated)> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > options.MaximumResponseBytes)
            {
                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    private string ByteLimitExceededMessage()
    {
        string limitText = options.MaximumResponseBytes.ToString(CultureInfo.InvariantCulture);
        return $"OpenTopography's response exceeded the configured {limitText} byte limit before it finished. " +
            "Increase OpenTopographyUsgs1mSourceOptions.MaximumResponseBytes if a larger response is " +
            "expected, or investigate why the response is unusually large.";
    }

    private string AppendTruncationNote(string serverMessage)
    {
        string limitText = options.MaximumResponseBytes.ToString(CultureInfo.InvariantCulture);
        return $"{serverMessage} (response truncated after it exceeded the configured {limitText} byte limit)";
    }

    private static bool StartsWith(byte[] body, byte[] signature)
    {
        if (body.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (body[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        return Encoding.UTF8.GetString(span);
    }

    private static string FirstToken(string text)
    {
        int index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        int start = index;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return text[start..index];
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using Stream entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string? ExtractSrsElement(string auxXmlText)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(auxXmlText);
        }
        catch (XmlException)
        {
            return null;
        }

        string? value = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals("SRS", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string DescribeEntries(List<string> entryNames) =>
        entryNames.Count == 0 ? "(none)" : string.Join(", ", entryNames);

    /// <summary>
    /// Throws <see cref="OpenTopographySourceMetadataException"/> when <paramref name="value"/> equals or
    /// contains the configured API key's raw or escaped value. The exception message names only
    /// <paramref name="fieldLabel"/>, never <paramref name="value"/> itself, because <paramref name="value"/>
    /// is exactly the text this method exists to keep out of any caller-visible message.
    /// </summary>
    private static void EnsureIdentifierDoesNotEchoApiKey(string fieldLabel, string value, OpenTopographyApiKey apiKey, string redactedRequestUri)
    {
        if (ContainsApiKeyValue(value, apiKey))
        {
            throw new OpenTopographySourceMetadataException(
                $"OpenTopography's coordinate reference metadata's {fieldLabel} unexpectedly matches the configured " +
                "API key rather than a legitimate identifier. SolidGround refuses to use it, to avoid carrying the " +
                "key into the acquired elevation data's reference metadata. This cannot be a legitimate coordinate " +
                "reference system or datum name; if this recurs, investigate why OpenTopography echoed the key back.",
                redactedRequestUri);
        }
    }

    /// <summary>Same as <see cref="EnsureIdentifierDoesNotEchoApiKey"/>, but a no-op when <paramref name="value"/> is null.</summary>
    private static void EnsureOptionalIdentifierDoesNotEchoApiKey(string fieldLabel, string? value, OpenTopographyApiKey apiKey, string redactedRequestUri)
    {
        if (value is not null)
        {
            EnsureIdentifierDoesNotEchoApiKey(fieldLabel, value, apiKey, redactedRequestUri);
        }
    }

    private static bool ContainsApiKeyValue(string value, OpenTopographyApiKey apiKey)
    {
        string rawValue = apiKey.RawValue;
        if (rawValue.Length > 0 && value.Contains(rawValue, StringComparison.Ordinal))
        {
            return true;
        }

        string escapedValue = Uri.EscapeDataString(rawValue);
        return escapedValue.Length > 0
            && !string.Equals(escapedValue, rawValue, StringComparison.Ordinal)
            && value.Contains(escapedValue, StringComparison.Ordinal);
    }

    private static bool MentionsDatasetAccess(string text) => ContainsAny(text, "dataset", "access", "restricted");

    private static bool MentionsQuota(string text) => ContainsAny(text, "rate limit", "limit exceeded", "quota");

    private static bool ContainsAny(string text, params ReadOnlySpan<string> needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildServerMessage(string rawBodyText, OpenTopographyApiKey apiKey, bool bodyTruncated) =>
        CollapseWhitespace(StripTags(OpenTopographyRedaction.RedactText(rawBodyText, apiKey, bodyTruncated)));

    private static string StripTags(string text) => Regex.Replace(text, "<[^>]*>", " ");

    private static string CollapseWhitespace(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
