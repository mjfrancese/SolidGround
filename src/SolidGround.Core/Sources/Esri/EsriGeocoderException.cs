using System.Net;

namespace SolidGround.Core.Sources.Esri;

/// <summary>The specific reason an <see cref="EsriGeocoderAuthorizationException"/> occurred.</summary>
public enum EsriGeocoderAuthorizationFailure
{
    /// <summary>No API key was configured. No HTTP request was sent.</summary>
    ApiKeyMissing,

    /// <summary>Esri reported <c>error.code</c> 403: the token is valid but lacks the privilege to store results (this provider always sends <c>forStorage=true</c>).</summary>
    InsufficientPrivilege,

    /// <summary>Esri reported <c>error.code</c> 499: a token is required. This should be unreachable, because this provider already checks for a configured key before sending any request.</summary>
    TokenRequired,
}

/// <summary>
/// The request could not be authorized. Thrown before any HTTP call when no API key is configured
/// (<see cref="EsriGeocoderAuthorizationFailure.ApiKeyMissing"/>), or after Esri's response body reports
/// <c>error.code</c> 403 or 499 -- which Esri can report even on an outer HTTP 200, so this exception does
/// not assume a non-success transport status.
/// </summary>
public sealed class EsriGeocoderAuthorizationException : AddressGeocoderException
{
    public EsriGeocoderAuthorizationException(
        string message,
        string redactedRequestUri,
        EsriGeocoderAuthorizationFailure failure,
        int? errorCode,
        string? serverMessage)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unsupported authorization failure kind.");
        }

        Failure = failure;
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The specific reason authorization failed.</summary>
    public EsriGeocoderAuthorizationFailure Failure { get; }

    /// <summary>Esri's own <c>error.code</c> value (403 or 499), or null when <see cref="Failure"/> is <see cref="EsriGeocoderAuthorizationFailure.ApiKeyMissing"/> and no request was sent.</summary>
    public int? ErrorCode { get; }

    /// <summary>The response body's <c>error.message</c>, redacted. Null when no request was sent.</summary>
    public string? ServerMessage { get; }
}

/// <summary>Esri's response body reported <c>error.code</c> 400: the request was malformed.</summary>
public sealed class EsriGeocoderRequestValidationException : AddressGeocoderException
{
    public EsriGeocoderRequestValidationException(string message, string redactedRequestUri, int errorCode, string? serverMessage)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri)
    {
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    /// <summary>Esri's own <c>error.code</c> value from the response body (always 400 for this exception).</summary>
    public int ErrorCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>
/// Esri reported a server-side error: its response body's <c>error.code</c> was 500 or 504
/// (<see cref="ErrorCode"/> set, <see cref="TransportStatusCode"/> null), or no recognizable error/candidates
/// body was present and the outer HTTP transport status was itself a 5xx (<see cref="TransportStatusCode"/>
/// set, <see cref="ErrorCode"/> null). 504 is classified as a server error, not a network error: it is still
/// a real (if slow) response, exactly like OpenTopography's "network" category being reserved for "no
/// response received".
/// </summary>
public sealed class EsriGeocoderServerException : AddressGeocoderException
{
    public EsriGeocoderServerException(
        string message,
        string redactedRequestUri,
        int? errorCode,
        HttpStatusCode? transportStatusCode,
        string? serverMessage)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri)
    {
        ErrorCode = errorCode;
        TransportStatusCode = transportStatusCode;
        ServerMessage = serverMessage;
    }

    /// <summary>Esri's own <c>error.code</c> value (500 or 504), or null when the outer HTTP transport status alone drove this classification.</summary>
    public int? ErrorCode { get; }

    /// <summary>The outer HTTP transport status code, when this classification was driven by it (no error object was present), or null when <see cref="ErrorCode"/> was used instead.</summary>
    public HttpStatusCode? TransportStatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>
/// The request could not complete because of a transport-level failure: a connection error
/// (<see cref="HttpRequestException"/> or <see cref="IOException"/>), or a timeout (a
/// <see cref="TaskCanceledException"/> or <see cref="OperationCanceledException"/> that fired while the
/// caller's own cancellation token was not cancelled). No HTTP response was received.
/// </summary>
public sealed class EsriGeocoderNetworkException : AddressGeocoderException
{
    public EsriGeocoderNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>
/// Esri returned a response this provider does not know how to interpret: an undocumented <c>error.code</c>
/// value, a candidate missing a documented field, or a response with neither a recognizable error object nor
/// a candidates array (classified instead by the outer HTTP transport status, when that alone is
/// undocumented too).
/// </summary>
public sealed class EsriGeocoderUnexpectedResponseException : AddressGeocoderException
{
    public EsriGeocoderUnexpectedResponseException(
        string message,
        string redactedRequestUri,
        int? errorCode,
        HttpStatusCode? transportStatusCode,
        string? serverMessage)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri)
    {
        ErrorCode = errorCode;
        TransportStatusCode = transportStatusCode;
        ServerMessage = serverMessage;
    }

    public int? ErrorCode { get; }

    public HttpStatusCode? TransportStatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>Esri reported a normal (non-error) body with an empty <c>candidates</c> array.</summary>
public sealed class EsriGeocoderNoCandidatesException : AddressGeocoderException
{
    public EsriGeocoderNoCandidatesException(string message, string redactedRequestUri)
        : base(EsriGeocoder.ProviderName, message, redactedRequestUri)
    {
    }
}
