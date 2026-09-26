using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Removes the OpenTopography API key, and any "API_Key=" query-string form, from text that might be
/// surfaced to a caller. OpenTopography's usgsdem endpoint accepts its key only as the "API_Key" query
/// parameter, so every request URI shown outside this class must first be redacted, and the endpoint has
/// been observed to echo an invalid key back in its error response body, so every server-supplied message
/// must be redacted too. Thin facade over <see cref="SensitiveQueryRedactor"/> (SolidGround Issue #27,
/// PH3-0), permanently scoped to <see cref="SensitiveQueryParameterNames.OpenTopography"/> only -- this
/// facade must never be widened to <see cref="SensitiveQueryParameterNames.KnownFamilies"/>, because
/// broadening the redacted set here could change this class's own output for a real (if currently
/// unobserved) OpenTopography response that happened to contain, for example, a "token=" substring.
/// </summary>
public static class OpenTopographyRedaction
{
    /// <summary>
    /// Returns <paramref name="uri"/> as a string with the value of any "API_Key" query parameter replaced
    /// by "REDACTED", regardless of the parameter's position, case, or whether it has a value at all.
    /// </summary>
    public static string RedactUri(Uri uri) =>
        SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.OpenTopography);

    /// <summary>
    /// Returns <paramref name="text"/> with every ordinal occurrence of <paramref name="key"/>'s raw value,
    /// its <see cref="Uri.EscapeDataString(string)"/> form, and any "API_Key=&lt;value&gt;" pattern replaced
    /// with "[REDACTED]", then truncated to <paramref name="maximumLength"/> for safe use in a message.
    /// Redaction of the "API_Key=" pattern does not depend on <paramref name="key"/> being supplied. See
    /// <see cref="SensitiveQueryRedactor.RedactText"/> for the full algorithm and ordering contract --
    /// identical to this method's pre-Issue-#27 behavior; only the implementation moved.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="key">The key whose raw and escaped forms must be removed, or null when none is available.</param>
    /// <param name="possiblyTruncated">See <see cref="SensitiveQueryRedactor.RedactText"/>.</param>
    /// <param name="maximumLength">See <see cref="SensitiveQueryRedactor.RedactText"/>.</param>
    public static string RedactText(
        string text,
        OpenTopographyApiKey? key,
        bool possiblyTruncated = false,
        int maximumLength = SensitiveQueryRedactor.DefaultMaximumTextLength) =>
        SensitiveQueryRedactor.RedactText(
            text,
            SensitiveQueryParameterNames.OpenTopography,
            key is null ? null : new ApiKey(key.RawValue),
            possiblyTruncated,
            maximumLength);
}
