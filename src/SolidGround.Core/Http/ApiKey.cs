using System.Diagnostics;

namespace SolidGround.Core.Http;

/// <summary>
/// Provider-agnostic safe wrapper for a secret value read from an environment variable or a user-secrets
/// file. Never exposes its raw value through a public member. This is a direct generalization of
/// <c>SolidGround.Core.Sources.OpenTopography.OpenTopographyApiKey</c> (SolidGround Issue #27, PH3-0); that
/// type is left independent and unchanged, but a future provider with no existing pinned key type of its own
/// may use this one directly instead of hand-rolling a copy of the same handful of lines.
/// </summary>
[DebuggerDisplay("[REDACTED]")]
public sealed class ApiKey
{
    public ApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An API key cannot be null or blank.", nameof(value));
        }

        RawValue = value;
    }

    /// <summary>The raw key value. Internal to SolidGround.Core only. No public member of this type exposes it.</summary>
    internal string RawValue { get; }

    /// <summary>Always returns a fixed redacted placeholder; the raw key is never returned by a public member.</summary>
    public override string ToString() => "[REDACTED]";
}
