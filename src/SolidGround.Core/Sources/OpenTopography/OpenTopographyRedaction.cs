using System.Text.RegularExpressions;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Removes the OpenTopography API key, and any "API_Key=" query-string form, from text that might be
/// surfaced to a caller. OpenTopography's usgsdem endpoint accepts its key only as the "API_Key" query
/// parameter, so every request URI shown outside this class must first be redacted, and the endpoint has
/// been observed to echo an invalid key back in its error response body, so every server-supplied message
/// must be redacted too.
/// </summary>
public static partial class OpenTopographyRedaction
{
    private const string ApiKeyParameterName = "API_Key";
    private const string UriRedactedPlaceholder = "REDACTED";
    private const string TextRedactedPlaceholder = "[REDACTED]";
    private const int DefaultMaximumTextLength = 512;

    /// <summary>
    /// Returns <paramref name="uri"/> as a string with the value of any "API_Key" query parameter replaced
    /// by "REDACTED", regardless of the parameter's position, case, or whether it has a value at all.
    /// </summary>
    public static string RedactUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        string uriText = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
        int queryStart = uriText.IndexOf('?');
        if (queryStart < 0)
        {
            return uriText;
        }

        int fragmentStart = uriText.IndexOf('#', queryStart);
        string beforeQuery = uriText[..queryStart];
        string query = fragmentStart < 0 ? uriText[(queryStart + 1)..] : uriText[(queryStart + 1)..fragmentStart];
        string fragment = fragmentStart < 0 ? string.Empty : uriText[fragmentStart..];

        if (query.Length == 0)
        {
            return uriText;
        }

