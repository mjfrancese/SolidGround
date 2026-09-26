using System.Text.RegularExpressions;

namespace SolidGround.Core.Http;

/// <summary>
/// Redacts a caller-supplied set of sensitive query-parameter names from a URI or free-form text. This is a
/// direct generalization of
/// <c>SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction</c>'s original implementation
/// (SolidGround Issue #27, PH3-0), parameterized by name instead of hardcoded to "API_Key". See
/// <c>docs/architecture/shared-http-redaction-and-key-resolution.md</c>.
/// </summary>
public static class SensitiveQueryRedactor
{
    private const string TextRedactedPlaceholder = "[REDACTED]";
    private const string UriRedactedPlaceholder = "REDACTED";

    /// <summary>The default maximum length <see cref="RedactText"/> truncates to, appropriate for exception-message text.</summary>
    public const int DefaultMaximumTextLength = 512;

    /// <summary>
    /// Returns <paramref name="uri"/> as a string with the value of any query parameter named in
    /// <paramref name="sensitiveParameterNames"/> (case-insensitive) replaced by "REDACTED", regardless of
    /// the parameter's position or whether it has a value at all. Works on an absolute or relative
    /// <paramref name="uri"/>. Every other parameter, and a <paramref name="uri"/> with no query string, pass
    /// through unchanged.
    /// </summary>
    public static string RedactUri(Uri uri, IReadOnlyCollection<string> sensitiveParameterNames)
    {
        ArgumentNullException.ThrowIfNull(uri);
        RequireNames(sensitiveParameterNames);
        HashSet<string> names = new(sensitiveParameterNames, StringComparer.OrdinalIgnoreCase);

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
            if (names.Contains(name))
            {
                pairs[i] = $"{name}={UriRedactedPlaceholder}";
            }
        }

        return $"{beforeQuery}?{string.Join('&', pairs)}{fragment}";
    }

    /// <summary>
    /// Returns <paramref name="text"/> with: (1) every ordinal occurrence of <paramref name="secret"/>'s raw
    /// value and its <see cref="Uri.EscapeDataString(string)"/> form replaced with "[REDACTED]" (plus, when
    /// <paramref name="possiblyTruncated"/>, the longest trailing fragment of either form -- see
    /// <see cref="RedactTrailingKeyFragment"/> for the full truncation-boundary and self-overlap handling);
    /// then (2), always, regardless of <paramref name="secret"/>, every "name=value"-shaped occurrence of any
    /// name in <paramref name="sensitiveParameterNames"/> (case-insensitive); then (3) truncated to
    /// <paramref name="maximumLength"/>. Free-form text may embed zero, one, or many URLs -- step (2) is a
    /// plain substring/regex scan, not URI parsing, so it finds a match anywhere in the text.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="sensitiveParameterNames">The query-parameter names (case-insensitive) to redact wherever they appear as "name=value".</param>
    /// <param name="secret">The secret whose raw and escaped forms must be removed, or null when none is available.</param>
    /// <param name="possiblyTruncated">
    /// True when <paramref name="text"/> may have been cut off mid-value by a response-size cap. See
    /// <c>SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction.RedactText</c> for the full
    /// rationale; the algorithm here is identical.
    /// </param>
    /// <param name="maximumLength">
    /// The maximum length of the returned text. Defaults to <see cref="DefaultMaximumTextLength"/>; callers
    /// that must preserve a complete, non-secret record can pass <see cref="int.MaxValue"/> to disable
    /// truncation.
    /// </param>
    public static string RedactText(
        string text,
        IReadOnlyCollection<string> sensitiveParameterNames,
        ApiKey? secret = null,
        bool possiblyTruncated = false,
        int maximumLength = DefaultMaximumTextLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        RequireNames(sensitiveParameterNames);

        string redacted = text;
        if (secret is not null)
        {
            string rawValue = secret.RawValue;
            if (rawValue.Length > 0)
            {
                // The ordinary whole-value replacement always runs first, before the trailing-fragment
                // search. A complete, untruncated occurrence of the secret (or its escaped form) is removed
                // here in one piece, including one with a "border" (a proper suffix equal to a proper
                // prefix, for example a value whose first and last characters match). That leaves only
                // genuinely partial, truncation-severed fragments -- which Replace cannot match at all -- for
                // RedactTrailingKeyFragment to find. Running the fragment search first could find and remove
                // only a short self-overlapping border of a complete value, breaking the contiguous match
                // Replace needed and leaving most of the real value exposed next to the fragment's
                // placeholder.
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

        redacted = BuildAssignmentPattern(sensitiveParameterNames).Replace(redacted, TextRedactedPlaceholder);

        return Truncate(redacted, maximumLength);
    }

    /// <summary>
    /// Returns <paramref name="text"/> with its longest trailing substring that equals a strict prefix of
    /// <paramref name="needle"/> (length 1 through <c>needle.Length - 1</c>) replaced with "[REDACTED]", or
    /// <paramref name="text"/> unchanged when no such suffix exists. Only ever matches at the end of
    /// <paramref name="text"/>, because a byte-cap truncation can only ever cut a value at its own tail. By
    /// the time this runs, <see cref="RedactText"/> has already replaced every complete occurrence of
    /// <paramref name="needle"/>, so a match here is always a genuinely partial, truncation-severed fragment,
    /// never a complete value that merely happens to have a self-overlapping "border". Moved verbatim from
    /// <c>OpenTopographyRedaction</c>'s original private implementation (SolidGround Issue #27, PH3-0).
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

    private static Regex BuildAssignmentPattern(IReadOnlyCollection<string> names)
    {
        // Not [GeneratedRegex]: the pattern is runtime-composed from a caller-supplied name set, which a
        // source-generated regex cannot accept (its pattern must be a compile-time literal). Not
        // RegexOptions.Compiled either: this Regex is constructed fresh per call and never cached, so
        // compiling it would add pure overhead with no reuse to amortize it against -- this is not a hot
        // path (occasional redaction of a log/error line, not a tight loop). The character class
        // '[^&\s"'<>]*' is copied verbatim from OpenTopographyRedaction's original pattern.
        string alternation = string.Join('|', names.Select(Regex.Escape));
        return new Regex($@"(?:{alternation})=[^&\s""'<>]*", RegexOptions.IgnoreCase);
    }

    private static void RequireNames(IReadOnlyCollection<string> sensitiveParameterNames)
    {
        ArgumentNullException.ThrowIfNull(sensitiveParameterNames);
        if (sensitiveParameterNames.Count == 0 || sensitiveParameterNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-blank sensitive parameter name is required.", nameof(sensitiveParameterNames));
        }
    }
}
