using System.Globalization;
using SolidGround.Core.Sources;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="LatitudeLongitudePointParser"/> against every observable behavior SolidGround Issue #31
/// (PH3-4)'s interactive dialog needs from it: culture-invariant parsing, WGS 84 range validation, and telling
/// "not shaped like coordinates at all" (an ordinary address) apart from "shaped like coordinates, but out of
/// range" (worth a targeted, non-echoing message). See docs/architecture/revit-interactive-dialog.md "Content
/// model and sections" step 1.
/// </summary>
public sealed class LatitudeLongitudePointParserTests
{
    [Theory]
    [InlineData("41.591194, -93.603806", 41.591194, -93.603806)]
    [InlineData("41.591194,-93.603806", 41.591194, -93.603806)]
    [InlineData("  41.591194 , -93.603806  ", 41.591194, -93.603806)]
    [InlineData("0,0", 0d, 0d)]
    [InlineData("-90, -180", -90d, -180d)]
    [InlineData("90, 180", 90d, 180d)]
    public void TryParseAcceptsAWellFormedCommaSeparatedPair(string text, double expectedLatitude, double expectedLongitude)
    {
        bool result = LatitudeLongitudePointParser.TryParse(text, out double latitude, out double longitude);

        Assert.True(result);
        Assert.Equal(expectedLatitude, latitude);
        Assert.Equal(expectedLongitude, longitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseRejectsNullOrBlankText(string? text)
    {
        Assert.False(LatitudeLongitudePointParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("41.591194")]
    [InlineData("41.591194, -93.603806, 100")]
    public void TryParseRejectsAnythingOtherThanExactlyTwoCommaSeparatedParts(string text)
    {
        Assert.False(LatitudeLongitudePointParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("100 EXAMPLE LOOP, TESTSITE")]
    [InlineData("abc, def")]
    public void TryParseRejectsNonNumericPartsSoAnOrdinaryAddressIsNeverMistakenForCoordinates(string text)
    {
        // "100 EXAMPLE LOOP, TESTSITE" has exactly one comma, like a real coordinate pair, but neither part is
        // numeric -- this must fall through so the dialog sends it to the geocoder unchanged, not reject it.
        Assert.False(LatitudeLongitudePointParser.TryParse(text, out _, out _));
        Assert.False(LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair(text));
    }

    [Theory]
    [InlineData("91, 0")]
    [InlineData("-91, 0")]
    [InlineData("0, 181")]
    [InlineData("0, -181")]
    [InlineData("999, 999")]
    public void TryParseRejectsOutOfRangeValuesButLooksLikeAttemptedCoordinatePairStillReportsTheAttempt(string text)
    {
        Assert.False(LatitudeLongitudePointParser.TryParse(text, out _, out _));
        Assert.True(LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair(text));
    }

    [Theory]
    [InlineData("NaN, 0")]
    [InlineData("Infinity, 0")]
    [InlineData("0, -Infinity")]
    public void TryParseAndLooksLikeAttemptedCoordinatePairBothRejectNonFiniteValues(string text)
    {
        Assert.False(LatitudeLongitudePointParser.TryParse(text, out _, out _));
        Assert.False(LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair(text));
    }

    [Fact]
    public void ParsingIsCultureInvariantRegardlessOfTheCurrentThreadCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // German formatting uses ',' as its decimal separator and would misparse "41.591194, -93.603806"
            // if this type ever used CultureInfo.CurrentCulture instead of CultureInfo.InvariantCulture.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            bool result = LatitudeLongitudePointParser.TryParse("41.591194, -93.603806", out double latitude, out double longitude);

            Assert.True(result);
            Assert.Equal(41.591194, latitude);
            Assert.Equal(-93.603806, longitude);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CoordinatePairOutOfRangeMessageNeverEchoesInputAndIsFixedText()
    {
        // The whole point of a fixed constant: whatever the operator typed is never interpolated into it.
        Assert.DoesNotContain("999", LatitudeLongitudePointParser.CoordinatePairOutOfRangeMessage, StringComparison.Ordinal);
        Assert.Contains("-90", LatitudeLongitudePointParser.CoordinatePairOutOfRangeMessage, StringComparison.Ordinal);
        Assert.Contains("-180", LatitudeLongitudePointParser.CoordinatePairOutOfRangeMessage, StringComparison.Ordinal);
    }
}