        string[] pairs = query.Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            string pair = pairs[i];
            int equalsIndex = pair.IndexOf('=');
            string name = equalsIndex < 0 ? pair : pair[..equalsIndex];
            if (name.Equals(ApiKeyParameterName, StringComparison.OrdinalIgnoreCase))
            {
                pairs[i] = $"{name}={UriRedactedPlaceholder}";
            }
        }

        return $"{beforeQuery}?{string.Join('&', pairs)}{fragment}";
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every ordinal occurrence of <paramref name="key"/>'s raw value,
    /// its <see cref="Uri.EscapeDataString(string)"/> form, and any "API_Key=&lt;value&gt;" pattern replaced
    /// with "[REDACTED]", then truncated to <paramref name="maximumLength"/> for safe use in a message.
    /// Redaction of the "API_Key=" pattern does not depend on <paramref name="key"/> being supplied.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="key">The key whose raw and escaped forms must be removed, or null when none is available.</param>
    /// <param name="possiblyTruncated">
    /// True when <paramref name="text"/> may have been cut off mid-value by a response-size cap (see
    /// <see cref="OpenTopographyUsgs1mSource"/>'s body-reading contract). A caller-configured byte cap can
    /// commit a chunk that ends partway through an echoed key, and because a truncated body is always a
    /// byte-for-byte prefix of the real response, any surviving fragment of the key can only appear at the
    /// very end of <paramref name="text"/>. When true, this also removes the longest trailing substring of
    /// <paramref name="text"/> that matches a strict prefix of the key's raw or escaped form, after the
    /// ordinary whole-value replacement runs (so a complete, untruncated key that happens to have a "border" —
    /// a proper suffix equal to a proper prefix, for example a key whose first and last characters match — is
    /// already replaced as a whole before the fragment search can mistake its short trailing border for the
    /// only surviving fragment). If a truncation cut a multi-byte UTF-8 character inside the key in half, the
    /// resulting <c>U+FFFD</c> replacement character at the very end of <paramref name="text"/> is excluded
    /// from the fragment search (it cannot equal any character of the key), so the fragment search still
    /// finds the genuine key characters that decoded successfully immediately before it.
    /// </param>
    /// <param name="maximumLength">
    /// The maximum length of the returned text. Defaults to <see cref="DefaultMaximumTextLength"/>, which is
    /// appropriate for exception-message text; callers that must preserve a complete, non-secret record (for
    /// example, diagnostic evidence) can pass <see cref="int.MaxValue"/> to disable truncation.
    /// </param>
    public static string RedactText(string text, OpenTopographyApiKey? key, bool possiblyTruncated = false, int maximumLength = DefaultMaximumTextLength)
    {
        ArgumentNullException.ThrowIfNull(text);

        string redacted = text;
        if (key is not null)
        {
            string rawValue = key.RawValue;
            if (rawValue.Length > 0)
            {
                // The ordinary whole-value replacement always runs first, before the trailing-fragment
                // search. A complete, untruncated occurrence of the key (or its escaped form) is removed
                // here in one piece, including one with a "border" (a proper suffix equal to a proper
                // prefix, for example a key whose first and last characters match). That leaves only
                // genuinely partial, truncation-severed fragments — which Replace cannot match at all — for
                // RedactTrailingKeyFragment to find. Running the fragment search first, as this used to,
                // could find and remove only a short self-overlapping border of a complete key, breaking the
                // contiguous match Replace needed and leaving most of the real key exposed next to the
                // fragment's placeholder.
                redacted = redacted.Replace(rawValue, TextRedactedPlaceholder, StringComparison.Ordinal);
                if (possiblyTruncated)
                {
                    redacted = RedactTrailingKeyFragment(redacted, rawValue);
                }

                string escapedValue = Uri.EscapeDataString(rawValue);
                if (!string.Equals(escapedValue, rawValue, StringComparison.Ordinal))
                {
                    redacted = redacted.Replace(escapedValue, TextRedactedPlaceholder, StringComparison.Ordinal);
                    if (possiblyTruncated)
                    {
                        redacted = RedactTrailingKeyFragment(redacted, escapedValue);
                    }
                }
            }
        }

        redacted = ApiKeyAssignmentPattern().Replace(redacted, TextRedactedPlaceholder);

        return Truncate(redacted, maximumLength);
    }

    /// <summary>
    /// Returns <paramref name="text"/> with its longest trailing substring that equals a strict prefix of
    /// <paramref name="needle"/> (length 1 through <c>needle.Length - 1</c>) replaced with "[REDACTED]", or
    /// <paramref name="text"/> unchanged when no such suffix exists. Only ever matches at the end of
    /// <paramref name="text"/>, because a byte-cap truncation can only ever cut a value at its own tail.
    /// By the time this runs, <see cref="RedactText"/> has already replaced every complete occurrence of
    /// <paramref name="needle"/>, so a match here is always a genuinely partial, truncation-severed fragment,
    /// never a complete value that merely happens to have a self-overlapping "border".
    /// </summary>
    private static string RedactTrailingKeyFragment(string text, string needle)
    {
        // .NET's UTF-8 decoder (Encoding.UTF8.GetString, used to decode a response body before it reaches
        // this method) emits exactly one U+FFFD replacement character when a byte-cap truncation cuts a
        // multi-byte character in half. That replacement character carries no information about the removed
        // bytes and cannot equal any character of the key, so comparing straight against the literal tail of
        // `text` would never find the key characters that decoded successfully just before it, silently
        // leaving that entire surviving prefix unredacted. Exclude a single trailing U+FFFD from the
        // comparison window (it is dropped from the result along with any fragment found immediately before
        // it), so a truncation that splits a multi-byte character inside the key is treated the same as one
        // that splits it between single-byte characters.
        bool endsWithReplacementCharacter = text.Length > 0 && text[^1] == '�';
        ReadOnlySpan<char> searchable = endsWithReplacementCharacter ? text.AsSpan(0, text.Length - 1) : text.AsSpan();

        int maximumFragmentLength = Math.Min(needle.Length - 1, searchable.Length);
        for (int fragmentLength = maximumFragmentLength; fragmentLength >= 1; fragmentLength--)
        {
            if (searchable[^fragmentLength..].SequenceEqual(needle.AsSpan(0, fragmentLength)))
            {
                return string.Concat(searchable[..^fragmentLength], TextRedactedPlaceholder);
            }
        }

        return text;
    }

    private static string Truncate(string text, int maximumLength) =>
        text.Length <= maximumLength ? text : string.Concat(text.AsSpan(0, maximumLength), "…");

    [GeneratedRegex(@"API_Key=[^&\s""'<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyAssignmentPattern();
}
