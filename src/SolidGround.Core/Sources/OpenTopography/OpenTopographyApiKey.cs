using System.Diagnostics;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Wraps an OpenTopography API key so the raw value cannot be printed, logged, or otherwise surfaced
/// through a public member. The OpenTopography API accepts its key only as the "API_Key" query
/// parameter; there is no header transport, so the raw value must reach the outgoing request URI, but
/// it must never reach a log line, an exception message, or any other public accessor.
/// </summary>
[DebuggerDisplay("[REDACTED]")]
public sealed class OpenTopographyApiKey
{
    public OpenTopographyApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An OpenTopography API key cannot be null or blank.", nameof(value));
        }

        RawValue = value;
    }

    /// <summary>
    /// The raw key value. Restricted to SolidGround.Core so only the request builder that must transmit
    /// the key in the query string can read it. No public member of this type exposes it.
    /// </summary>
    internal string RawValue { get; }

    /// <summary>Always returns a fixed redacted placeholder; the raw key is never returned by a public member.</summary>
    public override string ToString() => "[REDACTED]";
}
