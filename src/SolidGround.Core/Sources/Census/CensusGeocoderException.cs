using System.Net;

namespace SolidGround.Core.Sources.Census;

/// <summary>
/// A client-side pre-check rejected the request before any HTTP call was made (an address longer than the
/// configured maximum), or the Census Geocoder itself rejected the request as malformed (HTTP 400).
/// </summary>
public sealed class CensusGeocoderRequestValidationException : AddressGeocoderException
{
    public CensusGeocoderRequestValidationException(
        string message,
        string redactedRequestUri,
        HttpStatusCode? statusCode = null,
        string? serverMessage = null)
        : base(CensusGeocoder.ProviderName, message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    /// <summary>Null for a client-side pre-check; the Census Geocoder's own status code when it rejected the request.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The server response body, or null for a client-side pre-check.</summary>
    public string? ServerMessage { get; }
}

/// <summary>The Census Geocoder reported zero address matches (a 200 response with an empty <c>addressMatches</c> array).</summary>
public sealed class CensusGeocoderNoCandidatesException : AddressGeocoderException
{
    public CensusGeocoderNoCandidatesException(string message, string redactedRequestUri)
        : base(CensusGeocoder.ProviderName, message, redactedRequestUri)
    {
    }
}

/// <summary>The Census Geocoder reported a server-side error (HTTP 5xx). No 5xx shape is documented; this classification is by status-code range alone.</summary>
public sealed class CensusGeocoderServerException : AddressGeocoderException
{
    public CensusGeocoderServerException(string message, string redactedRequestUri, HttpStatusCode statusCode, string? serverMessage)
        : base(CensusGeocoder.ProviderName, message, redactedRequestUri)
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
public sealed class CensusGeocoderNetworkException : AddressGeocoderException
{
    public CensusGeocoderNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(CensusGeocoder.ProviderName, message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>The Census Geocoder returned a response this provider does not know how to interpret: an undocumented status code, or a 200 response whose body could not be parsed as the documented JSON shape.</summary>
public sealed class CensusGeocoderUnexpectedResponseException : AddressGeocoderException
{
    public CensusGeocoderUnexpectedResponseException(string message, string redactedRequestUri, HttpStatusCode? statusCode, string? serverMessage)
        : base(CensusGeocoder.ProviderName, message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ServerMessage { get; }
}
