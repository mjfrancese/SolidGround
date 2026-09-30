namespace SolidGround.Tests;

public sealed class DistanceDisplayConverterTests
{
    [Theory]
    [InlineData("usSurveyFeet", 1.0, 1200d / 3937d)]
    [InlineData("internationalFeet", 1.0, 0.3048)]
    [InlineData("internationalInches", 12.0, 0.3048)]
    [InlineData("feetAndInches", 1.0, 0.3048)]
    [InlineData("metres", 0.3048, 0.3048)]
    public void ParsesEachDisplayFormatWithoutChangingPhysicalDistance(string format, double displayed, double expectedMeters)
    {
        Type converter = Type.GetType("SolidGround.Core.Units.DistanceDisplayConverter, SolidGround.Core")
            ?? throw new InvalidOperationException("Distance display converter missing.");
        Type enumType = Type.GetType("SolidGround.Core.Units.DistanceDisplayFormat, SolidGround.Core")!;
        object formatValue = Enum.Parse(enumType, char.ToUpperInvariant(format[0]) + format[1..]);
        double meters = (double)converter.GetMethod("ParseMeters")!.Invoke(null, [displayed.ToString(System.Globalization.CultureInfo.InvariantCulture), formatValue])!;

        Assert.Equal(expectedMeters, meters, 12);
    }

    [Fact]
    public void FeetAndInchesRoundTripsWithoutRoundingTheStoredMeters()
    {
        Type converter = Type.GetType("SolidGround.Core.Units.DistanceDisplayConverter, SolidGround.Core")
            ?? throw new InvalidOperationException("Distance display converter missing.");
        Type enumType = Type.GetType("SolidGround.Core.Units.DistanceDisplayFormat, SolidGround.Core")!;
        object format = Enum.Parse(enumType, "FeetAndInches");
        double original = 1.234567890123d;
        string text = (string)converter.GetMethod("FormatMeters")!.Invoke(null, [original, format])!;
        double recovered = (double)converter.GetMethod("ParseMeters")!.Invoke(null, [text, format])!;

        Assert.Equal(original, recovered, 10);
    }
}
