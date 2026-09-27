using SolidGround.Core.Hosting;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="RevitIniToposolidThresholds.Parse"/> against every observable behavior named by
/// SolidGround Issue #15: present values, an absent <c>[Misc]</c> section, an absent key, a malformed value,
/// a key present but outside <c>[Misc]</c>, CRLF line endings, and a leading UTF-8 BOM. This type never reads
/// a real file -- <c>SolidGround.Revit</c>'s Preflight stage owns locating and reading the actual
/// <c>Revit.ini</c> -- so every fixture below is an inline string.
/// </summary>
public sealed class RevitIniToposolidThresholdsTests
{
    [Fact]
    public void ParsesBothValuesFromTheMiscSectionWhenPresentAndWellFormed()
    {
        const string text = """
            [Application]
            Language=ENU

            [Misc]
            NativeToposolidMaxPointThreshold=20000
            LinkToposolidMaxPointThreshold=15000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
        Assert.Equal(15000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void ReturnsBothValuesNullWhenNoMiscSectionExistsAtAll()
    {
        const string text = """
            [Application]
            Language=ENU
            NativeToposolidMaxPointThreshold=20000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Null(result.NativeToposolidMaxPointThreshold);
        Assert.Null(result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void LeavesOneValueNullWhenItsOwnKeyIsAbsentFromAnOtherwisePresentMiscSection()
    {
        const string text = """
            [Misc]
            NativeToposolidMaxPointThreshold=20000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
        Assert.Null(result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void ReturnsNullForAValueThatDoesNotParseAsABase10Integer()
    {
        const string text = """
            [Misc]
            NativeToposolidMaxPointThreshold=not-a-number
            LinkToposolidMaxPointThreshold=20000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Null(result.NativeToposolidMaxPointThreshold);
        Assert.Equal(20000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void IgnoresAKeyThatAppearsOutsideTheMiscSection()
    {
        const string text = """
            [General]
            NativeToposolidMaxPointThreshold=99999

            [Misc]
            LinkToposolidMaxPointThreshold=20000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Null(result.NativeToposolidMaxPointThreshold);
        Assert.Equal(20000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void ParsesCorrectlyWithCrlfLineEndings()
    {
        string text = "[Misc]\r\nNativeToposolidMaxPointThreshold=20000\r\nLinkToposolidMaxPointThreshold=20000\r\n";

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
        Assert.Equal(20000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void ParsesCorrectlyPastALeadingUtf8Bom()
    {
        // The byte order mark codepoint (U+FEFF) is built numerically here, rather than embedded as a raw
        // literal character, to keep this source file's own bytes unambiguous.
        string text = (char)0xFEFF + "[Misc]\nNativeToposolidMaxPointThreshold=20000\nLinkToposolidMaxPointThreshold=20000\n";

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
        Assert.Equal(20000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void MatchesKeysCaseInsensitively()
    {
        const string text = """
            [Misc]
            nativetoposolidmaxpointthreshold=20000
            LINKTOPOSOLIDMAXPOINTTHRESHOLD=15000
            """;

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
        Assert.Equal(15000, result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void TreatsAnEmptyDocumentAsBothValuesAbsentWithoutThrowing()
    {
        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(string.Empty);

        Assert.Null(result.NativeToposolidMaxPointThreshold);
        Assert.Null(result.LinkToposolidMaxPointThreshold);
    }

    [Fact]
    public void ToleratesSurroundingWhitespaceAroundSectionNamesKeysAndValues()
    {
        string text = "  [ Misc ]  \n   NativeToposolidMaxPointThreshold   =   20000   \n";

        RevitIniToposolidThresholds.Thresholds result = RevitIniToposolidThresholds.Parse(text);

        Assert.Equal(20000, result.NativeToposolidMaxPointThreshold);
    }

    [Theory]
    [InlineData(20001, 20000, true)] // over the threshold
    [InlineData(20000, 20000, false)] // exactly at the threshold -- not an excess
    [InlineData(19999, 20000, false)] // under the threshold
    [InlineData(999999, null, false)] // threshold unknown this session -- tolerant-by-design, never true
    public void ExceedsNativeThresholdMatchesPointBudgetAgainstTheParsedNativeThreshold(int pointBudget, int? nativeThreshold, bool expected)
    {
        RevitIniToposolidThresholds.Thresholds thresholds = new(nativeThreshold, LinkToposolidMaxPointThreshold: null);

        bool result = RevitIniToposolidThresholds.ExceedsNativeThreshold(pointBudget, thresholds);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DescribeExceedanceNamesTheBudgetTheThresholdAndTheRevitIniPath()
    {
        // A synthetic placeholder path only -- "[UserName]" matches this repository's own existing convention
        // for an unidentified user profile folder (see CreateToposolidCommand.cs's "Similar to
        // C:\Users\[UserName]\AppData\Roaming\Autodesk\[ProductType]\[ReleaseName]" comment) and is on
        // PersonalInformationGuardTests' own documented placeholder allow-list -- never a real, identifiable
        // person's or client's location (AGENTS.md "Secrets, downloads, and logs").
        const string revitIniPath = @"C:\Users\[UserName]\AppData\Roaming\Autodesk\Revit\Autodesk Revit 2027\Revit.ini";

        string message = RevitIniToposolidThresholds.DescribeExceedance(20001, 20000, revitIniPath);

        Assert.Contains("20001", message, StringComparison.Ordinal);
        Assert.Contains("exceeds this machine's NativeToposolidMaxPointThreshold of 20000", message, StringComparison.Ordinal);
        Assert.Contains(revitIniPath, message, StringComparison.Ordinal);
    }
}
