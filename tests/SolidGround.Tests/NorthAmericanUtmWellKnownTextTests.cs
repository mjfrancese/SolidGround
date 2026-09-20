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
    public void SupportedEpsgCodesIsExactly26901Through26923()
    {
        Assert.Equal(Enumerable.Range(26901, 23), NorthAmericanUtmWellKnownText.SupportedEpsgCodes);
    }

    [Fact]
    public void EverySupportedCodeParsesToTheExpectedHorizontalAndVerticalReference()
    {
        foreach (int code in NorthAmericanUtmWellKnownText.SupportedEpsgCodes)
        {
            string text = NorthAmericanUtmWellKnownText.Create(code, new VerticalReference("NAVD88", LengthUnit.Meter));

            WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(text);

            Assert.Equal($"EPSG:{code.ToString(CultureInfo.InvariantCulture)}", reference.Horizontal.CoordinateReferenceSystem);
            Assert.Equal(HorizontalReferenceKind.Projected, reference.Horizontal.Kind);
            Assert.Equal(LengthUnit.Meter, reference.Horizontal.Unit.LinearUnit);
            Assert.Equal("NAD83", reference.Horizontal.Datum);
            Assert.NotNull(reference.Vertical);
            Assert.Equal("NAVD88", reference.Vertical!.Datum);
            Assert.Equal(LengthUnit.Meter, reference.Vertical.Unit);
        }
    }

    [Fact]
    public void EverySupportedCodeBuildsAProjNetTransformThatMapsItsCentralMeridianToFalseEasting()
    {
        foreach (int code in NorthAmericanUtmWellKnownText.SupportedEpsgCodes)
        {
            string targetText = NorthAmericanUtmWellKnownText.Create(code, new VerticalReference("NAVD88", LengthUnit.Meter));
            int zone = NorthAmericanUtmWellKnownText.ZoneOf(code);
            double centralMeridian = (6 * zone) - 183;

            IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
                ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, targetText);

            Coordinate2D projected = transform.Forward(new Coordinate2D(centralMeridian, 0d));

            Assert.True(
                Math.Abs(projected.X - 500000d) < 1e-3,
                $"EPSG:{code.ToString(CultureInfo.InvariantCulture)}: expected easting near 500000, got {projected.X.ToString(CultureInfo.InvariantCulture)}.");
            Assert.True(
                Math.Abs(projected.Y - 0d) < 1e-3,
                $"EPSG:{code.ToString(CultureInfo.InvariantCulture)}: expected northing near 0, got {projected.Y.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    [Theory]
    [InlineData(26900)]
    [InlineData(26924)]
    [InlineData(0)]
    [InlineData(-1)]
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
