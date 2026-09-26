using SolidGround.Core.Http;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0): the provider-agnostic redactor <see cref="OpenTopographyRedactionTests"/>'s
/// pinned <c>OpenTopographyRedaction</c> facade now delegates to. These tests exercise the general
/// <see cref="SensitiveQueryRedactor"/> surface directly -- a configurable parameter-name set, not one
/// provider's hardcoded name -- while <see cref="OpenTopographyRedactionTests"/> (untouched by this issue)
/// keeps proving the facade's own byte-identical behavior.
/// </summary>
public sealed class SensitiveQueryRedactorTests
{
    [Fact]
    public void RedactUriRedactsOpenTopographysKnownParameterFamily()
    {
        var uri = new Uri("https://portal.opentopography.org/API/usgsdem?datasetName=USGS1m&API_Key=abc123");

        string redacted = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.OpenTopography);

        Assert.Equal("https://portal.opentopography.org/API/usgsdem?datasetName=USGS1m&API_Key=REDACTED", redacted);
    }

    [Fact]
    public void RedactUriRedactsAGenericLowercaseApiKeyParameterCaseInsensitively()
    {
        // Same SensitiveQueryParameterNames.OpenTopography preset as the test above: proves case-insensitive
        // matching (AC1's literal "a generic api_key=" wording), not a materially distinct provider family --
        // "api_key" and "API_Key" are definitionally the same name under this redactor's case-insensitive
        // contract. See RedactUriRedactsEsrisTokenParameter and
        // RedactUriIsConfigurableToAnArbitraryCustomParameterName below for the substantive "configurable,
        // not hardcoded to one provider's parameter name" proof.
        var uri = new Uri("https://example.com/api?foo=1&api_key=abc123&bar=2");

        string redacted = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.OpenTopography);

        Assert.Equal("https://example.com/api?foo=1&api_key=REDACTED&bar=2", redacted);
    }

    [Fact]
    public void RedactUriRedactsEsrisTokenParameter()
    {
        var uri = new Uri("https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/find?token=abc123&f=json");

        string redacted = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.Esri);

        Assert.Equal("https://geocode.arcgis.com/arcgis/rest/services/World/GeocodeServer/find?token=REDACTED&f=json", redacted);
    }

    [Fact]
    public void RedactUriIsConfigurableToAnArbitraryCustomParameterName()
    {
        var uri = new Uri("https://example.com/api?secret=abc123&other=1");

        string redactedWithCustomName = SensitiveQueryRedactor.RedactUri(uri, ["secret"]);
        Assert.Equal("https://example.com/api?secret=REDACTED&other=1", redactedWithCustomName);

        // Proves genuine configurability, not a hidden universal parameter list: an unrelated preset must
        // leave this arbitrary name alone.
        string redactedWithUnrelatedPreset = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.OpenTopography);
        Assert.Equal(uri.AbsoluteUri, redactedWithUnrelatedPreset);
    }

    [Fact]
    public void RedactUriLeavesUnlistedParametersAndNoQueryStringByteIdentical()
    {
        var withQuery = new Uri("https://example.com/api?foo=1&bar=2");
        Assert.Equal(withQuery.AbsoluteUri, SensitiveQueryRedactor.RedactUri(withQuery, SensitiveQueryParameterNames.OpenTopography));

        var withoutQuery = new Uri("https://example.com/api");
        Assert.Equal(withoutQuery.AbsoluteUri, SensitiveQueryRedactor.RedactUri(withoutQuery, SensitiveQueryParameterNames.OpenTopography));
    }

    [Fact]
    public void RedactUriWorksOnARelativeUri()
    {
        var uri = new Uri("/api?token=abc123", UriKind.Relative);

        string redacted = SensitiveQueryRedactor.RedactUri(uri, SensitiveQueryParameterNames.Esri);

        Assert.Equal("/api?token=REDACTED", redacted);
    }

    [Fact]
    public void RedactTextRedactsAFreeFormLogLineEmbeddingAUrl()
    {
        string redacted = SensitiveQueryRedactor.RedactText(
            "request failed for https://example.com/geocode?token=abc123&format=json",
            SensitiveQueryParameterNames.Esri);

        Assert.Equal("request failed for https://example.com/geocode?[REDACTED]&format=json", redacted);
    }

    [Fact]
    public void RedactTextRedactsTheSuppliedSecretsRawAndEscapedForm()
    {
        var key = new ApiKey("myFakeKey123");
        string redacted = SensitiveQueryRedactor.RedactText("token is myFakeKey123 end", SensitiveQueryParameterNames.OpenTopography, key);
        Assert.Equal("token is [REDACTED] end", redacted);

        var escapedKey = new ApiKey("abc def/ghi");
        string escaped = Uri.EscapeDataString("abc def/ghi");
        string redactedEscaped = SensitiveQueryRedactor.RedactText($"redirect?value={escaped}&x=1", SensitiveQueryParameterNames.OpenTopography, escapedKey);
        Assert.Equal("redirect?value=[REDACTED]&x=1", redactedEscaped);
        Assert.DoesNotContain("abc", redactedEscaped, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactUriThrowsOnNullUri()
    {
        Assert.Throws<ArgumentNullException>(() => SensitiveQueryRedactor.RedactUri(null!, SensitiveQueryParameterNames.OpenTopography));
    }

    [Fact]
    public void RedactTextThrowsOnNullText()
    {
        Assert.Throws<ArgumentNullException>(() => SensitiveQueryRedactor.RedactText(null!, SensitiveQueryParameterNames.OpenTopography));
    }

    [Fact]
    public void RedactUriThrowsWhenNoSensitiveParameterNameIsSupplied()
    {
        var uri = new Uri("https://example.com/api?token=abc");

        Assert.Throws<ArgumentNullException>(() => SensitiveQueryRedactor.RedactUri(uri, null!));
        Assert.Throws<ArgumentException>(() => SensitiveQueryRedactor.RedactUri(uri, []));
        Assert.Throws<ArgumentException>(() => SensitiveQueryRedactor.RedactUri(uri, [" "]));
    }

    [Fact]
    public void RedactTextThrowsWhenNoSensitiveParameterNameIsSupplied()
    {
        Assert.Throws<ArgumentNullException>(() => SensitiveQueryRedactor.RedactText("text", null!));
        Assert.Throws<ArgumentException>(() => SensitiveQueryRedactor.RedactText("text", []));
        Assert.Throws<ArgumentException>(() => SensitiveQueryRedactor.RedactText("text", [" "]));
    }
}
