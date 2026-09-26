using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="LocalBoundaryValidator.Validate"/>'s retained-sample checks: duplicate (X, Y)
/// positions, containment tolerance (aggregated into one summarized problem line), the point budget, and the
/// <see cref="LocalBoundaryValidator.MinimumRetainedSampleCount"/> floor. Every test here uses an
/// already-well-formed boundary, so only the retained-sample checks can produce a problem --
/// <see cref="LocalBoundaryTests"/> covers the boundary's own ring-shape checks.
/// </summary>
public sealed class LocalBoundaryValidatorTests
{
    [Fact]
    public void ValidateReportsNoProblemsForAWellFormedTriangleAndInteriorPoints()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(3, 1, 12), Sample(1, 1, 11)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ValidateReportsAProblemForDuplicateXyPoints()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(2, 2, 15)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("(X, Y) position", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemForAPointOutsideTheBoundary()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(100, 100, 10)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("outside the boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAggregatesMultipleOutsidePointsIntoOneProblemLineWithACountAndWorstOffset()
    {
        LocalBoundary boundary = SquareBoundary();
        // Directly west of the square's west edge (x = 0), at y = 5: the nearest boundary point for each is
        // (0, 5), so each point's offset is exactly the absolute value of its own x.
        LocalTerrainSample[] samples = [Sample(-1, 5, 0), Sample(-2, 5, 0), Sample(-5, 5, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        string[] outsideProblems = [.. result.Problems.Where(p => p.Contains("outside the boundary", StringComparison.Ordinal))];
        string problem = Assert.Single(outsideProblems);
        Assert.Contains("3", problem, StringComparison.Ordinal);
        Assert.Contains("5", problem, StringComparison.Ordinal);
    }

    // Regression coverage for SolidGround Issue #15's review fix: Validate's containmentToleranceMeters is
    // compared with no unit conversion against boundary/sample coordinates, which a LocalCoordinateFrame
    // expresses in its own OutputUnit (US survey foot by default), not always meters. A caller whose
    // coordinates are in a non-meter unit (SolidGround.Revit's CreateToposolidCommand, in production) must
    // convert DefaultContainmentToleranceMeters into that same unit -- exactly what these two tests simulate
    // by treating the boundary/samples below as US-survey-foot-valued and pre-converting the tolerance the
    // same way LengthConverter.Convert(LocalBoundaryValidator.DefaultContainmentToleranceMeters, ...) does.

    [Fact]
    public void ValidateAcceptsASampleWithinTheDefaultToleranceOnceItIsConvertedIntoTheCallersUnit()
    {
        LocalBoundary boundary = SquareBoundary();
        double toleranceInCallersUnit = LengthConverter.Convert(
            LocalBoundaryValidator.DefaultContainmentToleranceMeters, LengthUnit.Meter, LengthUnit.UsSurveyFoot);
        // 5 mm outside the west edge (x = 0), expressed in US survey feet: well within the intended 1 cm
        // (0.01 m) default tolerance, but outside the *raw* 0.01 literal a caller would wrongly compare
        // against if it forgot to convert -- 5 mm in feet is larger than 0.01.
        double offsetInFeet = LengthConverter.Convert(0.005d, LengthUnit.Meter, LengthUnit.UsSurveyFoot);
        // Two more interior samples so the retained count meets MinimumRetainedSampleCount; both are safely
        // inside the square, so neither adds its own tolerance problem.
        LocalTerrainSample[] samples = [Sample(-offsetInFeet, 5, 0), Sample(2, 2, 0), Sample(5, 5, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100, toleranceInCallersUnit);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ValidateRejectsASampleBeyondTheDefaultToleranceOnceItIsConvertedIntoTheCallersUnit()
    {
        LocalBoundary boundary = SquareBoundary();
        double toleranceInCallersUnit = LengthConverter.Convert(
            LocalBoundaryValidator.DefaultContainmentToleranceMeters, LengthUnit.Meter, LengthUnit.UsSurveyFoot);
        // 15 mm outside the west edge: genuinely beyond the intended 1 cm default tolerance even after the
        // correct unit conversion.
        double offsetInFeet = LengthConverter.Convert(0.015d, LengthUnit.Meter, LengthUnit.UsSurveyFoot);
        LocalTerrainSample[] samples = [Sample(-offsetInFeet, 5, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100, toleranceInCallersUnit);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("outside the boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemWhenRetainedCountExceedsBudget()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0), Sample(1, 2, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 2);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("point budget", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsRetainedCountExactlyAtBudget()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0), Sample(1, 2, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 3);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    // Regression coverage for SolidGround Issue #15's review fix: without this minimum, a clip region yielding
    // zero, one, or two retained samples fell through Geometry Preflight unrejected, reaching
    // CreateToposolidCommand's Stage 4 `points.Min(point => point.Z)` (zero samples) or a bare Revit API
    // rejection deep inside Stage 5 (one or two samples) instead of this actionable Preflight message.

    [Fact]
    public void ValidateRejectsZeroRetainedSamplesWithAnActionableMessage()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Problems,
            p => p.Contains("Only 0 terrain sample(s) were retained", StringComparison.Ordinal)
                && p.Contains("NODATA hole", StringComparison.Ordinal)
                && p.Contains("buffer", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsTwoRetainedSamples()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Problems,
            p => p.Contains("Only 2 terrain sample(s) were retained", StringComparison.Ordinal)
                && p.Contains("NODATA hole", StringComparison.Ordinal)
                && p.Contains("buffer", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsExactlyThreeRetainedSamples()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0), Sample(1, 2, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    // Regression coverage for SolidGround Issue #30 (PH3-3), stage 1: Validate's new, optional minimumEdgeLength
    // parameter -- a defense-in-depth backstop for whatever LocalBoundaryCleaner.Clean did not already repair --
    // and its null default, which must leave every pre-existing caller's behavior byte-for-byte unchanged. See
    // docs/architecture/revit-property-line-and-shared-coordinates.md's "Geometry cleanup contract" section.

    [Fact]
    public void ValidateRejectsAnEdgeShorterThanTheSuppliedMinimumEdgeLength()
    {
        // (0,0)-(0.05,0) is a genuine, nonzero 0.05-length edge -- not a zero-length duplicate -- shorter than
        // the supplied minimumEdgeLength of 0.1.
        LocalBoundary boundary = PentagonWithAShortEdge();
        LocalTerrainSample[] samples = [Sample(5, 5, 0), Sample(2, 2, 0), Sample(8, 8, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(
            boundary, samples, pointBudget: 1000, containmentToleranceMeters: null, minimumEdgeLength: 0.1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("shorter than the minimum edge length", StringComparison.Ordinal));
        // Distinct from the pre-existing zero-length-edge wording: this is a different, separately actionable finding.
        Assert.DoesNotContain(result.Problems, p => p.Contains("zero-length edge", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsWhenMinimumEdgeLengthIsNull()
    {
        // The identical short edge as above, but minimumEdgeLength is left at its default (null): this specific
        // check must not fire, documenting the default/backward-compatible behavior explicitly.
        LocalBoundary boundary = PentagonWithAShortEdge();
        LocalTerrainSample[] samples = [Sample(5, 5, 0), Sample(2, 2, 0), Sample(8, 8, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 1000);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    // Regression coverage from code review of Issue #30 (PH3-3) stage 1: the two tests above never exercise a
    // ring with two or more edges below minimumEdgeLength, so neither the "aggregate into one problem line, not
    // one per edge" contract nor the running-minimum computation in ValidateRingShape was previously verified.
    // Mirrors ValidateAggregatesMultipleOutsidePointsIntoOneProblemLineWithACountAndWorstOffset's own
    // count-plus-worst-value aggregation pattern above, but for short edges instead of out-of-boundary samples.

    [Fact]
    public void ValidateAggregatesMultipleShortEdgesIntoOneProblemLineWithTheShortestLength()
    {
        // Two distinct short edges -- (0,0)-(0.02,0) at 0.02 and (0.05,10)-(0,10) at 0.05 -- both below the
        // supplied minimumEdgeLength of 0.1, at different lengths so the running-minimum computation actually
        // has two distinct values to choose between.
        LocalBoundary boundary = HexagonWithTwoShortEdgesOfDifferentLengths();
        LocalTerrainSample[] samples = [Sample(5, 5, 0), Sample(2, 2, 0), Sample(8, 8, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(
            boundary, samples, pointBudget: 1000, containmentToleranceMeters: null, minimumEdgeLength: 0.1);

        Assert.False(result.IsValid);
        string[] shortEdgeProblems =
            [.. result.Problems.Where(p => p.Contains("shorter than the minimum edge length", StringComparison.Ordinal))];
        string problem = Assert.Single(shortEdgeProblems);
        Assert.Contains("2 edge(s)", problem, StringComparison.Ordinal);
        Assert.Contains("0.02", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("0.05", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateDoesNotDoubleCountAZeroLengthEdgeAsAlsoAShortEdge()
    {
        // (0,0) is duplicated verbatim -- a genuine zero-length edge -- and, separately, (0.03,10)-(0,10) is a
        // distinct, nonzero 0.03-length edge below the supplied minimumEdgeLength of 0.1. ValidateRingShape's
        // own "continue" for a zero-length edge must keep it from also being tallied as a short edge: without
        // it, this ring would wrongly report 2 short edges (the zero-length one included, at length 0) instead
        // of the correct 1.
        LocalBoundary boundary = HexagonWithADuplicateVertexAndASeparateShortEdge();
        LocalTerrainSample[] samples = [Sample(5, 5, 0), Sample(2, 2, 0), Sample(8, 8, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(
            boundary, samples, pointBudget: 1000, containmentToleranceMeters: null, minimumEdgeLength: 0.1);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("zero-length edge", StringComparison.Ordinal));
        string[] shortEdgeProblems =
            [.. result.Problems.Where(p => p.Contains("shorter than the minimum edge length", StringComparison.Ordinal))];
        string problem = Assert.Single(shortEdgeProblems);
        Assert.Contains("1 edge(s)", problem, StringComparison.Ordinal);
        Assert.Contains("0.03", problem, StringComparison.Ordinal);
    }

    private static LocalTerrainSample Sample(double x, double y, double elevation) => new(new LocalCoordinate(x, y, elevation));

    private static LocalBoundary TriangleBoundary()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundary SquareBoundary()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundary PentagonWithAShortEdge()
    {
        // A rectangle with one extra vertex 0.05 away from (0,0), along the bottom edge -- a valid, simple,
        // positive-area pentagon whose only unusual feature is that one edge is very short.
        LocalBoundaryRing shell = Ring((0, 0), (0.05, 0), (10, 0), (10, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundary HexagonWithTwoShortEdgesOfDifferentLengths()
    {
        // A square with two extra vertices, one near the (0,0) corner and one near the (0,10) corner, forming
        // two distinct short edges: (0,0)-(0.02,0) at length 0.02, and (0.05,10)-(0,10) at length 0.05. Each
        // short edge is anchored to an exact 0 coordinate on its differing axis (unlike, say, (10,10)-(9.95,10),
        // whose length would carry floating-point representation noise from subtracting two non-zero literals
        // and so would not format back to a clean "0.05" for the assertions below).
        LocalBoundaryRing shell = Ring((0, 0), (0.02, 0), (10, 0), (10, 10), (0.05, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundary HexagonWithADuplicateVertexAndASeparateShortEdge()
    {
        // (0,0) is duplicated verbatim (a zero-length edge); (0.03,10)-(0,10) is a separate, nonzero
        // 0.03-length short edge elsewhere in the same ring, anchored to an exact 0 coordinate on its differing
        // axis so its formatted length is exactly "0.03" (see HexagonWithTwoShortEdgesOfDifferentLengths).
        LocalBoundaryRing shell = Ring((0, 0), (0, 0), (10, 0), (10, 10), (0.03, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundaryRing Ring(params (double X, double Y)[] vertices) =>
        new([.. vertices.Select(v => new LocalCoordinate2D(v.X, v.Y))]);
}
