using SolidGround.Core.Transformations;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="SharedCoordinateDetection.LooksAlreadyCoordinated"/> -- the Revit-free replacement for
/// the detection proxy a live Revit 2027 session (manual evidence Step 14.5, 2026-09-27) found to be wrong: a
/// brand-new document opened from Revit's own default template (Default_I_ENU.rte) reports its survey point
/// already <c>Clipped == true</c> at the internal origin, so a proxy that read <c>Clipped</c> refused the
/// opt-in write on an uncoordinated model. This function never takes a "clipped" flag of any kind -- there is
/// no such parameter to pass one through even by mistake. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates detection" section for
/// the full reasoning, the corrected proxy, and this exact live finding.
/// </summary>
public sealed class SharedCoordinateDetectionTests
{
    // Stand-ins for Application.VertexTolerance / a small fixed angle tolerance -- this suite never asserts
    // either equals any specific installed Revit value, only that the function's own comparisons are correct
    // relative to whatever tolerance it is given.
    private const double LengthTolerance = 0.01;
    private const double AngleTolerance = 1e-9;

    [Fact]
    public void ReturnsFalseForTheExactDefaultTemplateCase()
    {
        // The exact live-evidence case (manual evidence Step 14.5, 2026-09-27): a brand-new document from
        // Revit's own default template reports EastWest=0, NorthSouth=0, Elevation=0, Angle=0, survey point
        // Position=(0,0,0), ProjectLocations.Size=1 -- and, no longer relevant to this function at all,
        // Clipped=True. This must read as "never coordinated" (false).
        Assert.False(Call());
    }

    [Fact]
    public void ReturnsTrueWhenMoreThanOneProjectLocationExists()
    {
        Assert.True(Call(projectLocationCount: 2));
    }

    [Fact]
    public void ReturnsTrueWhenEastWestAloneExceedsTheLengthTolerance()
    {
        Assert.True(Call(eastWest: LengthTolerance + 0.5));
    }

    [Fact]
    public void ReturnsTrueWhenNorthSouthAloneExceedsTheLengthTolerance()
    {
        Assert.True(Call(northSouth: LengthTolerance + 0.5));
    }

    [Fact]
    public void ReturnsTrueWhenElevationAloneExceedsTheLengthTolerance()
    {
        Assert.True(Call(elevation: LengthTolerance + 0.5));
    }

    [Fact]
    public void ReturnsTrueWhenANegativeEastWestExceedsTheLengthToleranceInAbsoluteValue()
    {
        // The sign must not matter: only |eastWest| is compared against lengthTolerance.
        Assert.True(Call(eastWest: -(LengthTolerance + 0.5)));
    }

    [Fact]
    public void ReturnsTrueWhenAngleAloneExceedsTheAngleTolerance()
    {
        Assert.True(Call(angle: AngleTolerance + 0.5));
    }

    [Fact]
    public void ReturnsTrueWhenANegativeAngleExceedsTheAngleToleranceInAbsoluteValue()
    {
        Assert.True(Call(angle: -(AngleTolerance + 0.5)));
    }

    [Fact]
    public void ReturnsTrueWhenTheSurveyPointAloneIsFartherThanTheLengthToleranceFromTheOrigin()
    {
        Assert.True(Call(surveyX: LengthTolerance + 0.5));
    }

    [Fact]
    public void ReturnsTrueWhenTheSurveyPointsCombinedDistanceExceedsToleranceEvenThoughNoSingleAxisDoes()
    {
        // A 3-4-5 triangle scaled to 0.3/0.4/0: each axis alone (0.3, 0.4) is under the 1.0 tolerance used
        // here, but the combined Euclidean distance from the origin (0.5) is not -- pins that this is a true
        // 3D distance check on the survey point, not a per-axis check like the project-position comparison.
        Assert.True(Call(surveyX: 0.3, surveyY: 0.4, lengthTolerance: 0.49));
        Assert.False(Call(surveyX: 0.3, surveyY: 0.4, lengthTolerance: 0.5));
    }

    [Fact]
    public void ReturnsFalseWhenEastWestIsExactlyAtTheLengthTolerance()
    {
        // "Exceeds" is strict: sitting exactly on the tolerance boundary does not count as coordinated.
        Assert.False(Call(eastWest: LengthTolerance));
    }

    [Fact]
    public void ReturnsTrueWhenEastWestIsJustBeyondTheLengthTolerance()
    {
        Assert.True(Call(eastWest: Math.BitIncrement(LengthTolerance)));
    }

    [Fact]
    public void ReturnsFalseWhenTheSurveyPointIsExactlyAtTheLengthToleranceDistance()
    {
        Assert.False(Call(surveyX: LengthTolerance));
    }

    [Fact]
    public void ReturnsTrueWhenTheSurveyPointIsJustBeyondTheLengthToleranceDistance()
    {
        Assert.True(Call(surveyX: Math.BitIncrement(LengthTolerance)));
    }

    [Fact]
    public void ReturnsFalseWhenAngleIsExactlyAtTheAngleTolerance()
    {
        Assert.False(Call(angle: AngleTolerance));
    }

    [Fact]
    public void ReturnsTrueWhenAngleIsJustBeyondTheAngleTolerance()
    {
        Assert.True(Call(angle: Math.BitIncrement(AngleTolerance)));
    }

    [Fact]
    public void ReturnsTrueForAWrittenUtmScalePosition()
    {
        // The example site's own recorded local origin (matching SharedCoordinateOriginTests' own
        // ExampleSiteOrigin and docs/architecture/revit-property-line-and-shared-coordinates.md's own
        // placement-record example) -- a real prior write this design must detect, e.g. on the second run
        // against the same document (manual evidence Step 14.6's own second check).
        Assert.True(Call(eastWest: 449674.0, northSouth: 4604563.0, elevation: 183.10));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteEastWest(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(eastWest: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteNorthSouth(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(northSouth: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteElevation(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(elevation: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteAngle(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(angle: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteSurveyPointX(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(surveyX: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteSurveyPointY(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(surveyY: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteSurveyPointZ(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(surveyZ: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteLengthTolerance(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(lengthTolerance: value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ThrowsForANonFiniteAngleTolerance(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(angleTolerance: value));

    [Fact]
    public void ThrowsForANegativeLengthTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(lengthTolerance: -0.01));
    }

    [Fact]
    public void ThrowsForANegativeAngleTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(angleTolerance: -1e-9));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowsWhenProjectLocationCountIsLessThanOne(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Call(projectLocationCount: value));
    }

    private static bool Call(
        double eastWest = 0,
        double northSouth = 0,
        double elevation = 0,
        double angle = 0,
        double surveyX = 0,
        double surveyY = 0,
        double surveyZ = 0,
        int projectLocationCount = 1,
        double lengthTolerance = LengthTolerance,
        double angleTolerance = AngleTolerance) =>
        SharedCoordinateDetection.LooksAlreadyCoordinated(
            eastWest, northSouth, elevation, angle, surveyX, surveyY, surveyZ,
            projectLocationCount, lengthTolerance, angleTolerance);
}
