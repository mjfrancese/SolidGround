using System.Net;

namespace SolidGround.Core.Sources.Census;

/// <summary>
/// Base type for every exception <see cref="CensusCountyLookup"/> raises. A new, independent exception family
/// -- not a subtype of <see cref="AddressGeocoderException"/> or <see cref="ParcelBoundarySourceException"/>,
/// since <see cref="CensusCountyLookup"/> is neither an <see cref="IAddressGeocoder"/> nor an
/// <see cref="IParcelBoundarySource"/>; it is a small standalone helper. See
/// docs/architecture/census-county-lookup.md.
/// </summary>
public abstract class CensusCountyLookupException : Exception
{
    private protected CensusCountyLookupException(string message, string redactedRequestUri, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedRequestUri);
        RedactedRequestUri = redactedRequestUri;
    }

    /// <summary>The request URI with every sensitive query parameter redacted. Safe to log or display. This endpoint sends no API key of its own.</summary>
    public string RedactedRequestUri { get; }
}

/// <summary>No county contains the requested point: a 200 response with an empty or Counties-less <c>geographies</c> object. This is the normal "off the parcel fabric" outcome, not a parse failure.</summary>
public sealed class CensusCountyLookupNoCountyException : CensusCountyLookupException
{
    public CensusCountyLookupNoCountyException(string message, string redactedRequestUri)
        : base(message, redactedRequestUri)
    {
    }
}

/// <summary>The Census county lookup endpoint reported a server-side error (HTTP 5xx). No 5xx shape is documented for this endpoint; this classification is by status-code range alone.</summary>
public sealed class CensusCountyLookupServerException : CensusCountyLookupException
{
    public CensusCountyLookupServerException(string message, string redactedRequestUri, HttpStatusCode statusCode, string? serverMessage)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>
/// The request could not complete because of a transport-level failure: a connection error
/// (<see cref="HttpRequestException"/> or <see cref="IOException"/>), or a timeout (a
/// <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/> that fired while the
/// caller's own cancellation token was not cancelled). No HTTP response was received.
/// </summary>
public sealed class CensusCountyLookupNetworkException : CensusCountyLookupException
{
    public CensusCountyLookupNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>
/// Any other non-200, non-5xx status, or a 200 body that does not parse into the documented shape (missing
/// <c>result</c>/<c>geographies</c>, a non-object <c>geographies</c>, a non-object first <c>Counties</c> entry,
/// or a <c>GEOID</c> that is missing, non-string, or not exactly 5 digits).
/// </summary>
public sealed class CensusCountyLookupUnexpectedResponseException : CensusCountyLookupException
{
    public CensusCountyLookupUnexpectedResponseException(string message, string redactedRequestUri, HttpStatusCode? statusCode, string? serverMessage)
        : base(message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ServerMessage { get; }
}
