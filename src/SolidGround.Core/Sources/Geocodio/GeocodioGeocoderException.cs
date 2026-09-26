using System.Net;

namespace SolidGround.Core.Sources.Geocodio;

/// <summary>The specific reason a <see cref="GeocodioGeocoderAuthorizationException"/> occurred.</summary>
public enum GeocodioGeocoderAuthorizationFailure
{
    /// <summary>No API key was configured. No HTTP request was sent.</summary>
    ApiKeyMissing,

    /// <summary>
    /// Geocodio returned HTTP 403. Geocodio's documented codes have no separate 401: an invalid key, a
    /// daily-limit condition, and a permission restriction are all folded into 403 ("Invalid API key, or
    /// other reason why access is forbidden"), indistinguishable from status and envelope shape alone.
    /// </summary>
    Rejected,
}

/// <summary>
/// The request could not be authorized. Thrown before any HTTP call when no API key is configured
/// (<see cref="GeocodioGeocoderAuthorizationFailure.ApiKeyMissing"/>), or after a 403 response
/// (<see cref="GeocodioGeocoderAuthorizationFailure.Rejected"/>).
/// </summary>
public sealed class GeocodioGeocoderAuthorizationException : AddressGeocoderException
{
    public GeocodioGeocoderAuthorizationException(
        string message,
        string redactedRequestUri,
        GeocodioGeocoderAuthorizationFailure failure,
        HttpStatusCode? statusCode,
        string? serverMessage)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
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
    public GeocodioGeocoderAuthorizationFailure Failure { get; }

    /// <summary>The HTTP status code returned, or null when <see cref="Failure"/> is <see cref="GeocodioGeocoderAuthorizationFailure.ApiKeyMissing"/> and no request was sent.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The redacted server response body, or null when no request was sent.</summary>
    public string? ServerMessage { get; }
}

/// <summary>Geocodio rejected the request as malformed or ambiguous (HTTP 422) -- also the more likely real "no good match" path, since Geocodio's docs say it rarely returns a true zero-result for anything address-shaped.</summary>
public sealed class GeocodioGeocoderRequestValidationException : AddressGeocoderException
{
    public GeocodioGeocoderRequestValidationException(string message, string redactedRequestUri, HttpStatusCode statusCode, string? serverMessage)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>Geocodio reported a rate limit (HTTP 429). Captures the three documented <c>X-RateLimit-*</c> response headers, when present.</summary>
public sealed class GeocodioGeocoderQuotaException : AddressGeocoderException
{
    public GeocodioGeocoderQuotaException(
        string message,
        string redactedRequestUri,
        HttpStatusCode statusCode,
        string? serverMessage,
        string? rateLimitRemaining,
        string? rateLimitLimit,
        string? rateLimitPeriod)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
        RateLimitRemaining = rateLimitRemaining;
        RateLimitLimit = rateLimitLimit;
        RateLimitPeriod = rateLimitPeriod;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ServerMessage { get; }

    /// <summary>The <c>X-RateLimit-Remaining</c> response header value, when present.</summary>
    public string? RateLimitRemaining { get; }

    /// <summary>The <c>X-RateLimit-Limit</c> response header value, when present.</summary>
    public string? RateLimitLimit { get; }

    /// <summary>The <c>X-RateLimit-Period</c> response header value, when present.</summary>
    public string? RateLimitPeriod { get; }
}

/// <summary>Geocodio reported a server-side error (HTTP 500).</summary>
public sealed class GeocodioGeocoderServerException : AddressGeocoderException
{
    public GeocodioGeocoderServerException(string message, string redactedRequestUri, HttpStatusCode statusCode, string? serverMessage)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
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
public sealed class GeocodioGeocoderNetworkException : AddressGeocoderException
{
    public GeocodioGeocoderNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>Geocodio returned a response this provider does not know how to interpret: an undocumented status code, or a 200 response whose body did not carry the documented <c>results</c> shape.</summary>
public sealed class GeocodioGeocoderUnexpectedResponseException : AddressGeocoderException
{
    public GeocodioGeocoderUnexpectedResponseException(string message, string redactedRequestUri, HttpStatusCode? statusCode, string? serverMessage)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>Geocodio reported an empty <c>results</c> array (a 200 response with no matches).</summary>
public sealed class GeocodioGeocoderNoCandidatesException : AddressGeocoderException
{
    public GeocodioGeocoderNoCandidatesException(string message, string redactedRequestUri)
        : base(GeocodioGeocoder.ProviderName, message, redactedRequestUri)
    {
    }
}
