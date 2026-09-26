using System.Net;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>Base type for every exception <see cref="CountyParcelRegistrySource"/> raises. <see cref="ParcelBoundarySourceException.SourceName"/> is always "CountyParcelRegistry".</summary>
public abstract class CountyParcelRegistryException : ParcelBoundarySourceException
{
    private protected CountyParcelRegistryException(string message, string? redactedRequestUri, Exception? innerException = null)
        : base("CountyParcelRegistry", message, innerException)
    {
        RedactedRequestUri = redactedRequestUri;
    }

    /// <summary>The request URI with every sensitive query parameter redacted, or null when no request URI exists yet (an unregistered GEOID, or a local request-validation failure caught before any request was built).</summary>
    public string? RedactedRequestUri { get; }
}

/// <summary>The requested GEOID is not a key of the loaded registry. Fires before any HTTP call.</summary>
public sealed class CountyParcelRegistryUnregisteredGeoidException : CountyParcelRegistryException
{
    public CountyParcelRegistryUnregisteredGeoidException(string message, string geoid, string registryPath)
        : base(message, redactedRequestUri: null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoid);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        Geoid = geoid;
        RegistryPath = registryPath;
    }

    public string Geoid { get; }

    public string RegistryPath { get; }
}

/// <summary>The county service reported a 400-class error, or a local request-validation rule (for example the address search text length bound) rejected the request before it was ever sent.</summary>
public sealed class CountyParcelRegistryRequestValidationException : CountyParcelRegistryException
{
    public CountyParcelRegistryRequestValidationException(string message, string? redactedRequestUri, int? errorCode, string? serverMessage)
        : base(message, redactedRequestUri)
    {
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    /// <summary>The county service's own error code, or null when a local validation rule fired before any request was sent.</summary>
    public int? ErrorCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>The county service reported a server-side error, or no recognizable body was present and the outer HTTP transport status was itself a 5xx.</summary>
public sealed class CountyParcelRegistryServerException : CountyParcelRegistryException
{
    public CountyParcelRegistryServerException(string message, string redactedRequestUri, int? errorCode, HttpStatusCode? transportStatusCode, string? serverMessage)
        : base(message, redactedRequestUri)
    {
        ErrorCode = errorCode;
        TransportStatusCode = transportStatusCode;
        ServerMessage = serverMessage;
    }

    public int? ErrorCode { get; }

    public HttpStatusCode? TransportStatusCode { get; }

    public string? ServerMessage { get; }
}

/// <summary>The request could not complete because of a transport-level failure: a connection error or a timeout. No HTTP response was received.</summary>
public sealed class CountyParcelRegistryNetworkException : CountyParcelRegistryException
{
    public CountyParcelRegistryNetworkException(string message, string redactedRequestUri, Exception innerException)
        : base(message, redactedRequestUri, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}

/// <summary>
/// The county service returned a response this source does not know how to interpret: an undocumented error
/// code, a malformed/unparseable body, a feature missing a required mapped field, or a ring/orientation/
/// topology reconstruction failure from <see cref="EsriJsonPolygonReader"/> or <c>PolygonalRegion.FromGeometry</c>
/// (rewrapped here with the original <c>ParcelGeometryException</c> as <see cref="Exception.InnerException"/>,
/// so a caller catching <see cref="ParcelBoundarySourceException"/> reliably catches every failure mode).
/// </summary>
public sealed class CountyParcelRegistryUnexpectedResponseException : CountyParcelRegistryException
{
    public CountyParcelRegistryUnexpectedResponseException(
        string message,
        string? redactedRequestUri,
        int? errorCode,
        HttpStatusCode? transportStatusCode,
        string? serverMessage,
        Exception? innerException = null)
        : base(message, redactedRequestUri, innerException)
    {
        ErrorCode = errorCode;
        TransportStatusCode = transportStatusCode;
        ServerMessage = serverMessage;
    }

    public int? ErrorCode { get; }

    public HttpStatusCode? TransportStatusCode { get; }

    public string? ServerMessage { get; }
}
