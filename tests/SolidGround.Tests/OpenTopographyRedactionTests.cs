using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

public sealed class OpenTopographyRedactionTests
{
    [Theory]
    [InlineData(
        "https://portal.opentopography.org/API/usgsdem?datasetName=USGS1m&API_Key=abc123&outputFormat=AAIGrid",
        "https://portal.opentopography.org/API/usgsdem?datasetName=USGS1m&API_Key=REDACTED&outputFormat=AAIGrid")]
    [InlineData(
        "https://portal.opentopography.org/API/usgsdem?API_Key=abc123",
        "https://portal.opentopography.org/API/usgsdem?API_Key=REDACTED")]
    [InlineData(
        "https://portal.opentopography.org/API/usgsdem?foo=1&api_key=abc123&bar=2",
        "https://portal.opentopography.org/API/usgsdem?foo=1&api_key=REDACTED&bar=2")]
    [InlineData(
        "https://portal.opentopography.org/API/usgsdem?FOO=1&API_KEY=abc123&BAR=2",
        "https://portal.opentopography.org/API/usgsdem?FOO=1&API_KEY=REDACTED&BAR=2")]
    public void RedactUriReplacesTheApiKeyValueRegardlessOfPositionOrCase(string uriText, string expected)
    {
        string redacted = OpenTopographyRedaction.RedactUri(new Uri(uriText));

        Assert.Equal(expected, redacted);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactUriRedactsAMissingValueAndPreservesOtherParameters()
    {
        var uri = new Uri("https://portal.opentopography.org/API/usgsdem?API_Key&foo=bar");

        string redacted = OpenTopographyRedaction.RedactUri(uri);

        Assert.Equal("https://portal.opentopography.org/API/usgsdem?API_Key=REDACTED&foo=bar", redacted);
    }

    [Fact]
    public void RedactUriLeavesAUriWithNoQueryStringUnchanged()
    {
        var uri = new Uri("https://portal.opentopography.org/API/usgsdem");

        string redacted = OpenTopographyRedaction.RedactUri(uri);

        Assert.Equal("https://portal.opentopography.org/API/usgsdem", redacted);
    }

    [Fact]
    public void RedactUriThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => OpenTopographyRedaction.RedactUri(null!));
    }

    [Fact]
    public void RedactTextReplacesTheRawKeyValue()
    {
        var key = new OpenTopographyApiKey("myFakeKey123");

        string redacted = OpenTopographyRedaction.RedactText("token is myFakeKey123 end", key);

        Assert.Equal("token is [REDACTED] end", redacted);
    }

    [Fact]
    public void RedactTextReplacesTheEscapedFormOfTheKeyValue()
    {
        var key = new OpenTopographyApiKey("abc def/ghi");
        string escaped = Uri.EscapeDataString("abc def/ghi");

        string redacted = OpenTopographyRedaction.RedactText($"redirect?value={escaped}&x=1", key);

        Assert.Equal("redirect?value=[REDACTED]&x=1", redacted);
        Assert.DoesNotContain("abc", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextReplacesTheApiKeyAssignmentPatternEvenWithoutAKey()
    {
        string redacted = OpenTopographyRedaction.RedactText("GET /api?API_Key=zzz999 HTTP/1.1", null);

        Assert.Equal("GET /api?[REDACTED] HTTP/1.1", redacted);
    }

    [Fact]
    public void RedactTextReplacesTheApiKeyAssignmentPatternCaseInsensitively()
    {
        string redacted = OpenTopographyRedaction.RedactText("prefix api_key=lowercase123 suffix", null);

        Assert.Equal("prefix [REDACTED] suffix", redacted);
    }

    [Fact]
    public void RedactTextTruncatesLongText()
    {
        string longText = new('a', 600);

        string redacted = OpenTopographyRedaction.RedactText(longText, null);

        Assert.Equal(513, redacted.Length);
        Assert.EndsWith("…", redacted, StringComparison.Ordinal);
        Assert.StartsWith(new string('a', 512), redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => OpenTopographyRedaction.RedactText(null!, null));
    }

    [Fact]
    public void RedactTextRedactsATrailingKeyFragmentWhenPossiblyTruncatedIsTrue()
    {
        // Regression test: a byte cap can commit a chunk that ends partway through an echoed key. Because
        // ReadBodyAsync always returns a byte-for-byte prefix of the real body, any surviving fragment of
        // the key can only ever be at the very end of the (possibly truncated) text.
        var key = new OpenTopographyApiKey("ABCDEFGHIJKLMNOPQRST");

        string redacted = OpenTopographyRedaction.RedactText("prefix-ABCDE", key, possiblyTruncated: true);

        Assert.Equal("prefix-[REDACTED]", redacted);
        Assert.DoesNotContain("ABCDE", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextDoesNotRedactATrailingFragmentWhenPossiblyTruncatedIsFalse()
    {
        // The default (possiblyTruncated: false) must not change: only a caller that knows a body may have
        // been cut off opts into trailing-fragment redaction, so ordinary complete text that happens to end
        // with a short prefix of an unrelated key is left alone.
        var key = new OpenTopographyApiKey("ABCDEFGHIJKLMNOPQRST");

        string redacted = OpenTopographyRedaction.RedactText("prefix-ABCDE", key);

        Assert.Equal("prefix-ABCDE", redacted);
    }

    [Fact]
    public void RedactTextRedactsATrailingFragmentOfTheEscapedKeyFormWhenPossiblyTruncatedIsTrue()
    {
        var key = new OpenTopographyApiKey("abc def/ghi");
        string escaped = Uri.EscapeDataString("abc def/ghi");
        string truncatedEscapedPrefix = escaped[..(escaped.Length - 2)];

        string redacted = OpenTopographyRedaction.RedactText($"value={truncatedEscapedPrefix}", key, possiblyTruncated: true);

        Assert.Equal("value=[REDACTED]", redacted);
    }

    [Fact]
    public void RedactTextFullyRedactsACompleteBorderingKeyInsteadOfOnlyItsShortSelfOverlap()
    {
        // Regression test: RedactTrailingKeyFragment used to run BEFORE the ordinary whole-value replacement
        // and searched fragment lengths 1..needle.Length-1 (deliberately excluding the complete key). A key
        // with a "border" (a proper suffix equal to a proper prefix — here, simply first character equals
        // last character) that is present COMPLETE and untruncated at the tail of text used to have only its
        // short border redacted by the fragment search, which broke the contiguous full-value substring so
        // the subsequent whole-value Replace could no longer find it, leaving most of the real key exposed.
        var key = new OpenTopographyApiKey("ABCDEA");

        string redacted = OpenTopographyRedaction.RedactText("prefix-ABCDEA", key, possiblyTruncated: true);

        Assert.Equal("prefix-[REDACTED]", redacted);
        Assert.DoesNotContain("ABCDE", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextFullyRedactsACompleteBorderingEscapedKeyInsteadOfOnlyItsShortSelfOverlap()
    {
        // Same regression as above, for the escaped-value branch: "///" escapes to "%2F%2F%2F", which has
        // its own repeating six-character border ("%2F%2F"), shorter than the full nine-character value.
        var key = new OpenTopographyApiKey("///");
        string escaped = Uri.EscapeDataString("///");

        string redacted = OpenTopographyRedaction.RedactText($"prefix-{escaped}", key, possiblyTruncated: true);

        Assert.Equal("prefix-[REDACTED]", redacted);
        Assert.DoesNotContain("%2F", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextRedactsTheSurvivingPrefixWhenTruncationSplitsAMultiByteCharacterInsideTheKey()
    {
        // Regression test: a byte-cap truncation that cuts through the middle of a multi-byte UTF-8
        // character makes Encoding.UTF8.GetString (used to decode the response body before it reaches this
        // method) emit a single U+FFFD replacement character at the cut point. RedactTrailingKeyFragment's
        // plain suffix comparison used to find no match at all (U+FFFD cannot equal any character of the
        // key), leaving the entire surviving ASCII prefix of the key exposed right next to the placeholder.
        // 'é' (U+00E9) is 2 bytes in UTF-8; this simulates a cut after "ABCDEFGHIJ" plus the lead byte of
        // 'é', which is exactly what DecodeUtf8 would produce for such a truncated response body.
        var key = new OpenTopographyApiKey("ABCDEFGHIJéKLMNOP");

        string redacted = OpenTopographyRedaction.RedactText("prefix-ABCDEFGHIJ�", key, possiblyTruncated: true);

        Assert.Equal("prefix-[REDACTED]", redacted);
        Assert.DoesNotContain("ABCDEFGHIJ", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactTextWithPossiblyTruncatedStillRedactsAWholeKeyValueThatIsNotAtTheEnd()
    {
        var key = new OpenTopographyApiKey("myFakeKey123");

        string redacted = OpenTopographyRedaction.RedactText("token is myFakeKey123 end", key, possiblyTruncated: true);

        Assert.Equal("token is [REDACTED] end", redacted);
    }

    [Fact]
    public void RedactTextDisablesTruncationWhenMaximumLengthIsMaxValue()
    {
        string longText = new('a', 600);

        string redacted = OpenTopographyRedaction.RedactText(longText, null, maximumLength: int.MaxValue);

        Assert.Equal(longText, redacted);
    }
}
