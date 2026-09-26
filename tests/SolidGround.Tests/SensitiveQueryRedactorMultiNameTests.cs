using SolidGround.Core.Http;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0) code-review follow-up: every call in <see cref="SensitiveQueryRedactorTests"/>
/// passes a single-element name collection, so the multi-name code paths this generalization specifically
/// added -- the <c>HashSet&lt;string&gt;</c> membership check in <see cref="SensitiveQueryRedactor.RedactUri"/>
/// and the N-way regex alternation <see cref="SensitiveQueryRedactor.RedactText"/> builds from more than one
/// name -- were never exercised with more than one name at once. These tests supply
/// <see cref="SensitiveQueryParameterNames.KnownFamilies"/> (two names) against input containing both
/// sensitive parameters together, asserting both are redacted in the same pass while an unrelated parameter
/// is left alone. In a new file, not appended to <see cref="SensitiveQueryRedactorTests"/>, per this task's
/// rule against editing an existing test file.
/// </summary>
public sealed class SensitiveQueryRedactorMultiNameTests
{
    [Fact]
    public void RedactUriRedactsEveryNameInAMultiNamePresetInOnePass()
    {
        var uri = new Uri("https://example.com/api?API_Key=abc123&token=xyz789&other=1");

        string redacted = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.KnownFamilies);

        Assert.Equal("https://example.com/api?API_Key=REDACTED&token=REDACTED&other=1", redacted);
    }

    [Fact]
    public void RedactTextRedactsEveryNameInAMultiNamePresetInOnePass()
    {
        string redacted = SensitiveQueryRedactor.RedactText(
            "request failed for https://example.com/api?API_Key=abc123&token=xyz789&other=1",
            SensitiveQueryParameterNames.KnownFamilies);

        Assert.Equal("request failed for https://example.com/api?[REDACTED]&[REDACTED]&other=1", redacted);
    }
}
