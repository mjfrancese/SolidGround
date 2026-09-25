using System.Globalization;
using SolidGround.Core.Aois;

namespace SolidGround.Tests;

public sealed class Wgs84EllipsoidTests
{
    private const double ExampleSiteLatitude = 41.591194d;

    [Fact]
    public void MetersPerDegreeLatitudeMatchesAnIndependentReimplementationAndTheDocumentedReferenceValue()
    {
        double expected = ReferenceMetersPerDegreeLatitude(ExampleSiteLatitude);
        double actual = Wgs84Ellipsoid.MetersPerDegreeLatitude(ExampleSiteLatitude);

        AssertRelativelyClose(expected, actual, 1e-9);
        AssertRelativelyClose(111065.3521d, actual, 0.0001d);
    }

    [Fact]
    public void MetersPerDegreeLongitudeMatchesAnIndependentReimplementationAndTheDocumentedReferenceValue()
    {
        double expected = ReferenceMetersPerDegreeLongitude(ExampleSiteLatitude);
        double actual = Wgs84Ellipsoid.MetersPerDegreeLongitude(ExampleSiteLatitude);

        AssertRelativelyClose(expected, actual, 1e-9);
        AssertRelativelyClose(83378.9293d, actual, 0.0001d);
    }

    [Fact]
    public void MetersPerDegreeLongitudeIsZeroAtThePoles()
    {
        Assert.Equal(0d, Wgs84Ellipsoid.MetersPerDegreeLongitude(90d), 6);
        Assert.Equal(0d, Wgs84Ellipsoid.MetersPerDegreeLongitude(-90d), 6);
    }

    [Fact]
    public void MetersPerDegreeLongitudeAtThePolesIsOnlyEffectivelyZeroNotBitExactZero()
    {
        // Math.PI only approximates pi, so cos(+/-90 degrees) is not bit-exact 0.0 in IEEE-754 double
        // precision, and neither is this method's result at either pole -- it is zero only within the
        // floating-point tolerance MetersPerDegreeLongitudeIsZeroAtThePoles (above) actually checks.
        Assert.NotEqual(0d, Wgs84Ellipsoid.MetersPerDegreeLongitude(90d));
        Assert.NotEqual(0d, Wgs84Ellipsoid.MetersPerDegreeLongitude(-90d));
    }

    [Theory]
    [InlineData(-90.0001d)]
    [InlineData(90.0001d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsAnOutOfRangeOrNonFiniteLatitude(double latitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude));
    }

    /// <summary>
    /// An independent reimplementation of the WGS 84 meridional and prime-vertical radius-of-curvature
    /// formulas, kept deliberately separate from <see cref="Wgs84Ellipsoid"/>'s own implementation so this
    /// test cannot pass merely by repeating the same code.
    /// </summary>
    private static double ReferenceMetersPerDegreeLatitude(double latitudeDegrees) => (Math.PI / 180d) * ReferenceRadii(latitudeDegrees).MeridionalRadius;

    private static double ReferenceMetersPerDegreeLongitude(double latitudeDegrees)
    {
        double phi = latitudeDegrees * (Math.PI / 180d);
        return (Math.PI / 180d) * ReferenceRadii(latitudeDegrees).PrimeVerticalRadius * Math.Cos(phi);
    }

    private static (double MeridionalRadius, double PrimeVerticalRadius) ReferenceRadii(double latitudeDegrees)
    {
        const double semiMajorAxis = 6378137d;
        const double inverseFlattening = 298.257223563d;
        double flattening = 1d / inverseFlattening;
        double eccentricitySquared = flattening * (2d - flattening);
        double phi = latitudeDegrees * (Math.PI / 180d);
        double sinPhi = Math.Sin(phi);
        double denominator = 1d - (eccentricitySquared * sinPhi * sinPhi);
        double primeVerticalRadius = semiMajorAxis / Math.Sqrt(denominator);
        double meridionalRadius = semiMajorAxis * (1d - eccentricitySquared) / Math.Pow(denominator, 1.5d);
        return (meridionalRadius, primeVerticalRadius);
    }

    private static void AssertRelativelyClose(double expected, double actual, double relativeTolerance)
    {
        double relativeDifference = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            relativeDifference <= relativeTolerance,
            $"Expected {actual.ToString("R", CultureInfo.InvariantCulture)} to be within relative tolerance " +
            $"{relativeTolerance} of {expected.ToString("R", CultureInfo.InvariantCulture)} (relative difference {relativeDifference}).");
    }
}
