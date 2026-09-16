using System.Net;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Base type for every exception raised while acquiring elevation data from OpenTopography. Every
/// message states what was observed and what the caller can do about it, and never contains the raw
/// API key or an unredacted request URI.
/// </summary>
public class OpenTopographyException : Exception
{
    private protected OpenTopographyException(string message, string redactedRequestUri, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedRequestUri);
        RedactedRequestUri = redactedRequestUri;
    }

    /// <summary>
    /// The request URI with its "API_Key" query parameter redacted by <see cref="OpenTopographyRedaction.RedactUri(Uri)"/>.
    /// This is the only form of the request URI safe to log, display, or include in a report.
    /// </summary>
    public string RedactedRequestUri { get; }
}

/// <summary>The specific reason an <see cref="OpenTopographyAuthorizationException"/> occurred.</summary>
public enum OpenTopographyAuthorizationFailure
{
    /// <summary>No API key was configured. No HTTP request was sent.</summary>
    ApiKeyMissing,

    /// <summary>OpenTopography rejected the configured API key as invalid (HTTP 401).</summary>
    ApiKeyRejected,

    /// <summary>OpenTopography denied access to the requested dataset even though a key was supplied (HTTP 401 or 403).</summary>
    DatasetAccessDenied,
}

/// <summary>
/// The request could not be authorized. Thrown before any HTTP call when no API key is configured
/// (<see cref="OpenTopographyAuthorizationFailure.ApiKeyMissing"/>), or after a 401/403 response.
/// </summary>
public sealed class OpenTopographyAuthorizationException : OpenTopographyException
{
    public OpenTopographyAuthorizationException(
        string message,
        string redactedRequestUri,
        OpenTopographyAuthorizationFailure failure,
        HttpStatusCode? statusCode,
        string? serverMessage)
        : base(message, redactedRequestUri)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unsupported authorization failure kind.");
        }

        Failure = failure;
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The specific reason authorization failed.</summary>
    public OpenTopographyAuthorizationFailure Failure { get; }

    /// <summary>The HTTP status code returned, or null when <see cref="Failure"/> is <see cref="OpenTopographyAuthorizationFailure.ApiKeyMissing"/> and no request was sent.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The redacted, tag-stripped, bounded server response body, or null when no request was sent.</summary>
    public string? ServerMessage { get; }
}

/// <summary>
/// OpenTopography appears to have reported a rate limit or quota condition: an explicit HTTP 429, or a
/// 401/403/400 response whose body mentions a rate limit, a limit being exceeded, or a quota. OpenTopography
/// does not document a rate-limit HTTP shape, so this classification is best effort.
/// </summary>
public sealed class OpenTopographyQuotaException : OpenTopographyException
{
    public OpenTopographyQuotaException(string message, string redactedRequestUri, HttpStatusCode statusCode, string serverMessage)
        : base(message, redactedRequestUri)
    {
        ArgumentNullException.ThrowIfNull(serverMessage);
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The HTTP status code that triggered the quota classification.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The redacted, tag-stripped, bounded server response body. Can be empty: an HTTP 429 (which is always
    /// classified as a quota condition) may carry no body at all.
    /// </summary>
    public string ServerMessage { get; }
}

/// <summary>
/// A client-side pre-check rejected the request before any HTTP call was made (an AOI that is not a WGS 84
/// bounding box, invalid bounds, or an approximate area above the configured limit), or OpenTopography
/// itself rejected the request as malformed (HTTP 400).
/// </summary>
public sealed class OpenTopographyRequestValidationException : OpenTopographyException
{
    public OpenTopographyRequestValidationException(
        string message,
        string redactedRequestUri,
        HttpStatusCode? statusCode = null,
        string? serverMessage = null)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    /// <summary>Null for a client-side pre-check; <see cref="HttpStatusCode.BadRequest"/> when OpenTopography itself rejected the request.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The redacted, tag-stripped, bounded server response body, or null for a client-side pre-check.</summary>
    public string? ServerMessage { get; }
}

/// <summary>OpenTopography reported that no elevation data exists for the requested area (HTTP 204).</summary>
public sealed class OpenTopographyNoDataException : OpenTopographyException
{
    public OpenTopographyNoDataException(string message, string redactedRequestUri, HttpStatusCode statusCode)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>OpenTopography reported a server-side error (HTTP 5xx).</summary>
public sealed class OpenTopographyServerException : OpenTopographyException
{
    public OpenTopographyServerException(string message, string redactedRequestUri, HttpStatusCode statusCode, string? serverMessage)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>The redacted, tag-stripped, bounded server response body, when one was available.</summary>
    public string? ServerMessage { get; }
}

/// <summary>
/// The request could not complete because of a transport-level failure: a connection error
/// (<see cref="HttpRequestException"/> or <see cref="IOException"/>), or a timeout (a
/// <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/> that fired while the
/// caller's own cancellation token was not cancelled). No HTTP response was received.
/// </summary>
public sealed class OpenTopographyNetworkException : OpenTopographyException
{
    public OpenTopographyNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>
/// OpenTopography returned a response this source does not know how to interpret: an undocumented status
/// code, a 200 response whose body is neither a zip archive nor a bare AAIGrid text body, a response over
/// the configured maximum size, or a body with gzip magic bytes.
/// </summary>
public sealed class OpenTopographyUnexpectedResponseException : OpenTopographyException
{
    public OpenTopographyUnexpectedResponseException(
        string message,
        string redactedRequestUri,
        HttpStatusCode? statusCode,
        string? serverMessage)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>A redacted, tag-stripped, bounded snippet of the response body, when one was available.</summary>
    public string? ServerMessage { get; }
}

/// <summary>
/// OpenTopography returned a 200 response with elevation data, but SolidGround could not extract a complete,
/// trustworthy elevation grid from it: no .prj or .aux.xml sidecar, a bare AAIGrid body with no sidecar at
/// all, more than one .prj entry, more than one .aux.xml entry, an .aux.xml sidecar present but lacking an
/// &lt;SRS&gt; element, a blank or whitespace-only coordinate reference sidecar, a zip with valid zip magic
/// bytes that could not be read as a valid archive, a WKT parse failure, WKT without a vertical coordinate
/// system, an unsupported unit, a parsed coordinate reference system name, horizontal datum, or vertical
/// datum that unexpectedly matches the configured API key, a zip archive with zero or multiple .asc entries,
/// or reference metadata that parsed successfully but whose .asc raster entry content then failed to parse
/// as a valid AAIGrid grid (for example a malformed header, a wrong NODATA_value count, or missing or extra
/// cell values). SolidGround preserves whatever reference metadata a response actually carries and fails
/// rather than assuming a coordinate reference system or accepting a malformed raster.
/// </summary>
public sealed class OpenTopographySourceMetadataException : OpenTopographyException
{
    public OpenTopographySourceMetadataException(string message, string redactedRequestUri, Exception? innerException = null)
        : base(message, redactedRequestUri, innerException)
    {
    }
}
