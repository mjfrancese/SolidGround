using System.Globalization;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class NorthAmericanUtmWellKnownTextTests
{
    [Fact]
    public void Zone15WithNavd88MeterProducesTheExactCommittedFixtureBytes()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.prj");
        string expected = File.ReadAllText(path);

        string actual = NorthAmericanUtmWellKnownText.Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DefinitionsIsExactlyTheFortySupportedCodesAscendingWithTheExpectedRealizationAndZone()
    {
        // Independent of NorthAmericanUtmWellKnownText's own Realizations table: every one of these 40 rows
        // was read from https://epsg.io/<code>.wkt on 2026-09-20 (see
        // docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis" section for the full
        // citation table) and confirmed to name "<realization> / UTM zone <N>N".
        (int Code, string Realization, int Zone)[] expected =
        [
            (3717, "NAD83(NSRS2007)", 10), (3718, "NAD83(NSRS2007)", 11), (3719, "NAD83(NSRS2007)", 12),
            (3720, "NAD83(NSRS2007)", 13), (3721, "NAD83(NSRS2007)", 14), (3722, "NAD83(NSRS2007)", 15),
            (3723, "NAD83(NSRS2007)", 16), (3724, "NAD83(NSRS2007)", 17), (3725, "NAD83(NSRS2007)", 18),
            (3726, "NAD83(NSRS2007)", 19),
            (3740, "NAD83(HARN)", 10), (3741, "NAD83(HARN)", 11), (3742, "NAD83(HARN)", 12),
            (3743, "NAD83(HARN)", 13), (3744, "NAD83(HARN)", 14), (3745, "NAD83(HARN)", 15),
            (3746, "NAD83(HARN)", 16), (3747, "NAD83(HARN)", 17), (3748, "NAD83(HARN)", 18),
            (3749, "NAD83(HARN)", 19),
            (6339, "NAD83(2011)", 10), (6340, "NAD83(2011)", 11), (6341, "NAD83(2011)", 12),
            (6342, "NAD83(2011)", 13), (6343, "NAD83(2011)", 14), (6344, "NAD83(2011)", 15),
            (6345, "NAD83(2011)", 16), (6346, "NAD83(2011)", 17), (6347, "NAD83(2011)", 18),
            (6348, "NAD83(2011)", 19),
            (26910, "NAD83", 10), (26911, "NAD83", 11), (26912, "NAD83", 12), (26913, "NAD83", 13),
            (26914, "NAD83", 14), (26915, "NAD83", 15), (26916, "NAD83", 16), (26917, "NAD83", 17),
            (26918, "NAD83", 18), (26919, "NAD83", 19),
        ];

        Assert.Equal(40, expected.Length);
        Assert.Equal(
            expected,
            NorthAmericanUtmWellKnownText.Definitions.Select(d => (d.EpsgCode, d.Realization, d.Zone)));
        Assert.Equal(expected.Select(e => e.Code), NorthAmericanUtmWellKnownText.SupportedEpsgCodes);
    }

    [Fact]
    public void EveryDefinitionParsesToTheExpectedHorizontalAndVerticalReference()
    {
        foreach (NorthAmericanUtmDefinition definition in NorthAmericanUtmWellKnownText.Definitions)
        {
            string text = NorthAmericanUtmWellKnownText.Create(definition.EpsgCode, new VerticalReference("NAVD88", LengthUnit.Meter));

            WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(text);

            string codeText = definition.EpsgCode.ToString(CultureInfo.InvariantCulture);
            Assert.Equal($"EPSG:{codeText}", reference.Horizontal.CoordinateReferenceSystem);
            Assert.Equal(HorizontalReferenceKind.Projected, reference.Horizontal.Kind);
            Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
            Assert.Equal(definition.DatumName, reference.Horizontal.Datum);
            Assert.NotNull(reference.Vertical);
            Assert.Equal("NAVD88", reference.Vertical!.Datum);
            Assert.Equal(LengthUnit.Meter, reference.Vertical.Unit);
        }
    }

    [Fact]
    public void EveryDefinitionBuildsAProjNetTransformThatMapsItsCentralMeridianToFalseEasting()
    {
        // This is also the proof that the pinned ProjNET 2.1.0 accepts a quoted DATUM name containing
        // parentheses through ProjNetHorizontalCoordinateTransformFactory.Create: 30 of these 40 definitions
        // (every realization except plain NAD83) carry a parenthesized DatumName -- "NAD83(HARN)",
        // "NAD83(NSRS2007)", "NAD83(2011)" -- and every one of them builds and applies a transform
        // successfully here, so the parenthesized form did not need the underscore-style fallback
        // ("NAD83_HARN", ...) the design note anticipated in case ProjNET rejected it.
        foreach (NorthAmericanUtmDefinition definition in NorthAmericanUtmWellKnownText.Definitions)
        {
            string targetText = NorthAmericanUtmWellKnownText.Create(definition.EpsgCode, new VerticalReference("NAVD88", LengthUnit.Meter));
            double centralMeridian = (6 * definition.Zone) - 183;

            IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
                ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, targetText);

            Coordinate2D projected = transform.Forward(new Coordinate2D(centralMeridian, 0d));

            string codeText = definition.EpsgCode.ToString(CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(projected.X - 500000d) < 1e-3,
                $"EPSG:{codeText}: expected easting near 500000, got {projected.X.ToString(CultureInfo.InvariantCulture)}.");
            Assert.True(
                Math.Abs(projected.Y - 0d) < 1e-3,
                $"EPSG:{codeText}: expected northing near 0, got {projected.Y.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    [Fact]
    public void Create3745TextContainsTheHarnProjectedNameAtBothInterpolationSitesAndTheParenthesizedDatumName()
    {
        string text = NorthAmericanUtmWellKnownText.Create(3745, new VerticalReference("NAVD88", LengthUnit.Meter));

        Assert.Contains("COMPD_CS[\"NAD_1983_HARN_UTM_Zone_15N + NAVD88_height\"", text, StringComparison.Ordinal);
        Assert.Contains("PROJCS[\"NAD_1983_HARN_UTM_Zone_15N\"", text, StringComparison.Ordinal);
        Assert.Contains("DATUM[\"NAD83(HARN)\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetDefinitionRoundTripsEveryDefinitionAndFailsForAnUnsupportedCode()
    {
        foreach (NorthAmericanUtmDefinition definition in NorthAmericanUtmWellKnownText.Definitions)
        {
            Assert.True(NorthAmericanUtmWellKnownText.TryGetDefinition(definition.EpsgCode, out NorthAmericanUtmDefinition? found));
            Assert.Equal(definition, found);
        }

        Assert.False(NorthAmericanUtmWellKnownText.TryGetDefinition(0, out NorthAmericanUtmDefinition? missing));
        Assert.Null(missing);
    }

    [Theory]
    [InlineData(26901)]
    [InlineData(26909)]
    [InlineData(26920)]
    [InlineData(26923)]
    [InlineData(3739)]
    [InlineData(3750)]
    [InlineData(3716)]
    [InlineData(3727)]
    [InlineData(6328)]
    [InlineData(6330)]
    [InlineData(6338)]
    [InlineData(6349)]
    [InlineData(32615)]
    [InlineData(0)]
    public void UnsupportedCodesThrowArgumentOutOfRange(int code)
    {
        Assert.False(NorthAmericanUtmWellKnownText.IsSupported(code));
        Assert.Throws<ArgumentOutOfRangeException>(() => NorthAmericanUtmWellKnownText.ZoneOf(code));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NorthAmericanUtmWellKnownText.Create(code, new VerticalReference("NAVD88", LengthUnit.Meter)));
    }

    [Theory]
    [InlineData(LengthUnit.Meter)]
    [InlineData(LengthUnit.UsSurveyFoot)]
    [InlineData(LengthUnit.InternationalFoot)]
    public void CreateRendersEveryVerticalUnitWithALiteralTheParserRecognizesWithinTolerance(LengthUnit unit)
    {
        string text = NorthAmericanUtmWellKnownText.Create(26915, new VerticalReference("NAVD88", unit));

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(text);

        Assert.NotNull(reference.Vertical);
        Assert.Equal(unit, reference.Vertical!.Unit);
    }

    [Fact]
    public void CreateRejectsANullVerticalReference()
    {
        Assert.Throws<ArgumentNullException>(() => NorthAmericanUtmWellKnownText.Create(26915, null!));
    }

    [Fact]
    public void NoSupportedCodesTextEverContainsACarriageReturn()
    {
        foreach (int code in NorthAmericanUtmWellKnownText.SupportedEpsgCodes)
        {
            string text = NorthAmericanUtmWellKnownText.Create(code, new VerticalReference("NAVD88", LengthUnit.Meter));
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        }
    }
}
