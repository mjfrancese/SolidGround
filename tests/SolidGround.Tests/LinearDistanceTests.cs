using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class LinearDistanceTests
{
    [Fact]
    public void ZeroIsAZeroLengthMeterDistance()
    {
        Assert.Equal(0d, LinearDistance.Zero.Value);
        Assert.Equal(LengthUnit.Meter, LinearDistance.Zero.Unit);
        Assert.Equal(0d, LinearDistance.Zero.ToMeters());
    }

    [Fact]
    public void MetersFactoryCreatesAMeterDistance()
    {
        LinearDistance distance = LinearDistance.Meters(12.5d);

        Assert.Equal(12.5d, distance.Value);
        Assert.Equal(LengthUnit.Meter, distance.Unit);
    }

    [Fact]
    public void ConstructorAcceptsExactlyZero()
    {
        LinearDistance distance = new(0d, LengthUnit.UsSurveyFoot);

        Assert.Equal(0d, distance.Value);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ConstructorRejectsNegativeOrNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearDistance(value, LengthUnit.Meter));
    }

    [Fact]
    public void ToMetersAndInAreExactForUsSurveyFoot()
    {
        LinearDistance oneUsSurveyFoot = new(1d, LengthUnit.UsSurveyFoot);

        Assert.Equal(1200d / 3937d, oneUsSurveyFoot.ToMeters(), 15);
        Assert.Equal(1d, oneUsSurveyFoot.In(LengthUnit.UsSurveyFoot), 15);

        LinearDistance fromMeters = LinearDistance.Meters(1200d / 3937d);
        Assert.Equal(1d, fromMeters.In(LengthUnit.UsSurveyFoot), 12);
    }

    [Fact]
    public void ToMetersAndInAreExactForInternationalFoot()
    {
        LinearDistance oneInternationalFoot = new(1d, LengthUnit.InternationalFoot);

        Assert.Equal(0.3048d, oneInternationalFoot.ToMeters(), 15);
        Assert.Equal(1d, oneInternationalFoot.In(LengthUnit.InternationalFoot), 15);

        LinearDistance fromMeters = LinearDistance.Meters(0.3048d);
        Assert.Equal(1d, fromMeters.In(LengthUnit.InternationalFoot), 12);
    }

    [Fact]
    public void InConvertsBetweenTwoNonMeterUnits()
    {
        LinearDistance oneInternationalFoot = new(1d, LengthUnit.InternationalFoot);

        double usSurveyFeet = oneInternationalFoot.In(LengthUnit.UsSurveyFoot);

        Assert.Equal(0.3048d * 3937d / 1200d, usSurveyFeet, 12);
    }
}
