using System.Globalization;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class WellKnownTextReferenceParserTests
{
    [Fact]
    public void ParsesAnEsriStyleProjcsWithMetreUnits()
    {
        const string wkt = """
            PROJCS["NAD_1983_UTM_Zone_15N",
                GEOGCS["GCS_North_American_1983",
                    DATUM["D_North_American_1983",
                        SPHEROID["GRS_1980",6378137.0,298.257222101]],
                    PRIMEM["Greenwich",0.0],
                    UNIT["Degree",0.0174532925199433]],
                PROJECTION["Transverse_Mercator"],
                PARAMETER["False_Easting",500000.0],
                PARAMETER["False_Northing",0.0],
                PARAMETER["Central_Meridian",-93.0],
                PARAMETER["Scale_Factor",0.9996],
                PARAMETER["Latitude_Of_Origin",0.0],
                UNIT["Meter",1.0]]
            """;

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal("NAD_1983_UTM_Zone_15N", reference.Horizontal.CoordinateReferenceSystem);
        Assert.Contains("North_American_1983", reference.Horizontal.Datum, StringComparison.Ordinal);
        Assert.Equal(HorizontalReferenceKind.Projected, reference.Horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
        Assert.Equal(HorizontalAxisOrder.EastingNorthing, reference.Horizontal.AxisOrder);
        Assert.Null(reference.Vertical);
        Assert.Equal(wkt, reference.Text);
    }

    [Fact]
    public void ParsesAnOgcStyleProjcsWithAnAuthorityCode()
    {
        const string wkt = """
            PROJCS["NAD83 / UTM zone 15N",
                GEOGCS["NAD83",
                    DATUM["North_American_Datum_1983",
                        SPHEROID["GRS 1980",6378137,298.257222101]],
                    PRIMEM["Greenwich",0],
                    UNIT["degree",0.0174532925199433]],
                PROJECTION["Transverse_Mercator"],
                PARAMETER["latitude_of_origin",0],
                PARAMETER["central_meridian",-93],
                PARAMETER["scale_factor",0.9996],
                PARAMETER["false_easting",500000],
                PARAMETER["false_northing",0],
                UNIT["metre",1],
                AUTHORITY["EPSG","26915"]]
            """;

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal("EPSG:26915", reference.Horizontal.CoordinateReferenceSystem);
        Assert.Equal("North_American_Datum_1983", reference.Horizontal.Datum);
        Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
        Assert.Equal(HorizontalAxisOrder.EastingNorthing, reference.Horizontal.AxisOrder);
    }

    [Fact]
    public void RecognizesTheUsSurveyFootConversionFactor()
    {
        string factor = LengthConverter.MetersPerUnit(LengthUnit.UsSurveyFoot).ToString("G17", CultureInfo.InvariantCulture);
        string wkt = $"""PROJCS["Test State Plane",GEOGCS["Test Geographic",DATUM["Test Datum"]],UNIT["US survey foot",{factor}]]""";

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal(LengthUnit.UsSurveyFoot, reference.Horizontal.Unit.LinearUnit);
    }

    [Fact]
    public void RecognizesTheInternationalFootConversionFactor()
    {
        string factor = LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot).ToString("G17", CultureInfo.InvariantCulture);
        string wkt = $"""PROJCS["Test State Plane",GEOGCS["Test Geographic",DATUM["Test Datum"]],UNIT["Foot",{factor}]]""";

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal(LengthUnit.InternationalFoot, reference.Horizontal.Unit.LinearUnit);
    }

    [Fact]
    public void RejectsAnUnsupportedLinearUnit()
    {
        const string wkt = """PROJCS["Test",GEOGCS["Test",DATUM["Test"]],UNIT["Unsupported Unit",2.0]]""";

        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("Unsupported Unit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesAGeographicRootWithAnAuthorityCode()
    {
        const string wkt = """
            GEOGCS["WGS 84",
                DATUM["WGS_1984",
                    SPHEROID["WGS 84",6378137,298.257223563]],
                PRIMEM["Greenwich",0],
                UNIT["degree",0.0174532925199433],
                AUTHORITY["EPSG","4326"]]
            """;

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal("EPSG:4326", reference.Horizontal.CoordinateReferenceSystem);
        Assert.Equal("WGS_1984", reference.Horizontal.Datum);
        Assert.Equal(HorizontalReferenceKind.Geographic, reference.Horizontal.Kind);
        Assert.Equal(HorizontalUnit.DecimalDegrees, reference.Horizontal.Unit);
        Assert.Equal(HorizontalAxisOrder.LongitudeLatitude, reference.Horizontal.AxisOrder);
        Assert.Null(reference.Vertical);
    }

    [Fact]
    public void ParsesAMinimalWkt2ProjcrsInsideACompoundCrs()
    {
        const string wkt = """
            COMPOUNDCRS["NAD83 / UTM zone 15N + NAVD88 height",
                PROJCRS["NAD83 / UTM zone 15N",
                    BASEGEOGCRS["NAD83",DATUM["North American Datum 1983"]],
                    LENGTHUNIT["metre",1],
                    ID["EPSG",26915]],
                VERTCRS["NAVD88 height",
                    VDATUM["North American Vertical Datum 1988"],
                    LENGTHUNIT["metre",1]]]
            """;

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal("EPSG:26915", reference.Horizontal.CoordinateReferenceSystem);
        Assert.Equal("North American Datum 1983", reference.Horizontal.Datum);
        Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
        Assert.NotNull(reference.Vertical);
        Assert.Equal("North American Vertical Datum 1988", reference.Vertical!.Datum);
        Assert.Equal(LengthUnit.Meter, reference.Vertical.Unit);
    }

    [Fact]
    public void ParsesTheOfflineExampleSiteFixturePrjFile()
    {
        string wkt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.prj"));

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(wkt);

        Assert.Equal("EPSG:26915", reference.Horizontal.CoordinateReferenceSystem);
        Assert.Contains("NAD83", reference.Horizontal.Datum, StringComparison.Ordinal);
        Assert.Equal(HorizontalReferenceKind.Projected, reference.Horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
        Assert.Equal(HorizontalAxisOrder.EastingNorthing, reference.Horizontal.AxisOrder);
        Assert.NotNull(reference.Vertical);
        Assert.Equal("NAVD88", reference.Vertical!.Datum);
        Assert.Equal(LengthUnit.Meter, reference.Vertical.Unit);
    }

    [Fact]
    public void RejectsMalformedWktMissingAClosingBracket()
    {
        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse("GEOGCS[\"WGS84\""));

        Assert.Contains("character", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnUnsupportedRootKeyword()
    {
        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse("""FOO["bar"]"""));

        Assert.Contains("not a supported root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsABlankQuotedCoordinateReferenceSystemNameAsAFormatException()
    {
        // Regression test: FirstStringArgument used to return "" (not null) for a present-but-blank quoted
        // name, so the "?? throw new FormatException(...)" guard never fired for it. The blank name instead
        // reached HorizontalReference's constructor, which throws an undocumented ArgumentException instead
        // of the FormatException this parser's own contract promises for malformed WKT.
        const string wkt = """PROJCS["",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0]]""";

        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("quoted name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAWhitespaceOnlyQuotedCoordinateReferenceSystemNameAsAFormatException()
    {
        const string wkt = """PROJCS["   ",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0]]""";

        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("quoted name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsABlankQuotedDatumNameAsAFormatException()
    {
        const string wkt = """PROJCS["Test",GEOGCS["Test",DATUM[""]],UNIT["Meter",1.0]]""";

        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("DATUM", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABlankQuotedVerticalDatumNameAsAFormatException()
    {
        const string wkt = """
            COMPD_CS["Test Compound",
                PROJCS["Test Proj",GEOGCS["Test Geo",DATUM["Test Datum"]],UNIT["Meter",1.0]],
                VERT_CS["Test Vertical",VERT_DATUM[""],UNIT["Meter",1.0]]]
            """;

        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("vertical datum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["",1.0]]""")]
    [InlineData("""PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["   ",1.0]]""")]
    public void RejectsABlankOrWhitespaceOnlyQuotedUnitNameAsAFormatException(string wkt)
    {
        // Regression test: InterpretLengthUnit checked "unitNode.Arguments[0] is not string unitName"
        // directly instead of routing through FirstStringArgument like every other quoted-name lookup in
        // this file (the CRS name, the horizontal datum name, and the vertical datum name). That only
        // rejected a non-string first argument, not a present-but-blank one, so UNIT["",1.0] (a recognized
        // factor paired with a blank name) used to parse successfully as LengthUnit.Meter instead of
        // throwing, unlike the blank-name cases covered above.
        FormatException error = Assert.Throws<FormatException>(() => WellKnownTextReferenceParser.Parse(wkt));

        Assert.Contains("unit name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsNullOrBlankInput(string? wkt)
    {
        // Null specifically throws ArgumentNullException, a subtype of ArgumentException; ThrowsAny
        // accepts either so this verifies the documented "ArgumentException (null/blank)" contract
        // without over-specifying which exact subtype backs the null case.
        Assert.ThrowsAny<ArgumentException>(() => WellKnownTextReferenceParser.Parse(wkt!));
    }
}
