using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class LengthConverterTests
{
    [Fact]
    public void ConvertsMetersUsingExactUsSurveyFootDefinition()
    {
        double feet = LengthConverter.Convert(1200d / 3937d, LengthUnit.Meter, LengthUnit.UsSurveyFoot);

        Assert.Equal(1d, feet, 12);
    }

    [Fact]
    public void ExposesInternationalFootDefinition()
    {
        Assert.Equal(0.3048d, LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot));
    }
}
