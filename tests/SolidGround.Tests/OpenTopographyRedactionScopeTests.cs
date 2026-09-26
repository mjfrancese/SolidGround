using SolidGround.Core.Http;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0) code-review follow-up: <see cref="OpenTopographyRedaction"/>'s own XML doc
/// comment states its facade is permanently scoped to <see cref="SensitiveQueryParameterNames.OpenTopography"/>
/// only and "must never be widened to <see cref="SensitiveQueryParameterNames.KnownFamilies"/>", because
/// broadening the redacted set there could change this facade's own output for a real OpenTopography response
/// or request URI that happened to contain, for example, a "token=" substring. No test anywhere in the suite
/// exercised that invariant before this file: the pinned <see cref="OpenTopographyRedactionTests"/> never
/// includes a "token=" query parameter or assignment, and the other new Issue #27 test files call the general
/// <see cref="SensitiveQueryRedactor"/> surface directly, never this facade's own <c>RedactUri</c>/
/// <c>RedactText</c> methods. A regression that swapped the facade's preset argument from
/// <c>SensitiveQueryParameterNames.OpenTopography</c> to <c>SensitiveQueryParameterNames.KnownFamilies</c>
/// would therefore pass every existing and other new test silently. These tests call the facade itself with a
/// "token=" input and assert it passes through byte-identical, so that regression fails here immediately. In a
/// new file, not appended to <see cref="OpenTopographyRedactionTests"/>, per this task's rule against editing
/// an existing test file.
/// </summary>
public sealed class OpenTopographyRedactionScopeTests
{
    [Fact]
    public void RedactUriLeavesATokenParameterUntouchedWhileStillRedactingApiKey()
    {
        var uri = new Uri("https://portal.opentopography.org/API/usgsdem?token=untouched&API_Key=abc123");

        string redacted = OpenTopographyRedaction.RedactUri(uri);

        Assert.Equal("https://portal.opentopography.org/API/usgsdem?token=untouched&API_Key=REDACTED", redacted);
    }

    [Fact]
    public void RedactUriLeavesAUriThatOnlyHasATokenParameterCompletelyUnchanged()
    {
        var uri = new Uri("https://example.com/api?token=untouched");

        string redacted = OpenTopographyRedaction.RedactUri(uri);

        Assert.Equal(uri.AbsoluteUri, redacted);
    }

    [Fact]
    public void RedactTextLeavesATokenAssignmentUnredacted()
    {
        string redacted = OpenTopographyRedaction.RedactText("prefix token=untouched suffix", key: null);

        Assert.Equal("prefix token=untouched suffix", redacted);
    }
}
