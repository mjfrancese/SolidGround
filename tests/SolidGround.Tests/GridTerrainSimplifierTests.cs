using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class GridTerrainSimplifierTests
{
    [Fact]
    public async Task RidgeCellsAreRetainedAheadOfFlatterInteriorCellsUnderAModestBudget()
    {
        // coverageFloorFraction is 0 here to isolate curvature-ranked interior fill (Pass 2) from the
        // coverage-floor mechanism (Pass 3), which is exercised deliberately by
        // AFlatPlaneRetainsOnlyPerimeterStructuralPointsPlusACoverageFloorSpreadAcrossTheInterior below.
        ElevationGrid grid = BuildGrid(15, 15, (row, column) => 10d - (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int perimeterCount = (2 * 15) + (2 * 15) - 4;
        const int ridgeInteriorCellCount = 15 - 2; // interior rows 1..13 each contribute exactly one column-7 cell.

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + ridgeInteriorCellCount), TestContext.Current.CancellationToken);

        Assert.True(result.RetainedPointCount <= perimeterCount + ridgeInteriorCellCount);
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            int row = (int)Math.Round(sample.Position.Y - 0.5d);
            bool isPerimeter = row is 0 or 14 || column is 0 or 14;
            if (!isPerimeter)
            {
                // SolidGround Issue #7 review: this elevation function is piecewise linear with its only kink
                // at column 7, so curvatureAllotment (13) exactly equals the count of nonzero-curvature
                // candidates (the 13 column-7 interior cells, one per interior row) with zero ties against the
                // 156 zero-curvature interior cells. The correct output is therefore deterministically exactly
                // column 7 for every interior retained sample, not merely "on or adjacent to" it; a
                // one-column peak-detection shift in ComputeCellMetrics must fail this assertion.
                Assert.Equal(7, column);
            }
        }

        Assert.True(
            result.Diagnostics!.MaxRemovedCurvatureMagnitude < 0.01d,
            "The maximum removed curvature magnitude should belong to a flat-flank cell (~0), not an unretained ridge cell (0.4).");
    }

    [Fact]
    public async Task SwaleCellsAreRetainedAheadOfFlatterInteriorCellsUnderAModestBudget()
    {
        ElevationGrid grid = BuildGrid(15, 15, (row, column) => -10d + (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int perimeterCount = (2 * 15) + (2 * 15) - 4;
        const int swaleInteriorCellCount = 15 - 2;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + swaleInteriorCellCount), TestContext.Current.CancellationToken);

        Assert.True(result.RetainedPointCount <= perimeterCount + swaleInteriorCellCount);
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            int row = (int)Math.Round(sample.Position.Y - 0.5d);
            bool isPerimeter = row is 0 or 14 || column is 0 or 14;
            if (!isPerimeter)
            {
                // SolidGround Issue #7 review: mirrors the ridge test above with the sign flipped. |Cxx| is
                // unaffected by that sign flip, so curvatureAllotment (13) again exactly equals the count of
                // nonzero-curvature candidates (the 13 column-7 interior cells), making column 7 the
                // deterministically exact answer for every interior retained sample here too.
                Assert.Equal(7, column);
            }
        }

        Assert.True(
            result.Diagnostics!.MaxRemovedCurvatureMagnitude < 0.01d,
            "The maximum removed curvature magnitude should belong to a flat-flank cell (~0), not an unretained swale cell (0.4).");
    }

    [Fact]
    public async Task SaddleShapedTerrainIsNotCancelledByOppositeAxisCurvatureSigns()
    {
        ElevationGrid grid = BuildGrid(11, 11, (row, column) => (double)(((row - 5) * (row - 5)) - ((column - 5) * (column - 5))));
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int perimeterCount = (2 * 11) + (2 * 11) - 4; // 40
        const int interiorBudget = 20; // well under the 81 true-interior candidates, all tied at |Cxx|+|Cyy|=4.

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + interiorBudget), TestContext.Current.CancellationToken);

        Assert.Equal(perimeterCount, result.Diagnostics!.StructuralPointCount);
        Assert.Equal(perimeterCount + interiorBudget, result.RetainedPointCount);

        // Every true-interior cell of this exact quadratic (rows/columns 1-9) has Cxx = -2 and Cyy = +2
        // everywhere, including the saddle center (5,5): a naive |Cxx + Cyy| implementation would cancel these
        // to exactly 0 for a symmetric saddle. The actual |Cxx| + |Cyy| = 4 formula never cancels. The center
        // is not among the 20 curvature-ranked interior cells retained (row-major order reaches (5,5) only
        // after 41 interior cells), so it is one of the removed candidates feeding these statistics.
        Assert.Equal(4d, result.Diagnostics.MaxRemovedCurvatureMagnitude);
        Assert.Equal(4d, result.Diagnostics.MeanRemovedCurvatureMagnitude);
        Assert.True(result.Diagnostics.MaxRemovedCurvatureMagnitude > 1d, "A |Cxx + Cyy| cancellation bug would produce ~0 here, not 4.");
    }

    [Fact]
    public async Task EveryArrayPerimeterCellIsStructuralAndRetainedWhenTheBudgetAllows()
    {
        ElevationGrid grid = UniformGrid(10, 10, 3d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new();
        const int perimeterCount = (2 * 10) + (2 * 10) - 4; // 36

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 5), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(perimeterCount, diagnostics.StructuralCandidateCount);
        Assert.Equal(perimeterCount, diagnostics.StructuralPointCount);
        Assert.False(diagnostics.StructuralBudgetShortfall);

        for (int row = 0; row < 10; row++)
        {
            for (int column = 0; column < 10; column++)
            {
                if (row is 0 or 9 || column is 0 or 9)
                {
                    Coordinate2D center = grid.GetCellCenter(row, column);
                    Assert.Contains(result.RetainedSamples, sample => sample.Position.X == center.X && sample.Position.Y == center.Y);
                }
            }
        }
    }

    [Fact]
    public async Task InteriorNoDataHoleRimCellsAreRetainedAndTheHoleNeverProducesASample()
    {
        double?[,] elevations = BuildElevations(
            12, 12, (row, column) => row is >= 5 and <= 6 && column is >= 5 and <= 6
                ? null
                : 50d + (row * 0.1d) + (column * 0.05d));
        ElevationGrid grid = ToGrid(elevations);
        int expectedStructuralCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(4, diagnostics.NoDataOrExcludedCellCount);
        Assert.Equal(144 - 4, result.OriginalPointCount);
        Assert.Equal(expectedStructuralCount, diagnostics.StructuralCandidateCount);
        Assert.Equal(result.OriginalPointCount, result.RetainedPointCount);

        Coordinate2D[] holeCenters =
        [
            grid.GetCellCenter(5, 5), grid.GetCellCenter(5, 6), grid.GetCellCenter(6, 5), grid.GetCellCenter(6, 6),
        ];
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            foreach (Coordinate2D holeCenter in holeCenters)
            {
                Assert.False(sample.Position.X == holeCenter.X && sample.Position.Y == holeCenter.Y);
            }
        }
    }

    [Fact]
    public async Task InteriorNoDataHoleRimCellsSurviveATightBudgetEqualToTheStructuralCandidateCount()
    {
        // Companion to InteriorNoDataHoleRimCellsAreRetainedAndTheHoleNeverProducesASample above: that test
        // uses pointBudget: 1000, far above the 140 valid cells in this 12x12 grid, so every valid cell is
        // retained regardless of structural classification (SolidGround Issue #7 review). Setting the budget
        // to exactly the (independently computed) structural candidate count leaves zero budget for Pass 2/3,
        // so only cells GridTerrainSimplifier itself classifies structural survive, making retention of the
        // diagonal-only hole-rim corners below a genuine proof of 8-connectivity rather than an artifact of a
        // generous budget.
        double?[,] elevations = BuildElevations(
            12, 12, (row, column) => row is >= 5 and <= 6 && column is >= 5 and <= 6
                ? null
                : 50d + (row * 0.1d) + (column * 0.05d));
        ElevationGrid grid = ToGrid(elevations);
        int expectedStructuralCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: expectedStructuralCount), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(expectedStructuralCount, result.RetainedPointCount);
        Assert.Equal(expectedStructuralCount, diagnostics.StructuralPointCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(0, diagnostics.CoverageFloorPointCount);
        Assert.False(diagnostics.StructuralBudgetShortfall);

        // (4,4)/(4,7)/(7,4)/(7,7) touch the 2x2 hole only diagonally (at its four corners), never orthogonally,
        // so they are exactly the cells a 4-connectivity bug would misclassify as interior.
        Coordinate2D[] diagonalHoleCornerCenters =
        [
            grid.GetCellCenter(4, 4), grid.GetCellCenter(4, 7), grid.GetCellCenter(7, 4), grid.GetCellCenter(7, 7),
        ];
        foreach (Coordinate2D center in diagonalHoleCornerCenters)
        {
            Assert.Contains(result.RetainedSamples, sample => sample.Position.X == center.X && sample.Position.Y == center.Y);
        }
    }

    [Fact]
    public async Task DiagonallyAdjacentCellsNextToAHoleAreAlsoClassifiedStructural()
    {
        double?[,] elevations = BuildElevations(9, 9, (row, column) => row == 4 && column == 4 ? null : 30d);
        ElevationGrid grid = ToGrid(elevations);
        int expectedStructuralCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        Assert.Equal(expectedStructuralCount, result.Diagnostics!.StructuralCandidateCount);

        (int Row, int Column)[] diagonalNeighbors = [(3, 3), (3, 5), (5, 3), (5, 5)];
        foreach ((int row, int column) in diagonalNeighbors)
        {
            Assert.True(CountMissingNeighborsIndependent(elevations, row, column) >= 1);
            Coordinate2D expectedCenter = grid.GetCellCenter(row, column);
            Assert.Contains(result.RetainedSamples, sample => sample.Position.X == expectedCenter.X && sample.Position.Y == expectedCenter.Y);
        }
    }

    [Fact]
    public async Task DiagonallyAdjacentCellsNextToAHoleSurviveATightBudgetEqualToTheStructuralCandidateCount()
    {
        // Companion to DiagonallyAdjacentCellsNextToAHoleAreAlsoClassifiedStructural above: that test uses
        // pointBudget: 1000, far above the 80 valid cells in this 9x9 grid, so every valid cell is retained
        // regardless of structural classification and its Assert.Contains checks cannot distinguish correct
        // 8-connectivity from a 4-connectivity bug (SolidGround Issue #7 review). Setting the budget to exactly
        // the (independently computed) structural candidate count leaves zero budget for Pass 2/3, so only
        // cells GridTerrainSimplifier itself classifies structural survive; a 4-connectivity bug would
        // reclassify the diagonal cells as interior and starve them of budget entirely.
        double?[,] elevations = BuildElevations(9, 9, (row, column) => row == 4 && column == 4 ? null : 30d);
        ElevationGrid grid = ToGrid(elevations);
        int expectedStructuralCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: expectedStructuralCount), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(expectedStructuralCount, result.RetainedPointCount);
        Assert.Equal(expectedStructuralCount, diagnostics.StructuralPointCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(0, diagnostics.CoverageFloorPointCount);
        Assert.False(diagnostics.StructuralBudgetShortfall);

        (int Row, int Column)[] diagonalNeighbors = [(3, 3), (3, 5), (5, 3), (5, 5)];
        foreach ((int row, int column) in diagonalNeighbors)
        {
            Coordinate2D expectedCenter = grid.GetCellCenter(row, column);
            Assert.Contains(result.RetainedSamples, sample => sample.Position.X == expectedCenter.X && sample.Position.Y == expectedCenter.Y);
        }
    }

    [Fact]
    public async Task AFlatPlaneRetainsOnlyPerimeterStructuralPointsPlusACoverageFloorSpreadAcrossTheInterior()
    {
        ElevationGrid grid = UniformGrid(20, 20, 5d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new(); // default coverage floor fraction
        const int perimeterCount = (2 * 20) + (2 * 20) - 4; // 76

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 40), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(perimeterCount, diagnostics.StructuralPointCount);
        Assert.False(diagnostics.StructuralBudgetShortfall);
        Assert.True(diagnostics.CoverageFloorPointCount > 0);

        HashSet<(bool RowHalf, bool ColumnHalf)> quadrants = [];
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            int row = (int)Math.Round(sample.Position.Y - 0.5d);
            bool isPerimeter = row is 0 or 19 || column is 0 or 19;
            if (!isPerimeter)
            {
                quadrants.Add((row >= 10, column >= 10));
            }
        }

        Assert.True(quadrants.Count >= 2, $"Expected interior retained samples to spread across multiple quadrants; touched {quadrants.Count}.");
    }

    [Fact]
    public async Task CoverageFloorAloneSpreadsRetainedInteriorSamplesAcrossEveryQuadrantWhenCurvatureRankingContributesNothing()
    {
        // Isolates Pass 3 (the coverage floor) from Pass 2 (SolidGround Issue #7 review): the sibling test
        // above uses the default coverageFloorFraction (0.2), under which Pass 2's own (Row asc, Column asc)
        // tie-break already spans multiple quadrants on a perfectly flat plane before Pass 3 contributes
        // anything, so that test's quadrant-count assertion alone does not prove the coverage floor causes any
        // spread. Setting coverageFloorFraction to 1.0 forces curvatureAllotment to exactly 0, so every single
        // interior retained sample here must come from Pass 3's block selection instead.
        ElevationGrid grid = UniformGrid(20, 20, 5d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 1.0d);
        const int perimeterCount = (2 * 20) + (2 * 20) - 4; // 76

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 40), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(perimeterCount, diagnostics.StructuralPointCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.True(diagnostics.CoverageFloorPointCount > 0);

        HashSet<(bool RowHalf, bool ColumnHalf)> quadrants = [];
        HashSet<int> interiorRows = [];
        HashSet<int> interiorColumns = [];
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            int row = (int)Math.Round(sample.Position.Y - 0.5d);
            bool isPerimeter = row is 0 or 19 || column is 0 or 19;
            if (!isPerimeter)
            {
                quadrants.Add((row >= 10, column >= 10));
                interiorRows.Add(row);
                interiorColumns.Add(column);
            }
        }

        Assert.Equal(4, quadrants.Count);
        Assert.True(interiorRows.Count >= 5, $"Expected the coverage floor to spread samples across at least 5 distinct rows; touched {interiorRows.Count}.");
        Assert.True(interiorColumns.Count >= 5, $"Expected the coverage floor to spread samples across at least 5 distinct columns; touched {interiorColumns.Count}.");
    }

    [Fact]
    public async Task CoverageFloorReachesEveryBlockRowEvenWhenTheBudgetIsSmallerThanTheNaturalBlockCount()
    {
        // SolidGround Issue #7 review: visiting coverage-floor blocks in strict (BlockRow asc, BlockColumn
        // asc) order and stopping the instant coverageBudgetFinal picks are made meant a coverageBudgetFinal
        // smaller than the number of blocks spanned by the first BlockRow(s) never reached any later BlockRow
        // at all -- the opposite of this pass's spreading purpose. This 30x30 flat plane with
        // coverageFloorFraction: 1.0 and pointBudget: perimeterCount + 2 forces coverageBudgetFinal to exactly
        // 2 against a natural 2x2 block grid (blockSize = ceil(sqrt(900 / 2)) = 22, giving BlockRow 0 =
        // interior rows 1-21 and BlockRow 1 = interior rows 22-28): the old row-major-with-early-break
        // visitation put both picks in BlockRow 0 and never reached BlockRow 1 at all.
        ElevationGrid grid = UniformGrid(30, 30, 6d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 1.0d);
        const int perimeterCount = (2 * 30) + (2 * 30) - 4; // 116

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 2), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(perimeterCount, diagnostics.StructuralPointCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(2, diagnostics.CoverageFloorPointCount);

        List<int> interiorRows = [];
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            int row = (int)Math.Round(sample.Position.Y - 0.5d);
            bool isPerimeter = row is 0 or 29 || column is 0 or 29;
            if (!isPerimeter)
            {
                interiorRows.Add(row);
            }
        }

        Assert.Equal(2, interiorRows.Count);
        Assert.Contains(interiorRows, row => row <= 21);
        Assert.Contains(interiorRows, row => row >= 22);
    }

    [Fact]
    public async Task ABudgetAtOrAboveTheValidCellCountRetainsEveryValidCellExactlyOnce()
    {
        ElevationGrid grid = UniformGrid(8, 8, 12d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 200), TestContext.Current.CancellationToken);

        Assert.Equal(64, result.RetainedPointCount);
        Assert.Equal(64, result.OriginalPointCount);

        HashSet<(double X, double Y)> expected = [];
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                Coordinate2D center = grid.GetCellCenter(row, column);
                expected.Add((center.X, center.Y));
            }
        }

        HashSet<(double X, double Y)> actual = [.. result.RetainedSamples.Select(sample => (sample.Position.X, sample.Position.Y))];
        Assert.Equal(64, actual.Count);
        Assert.True(expected.SetEquals(actual));
    }

    [Fact]
    public async Task ATightBudgetBelowTheStructuralCandidateCountTruncatesDeterministicallyAndReportsAShortfall()
    {
        double?[,] elevations = FlatGridWithInteriorHoleElevations();
        ElevationGrid grid = ToGrid(elevations);
        int structuralCandidateCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 20), TestContext.Current.CancellationToken);

        Assert.Equal(20, result.RetainedPointCount);
        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(structuralCandidateCount, diagnostics.StructuralCandidateCount);
        Assert.True(diagnostics.StructuralBudgetShortfall);
        Assert.Equal(structuralCandidateCount - 20, diagnostics.StructuralPointsOmittedForBudget);

        // Curvature is exactly 0 everywhere in this flat-plus-hole grid (every present neighbor pair has an
        // equal value, and a missing pair contributes 0 by definition), so the truncation order reduces to
        // (MissingNeighborCount desc, Row asc, Column asc) alone, computed independently here.
        List<(int Row, int Column)> structuralCells = [];
        for (int row = 0; row < 10; row++)
        {
            for (int column = 0; column < 10; column++)
            {
                if (elevations[row, column] is not null && CountMissingNeighborsIndependent(elevations, row, column) > 0)
                {
                    structuralCells.Add((row, column));
                }
            }
        }

        List<(int Row, int Column)> top20 = [.. structuralCells
            .OrderByDescending(cell => CountMissingNeighborsIndependent(elevations, cell.Row, cell.Column))
            .ThenBy(cell => cell.Row)
            .ThenBy(cell => cell.Column)
            .Take(20)
            .OrderBy(cell => cell.Row)
            .ThenBy(cell => cell.Column)];

        List<Coordinate3D> expectedPositions = [.. top20.Select(cell =>
        {
            Coordinate2D center = grid.GetCellCenter(cell.Row, cell.Column);
            return new Coordinate3D(center.X, center.Y, 7d);
        })];
        List<Coordinate3D> actualPositions = [.. result.RetainedSamples.Select(sample => sample.Position)];
        Assert.Equal(expectedPositions, actualPositions);
    }

    [Fact]
    public async Task ATightBudgetExactlyEqualToTheStructuralCandidateCountFitsWithNoShortfall()
    {
        double?[,] elevations = FlatGridWithInteriorHoleElevations();
        ElevationGrid grid = ToGrid(elevations);
        int structuralCandidateCount = CountStructuralCandidates(elevations);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: structuralCandidateCount), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.False(diagnostics.StructuralBudgetShortfall);
        Assert.Equal(0, diagnostics.StructuralPointsOmittedForBudget);
        Assert.Equal(structuralCandidateCount, diagnostics.StructuralPointCount);
        Assert.Equal(structuralCandidateCount, diagnostics.StructuralCandidateCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(0, diagnostics.CoverageFloorPointCount);
    }

    [Fact]
    public async Task OutputNeverExceedsTheRequestedBudgetAcrossASweepOfBudgetValues()
    {
        // Implemented as a Fact with an internal sweep rather than an xUnit Theory: the boundary budget
        // values depend on this grid's own structural candidate count, which is not a compile-time constant.
        ElevationGrid grid = BuildGrid(14, 14, MixedTerrainElevation);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult probe = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        int structuralCount = probe.Diagnostics!.StructuralCandidateCount;
        int totalValidCells = probe.OriginalPointCount;

        int[] budgets =
        [
            1, 5, structuralCount - 1, structuralCount, structuralCount + 1, totalValidCells, totalValidCells + 100,
        ];

        foreach (int budget in budgets)
        {
            SimplificationResult result = await simplifier.SimplifyAsync(
                grid, new SimplificationRequest(pointBudget: budget), TestContext.Current.CancellationToken);
            Assert.True(result.RetainedPointCount <= budget);
            Assert.True(result.RetainedPointCount <= result.OriginalPointCount);

            // SolidGround Issue #24: once the budget reaches or exceeds every valid cell, the retain-all
            // branch (or, below it, the ordinary passes with room to spare) must retain the whole candidate
            // set exactly, not merely "at most the budget".
            if (budget >= totalValidCells)
            {
                Assert.Equal(totalValidCells, result.RetainedPointCount);
            }
        }
    }

    [Fact]
    public async Task ATightSurplusGridWithBudgetAboveCandidateCountRetainsEveryCandidateWithRetainAllDiagnostics()
    {
        ElevationGrid grid = BuildGrid(20, 20, TightSurplusElevation);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult probe = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        int candidateCount = probe.OriginalPointCount;
        int structuralCount = probe.Diagnostics!.StructuralCandidateCount;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: candidateCount + 3), TestContext.Current.CancellationToken);

        Assert.Equal(candidateCount, result.RetainedPointCount);
        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(structuralCount, diagnostics.StructuralPointCount);
        Assert.Equal(candidateCount - structuralCount, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(0, diagnostics.CoverageFloorPointCount);
        Assert.Equal(0, diagnostics.UniformlySampledPointCount);
        Assert.True(diagnostics.InteriorCandidatesExhausted);
        Assert.True(diagnostics.RetainedEveryCandidate);
        Assert.Equal(0d, diagnostics.MaxRemovedCurvatureMagnitude);
        Assert.Equal(0d, diagnostics.MeanRemovedCurvatureMagnitude);
        Assert.Equal(0d, diagnostics.MaxRemovedElevationResidual);
        Assert.Equal(0d, diagnostics.MeanRemovedElevationResidual);

        List<Coordinate3D> expectedPositions = ExpectedRowMajorPositions(grid, TightSurplusElevation, rowCount: 20, columnCount: 20);
        List<Coordinate3D> actualPositions = [.. result.RetainedSamples.Select(sample => sample.Position)];
        Assert.Equal(expectedPositions, actualPositions);
    }

    [Fact]
    public async Task ATightSurplusGridWithBudgetExactlyEqualToCandidateCountRetainsEveryCandidate()
    {
        ElevationGrid grid = BuildGrid(20, 20, TightSurplusElevation);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult probe = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        int candidateCount = probe.OriginalPointCount;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: candidateCount), TestContext.Current.CancellationToken);

        Assert.Equal(candidateCount, result.RetainedPointCount);
        Assert.True(result.Diagnostics!.RetainedEveryCandidate);
        List<Coordinate3D> expectedPositions = ExpectedRowMajorPositions(grid, TightSurplusElevation, rowCount: 20, columnCount: 20);
        List<Coordinate3D> actualPositions = [.. result.RetainedSamples.Select(sample => sample.Position)];
        Assert.Equal(expectedPositions, actualPositions);
    }

    [Fact]
    public async Task ATightSurplusGridWithBudgetOneBelowCandidateCountRunsThePassesAndReportsNotEveryCandidateRetained()
    {
        ElevationGrid grid = BuildGrid(20, 20, TightSurplusElevation);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult probe = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        int candidateCount = probe.OriginalPointCount;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: candidateCount - 1), TestContext.Current.CancellationToken);

        Assert.True(result.RetainedPointCount <= candidateCount - 1);
        Assert.False(result.Diagnostics!.RetainedEveryCandidate);
    }

    [Fact]
    public async Task ARetainAllCaseBypassesThePassesEvenWithAHighCoverageFloorFraction()
    {
        ElevationGrid grid = BuildGrid(20, 20, TightSurplusElevation);
        GridTerrainSimplifier defaultSimplifier = new();
        GridTerrainSimplifier highCoverageFloorSimplifier = new(coverageFloorFraction: 0.9d);

        SimplificationResult probe = await defaultSimplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        SimplificationRequest request = new(pointBudget: probe.OriginalPointCount + 3);

        SimplificationResult defaultResult = await defaultSimplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);
        SimplificationResult highCoverageFloorResult = await highCoverageFloorSimplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);

        // coverageFloorFraction: 0.9 would, if Pass 2/3 ran, push most of the non-structural budget into
        // CoverageFloorPointCount rather than CurvatureSelectedPointCount. The retain-all branch runs before
        // either pass and never reads coverageFloorFraction, so a high-floor simplifier and the default one
        // must produce byte-identical results here.
        Assert.Equal(0, highCoverageFloorResult.Diagnostics!.CoverageFloorPointCount);
        Assert.True(highCoverageFloorResult.Diagnostics.RetainedEveryCandidate);
        Assert.Equal(defaultResult.RetainedSamples, highCoverageFloorResult.RetainedSamples);
        Assert.Equal(defaultResult.Diagnostics, highCoverageFloorResult.Diagnostics);
    }

    [Fact]
    public async Task ATightSurplusGridRetainAllBranchIsDeterministicAcrossRepeatedRuns()
    {
        ElevationGrid grid = BuildGrid(20, 20, TightSurplusElevation);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult probe = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 10_000), TestContext.Current.CancellationToken);
        SimplificationRequest request = new(pointBudget: probe.OriginalPointCount + 3);

        SimplificationResult first = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);
        SimplificationResult second = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);

        Assert.Equal(first.RetainedSamples, second.RetainedSamples);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public async Task SimplifyAsyncIsDeterministicAcrossRepeatedRunsOnTheSameGridInstance()
    {
        ElevationGrid grid = BuildGrid(16, 16, MixedTerrainElevation);
        GridTerrainSimplifier simplifier = new();
        SimplificationRequest request = new(pointBudget: 120);

        SimplificationResult first = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);
        SimplificationResult second = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);

        Assert.Equal(first.RetainedSamples, second.RetainedSamples);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public async Task SimplifyAsyncIsDeterministicOnAFreshlyConstructedValueEqualGrid()
    {
        double?[,] elevationsA = BuildElevations(16, 16, MixedTerrainElevation);
        double?[,] elevationsB = BuildElevations(16, 16, MixedTerrainElevation);
        ElevationGrid gridA = ToGrid(elevationsA);
        ElevationGrid gridB = ToGrid(elevationsB);
        GridTerrainSimplifier simplifier = new();
        SimplificationRequest request = new(pointBudget: 120);

        SimplificationResult resultA = await simplifier.SimplifyAsync(gridA, request, TestContext.Current.CancellationToken);
        SimplificationResult resultB = await simplifier.SimplifyAsync(gridB, request, TestContext.Current.CancellationToken);

        Assert.Equal(resultA.RetainedSamples, resultB.RetainedSamples);
        Assert.Equal(resultA.Diagnostics, resultB.Diagnostics);
    }

    [Fact]
    public async Task TheDefaultCurvatureAwareMethodRetainsMoreOfARidgeLineThanUniformSamplerAtTheSameTightBudget()
    {
        ElevationGrid grid = BuildGrid(15, 15, (row, column) => 10d - (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int budget = 65; // 56 perimeter + 9 interior; verified to give a comfortable ridge-count margin.

        SimplificationResult curvatureAware = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget), TestContext.Current.CancellationToken);
        SimplificationResult uniform = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget, method: SimplificationMethod.UniformSampler), TestContext.Current.CancellationToken);

        int curvatureAwareNearRidge = CountNearRidgeColumn(curvatureAware.RetainedSamples, ridgeColumn: 7);
        int uniformNearRidge = CountNearRidgeColumn(uniform.RetainedSamples, ridgeColumn: 7);

        Assert.True(
            curvatureAwareNearRidge > uniformNearRidge,
            $"Expected CurvatureAware ({curvatureAwareNearRidge}) to retain more near-ridge samples than UniformSampler ({uniformNearRidge}).");
    }

    [Fact]
    public async Task CurvatureAwareAchievesALowerMeanRemovedElevationResidualThanUniformSamplerAtTheSameBudget()
    {
        ElevationGrid grid = BuildGrid(15, 15, (row, column) => 10d - (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int budget = 65;

        SimplificationResult curvatureAware = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget), TestContext.Current.CancellationToken);
        SimplificationResult uniform = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget, method: SimplificationMethod.UniformSampler), TestContext.Current.CancellationToken);

        Assert.True(
            curvatureAware.Diagnostics!.MeanRemovedElevationResidual < uniform.Diagnostics!.MeanRemovedElevationResidual,
            $"Expected CurvatureAware's mean removed residual ({curvatureAware.Diagnostics.MeanRemovedElevationResidual}) to be lower than " +
            $"UniformSampler's ({uniform.Diagnostics.MeanRemovedElevationResidual}).");
    }

    [Fact]
    public async Task UniformSamplerNeverPopulatesStructuralOrCurvatureSelectedDiagnosticsAndIsNotTheDefault()
    {
        Assert.Equal(SimplificationMethod.CurvatureAware, new SimplificationRequest().Method);

        ElevationGrid grid = BuildGrid(15, 15, (row, column) => 10d - (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new();
        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 50, method: SimplificationMethod.UniformSampler), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        Assert.Equal(0, diagnostics.StructuralCandidateCount);
        Assert.Equal(0, diagnostics.StructuralPointCount);
        Assert.Equal(0, diagnostics.CurvatureSelectedPointCount);
        Assert.Equal(0, diagnostics.CoverageFloorPointCount);
        Assert.Equal(result.RetainedPointCount, diagnostics.UniformlySampledPointCount);
        Assert.False(diagnostics.StructuralBudgetShortfall);
    }

    [Fact]
    public async Task UniformSamplerSelectionIsDeterministicAcrossRepeatedRuns()
    {
        ElevationGrid grid = BuildGrid(15, 15, (row, column) => 10d - (0.2d * Math.Abs(column - 7)));
        GridTerrainSimplifier simplifier = new();
        SimplificationRequest request = new(pointBudget: 50, method: SimplificationMethod.UniformSampler);

        SimplificationResult first = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);
        SimplificationResult second = await simplifier.SimplifyAsync(grid, request, TestContext.Current.CancellationToken);

        Assert.Equal(first.RetainedSamples, second.RetainedSamples);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(7)]
    [InlineData(5)]
    [InlineData(1)]
    public async Task UniformSamplerBucketSamplingSelectsExactlyTheRequestedBudgetWithoutUndershootNearParity(int budget)
    {
        const int n = 10;
        ElevationGrid grid = BuildGrid(1, n, (_, column) => (double)column, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget, method: SimplificationMethod.UniformSampler), TestContext.Current.CancellationToken);

        Assert.Equal(budget, result.RetainedPointCount);

        List<int> expectedIndices = [.. Enumerable.Range(0, budget).Select(i => (i * n) / budget)];
        Assert.Equal(expectedIndices.Count, expectedIndices.Distinct().Count());
        for (int i = 1; i < expectedIndices.Count; i++)
        {
            Assert.True(expectedIndices[i] > expectedIndices[i - 1]);
        }

        List<int> actualColumns = [.. result.RetainedSamples.Select(sample => (int)Math.Round(sample.Position.X - 0.5d))];
        Assert.Equal(expectedIndices, actualColumns);
    }

    [Fact]
    public async Task RequestingTinErrorThrowsATerrainSimplificationExceptionNamingBothWorkingAlternatives()
    {
        ElevationGrid grid = UniformGrid(5, 5, 1d);
        GridTerrainSimplifier simplifier = new();

        TerrainSimplificationException exception = await Assert.ThrowsAsync<TerrainSimplificationException>(
            () => simplifier.SimplifyAsync(grid, new SimplificationRequest(method: SimplificationMethod.TinError), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(nameof(SimplificationMethod.CurvatureAware), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SimplificationMethod.UniformSampler), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimplifyAsyncRejectsATerrainSampleSetInput()
    {
        TerrainSampleSet sampleSet = new(
            ProjectedReference(), VerticalReference(), [new TerrainSample(new Coordinate3D(0d, 0d, 1d))]);
        GridTerrainSimplifier simplifier = new();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => simplifier.SimplifyAsync(sampleSet, new SimplificationRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("terrain", exception.ParamName);
        Assert.Contains(nameof(TerrainSampleSet), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimplifyAsyncRejectsNullTerrainOrNullRequest()
    {
        ElevationGrid grid = UniformGrid(3, 3, 1d);
        GridTerrainSimplifier simplifier = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => simplifier.SimplifyAsync(null!, new SimplificationRequest(), TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => simplifier.SimplifyAsync(grid, null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SimplifyAsyncHonorsAnAlreadyCancelledToken()
    {
        ElevationGrid grid = UniformGrid(3, 3, 1d);
        GridTerrainSimplifier simplifier = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => InvokeWithCancellationToken(simplifier, grid, new SimplificationRequest(), cts.Token));
    }

    // A plain (non-test) helper: this test must deliberately control and cancel its own token rather than
    // using TestContext.Current.CancellationToken, unlike every other call in this file, so the actual
    // SimplifyAsync invocation is routed through an ordinary method xUnit1051 does not inspect.
    private static Task<SimplificationResult> InvokeWithCancellationToken(
        GridTerrainSimplifier simplifier, ElevationGrid grid, SimplificationRequest request, CancellationToken cancellationToken) =>
        simplifier.SimplifyAsync(grid, request, cancellationToken).AsTask();

    [Fact]
    public async Task ARealExtremeElevationValueIsRetainedAndNeverMistakenForTheAaiGridNoDataSentinel()
    {
        double?[,] elevations = BuildElevations(7, 7, (row, column) =>
            row == 0 && column == 3 ? -9999d :
            row == 3 && column == 3 ? null :
            50d);
        ElevationGrid grid = ToGrid(elevations);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        Coordinate2D sentinelCenter = grid.GetCellCenter(0, 3);
        Assert.Contains(
            result.RetainedSamples,
            sample => sample.Position.X == sentinelCenter.X && sample.Position.Y == sentinelCenter.Y && sample.Position.Elevation == -9999d);
        Assert.Equal(-9999d, result.Diagnostics!.MinElevation);

        Coordinate2D holeCenter = grid.GetCellCenter(3, 3);
        Assert.DoesNotContain(result.RetainedSamples, sample => sample.Position.X == holeCenter.X && sample.Position.Y == holeCenter.Y);
    }

    [Fact]
    public async Task AllNoDataGridProducesAnEmptyResultWithoutThrowing()
    {
        ElevationGrid grid = BuildGrid(5, 5, (_, _) => (double?)null);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(grid, new SimplificationRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.OriginalPointCount);
        Assert.Equal(0, result.RetainedPointCount);
        Assert.Null(result.Diagnostics!.MinElevation);
        Assert.Null(result.Diagnostics.MaxElevation);
        Assert.Equal(25, result.Diagnostics.NoDataOrExcludedCellCount);
    }

    [Theory]
    [InlineData(SimplificationMethod.CurvatureAware)]
    [InlineData(SimplificationMethod.UniformSampler)]
    public async Task DiagnosticsSelectionCountsSumToTheRetainedPointCountForBothMethods(SimplificationMethod method)
    {
        ElevationGrid grid = BuildGrid(14, 14, MixedTerrainElevation);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 90, method: method), TestContext.Current.CancellationToken);

        SimplificationDiagnostics diagnostics = result.Diagnostics!;
        int sum = diagnostics.StructuralPointCount + diagnostics.CurvatureSelectedPointCount
            + diagnostics.CoverageFloorPointCount + diagnostics.UniformlySampledPointCount;
        Assert.Equal(result.RetainedPointCount, sum);
    }

    [Fact]
    public async Task DiagnosticsReportsTheFullSourceElevationRangeEvenWhenATightBudgetExcludesTheExtremeCells()
    {
        double?[,] elevations = BuildElevations(12, 12, (row, column) =>
        {
            bool plateauA = row is >= 1 and <= 3 && column is >= 1 and <= 3;
            bool plateauB = row is >= 8 and <= 10 && column is >= 8 and <= 10;
            if (plateauA)
            {
                return 500d;
            }
            if (plateauB)
            {
                return -500d;
            }
            if (row == 5 && column == 5)
            {
                return 250d; // an isolated spike: high local curvature, but not the global extreme.
            }
            return 50d;
        });
        ElevationGrid grid = ToGrid(elevations);
        GridTerrainSimplifier simplifier = new(coverageFloorFraction: 0d);
        const int perimeterCount = (2 * 12) + (2 * 12) - 4;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 1), TestContext.Current.CancellationToken);

        Assert.Equal(500d, result.Diagnostics!.MaxElevation);
        Assert.Equal(-500d, result.Diagnostics.MinElevation);

        Coordinate2D maxCenter = grid.GetCellCenter(2, 2);
        Coordinate2D minCenter = grid.GetCellCenter(9, 9);
        Assert.DoesNotContain(result.RetainedSamples, sample => sample.Position.X == maxCenter.X && sample.Position.Y == maxCenter.Y);
        Assert.DoesNotContain(result.RetainedSamples, sample => sample.Position.X == minCenter.X && sample.Position.Y == minCenter.Y);
    }

    [Fact]
    public async Task DiagnosticsElevationBoundsAreNullOnlyWhenCandidatePointCountIsZero()
    {
        GridTerrainSimplifier simplifier = new();

        ElevationGrid allNoData = BuildGrid(3, 3, (_, _) => (double?)null);
        SimplificationResult emptyResult = await simplifier.SimplifyAsync(allNoData, new SimplificationRequest(), TestContext.Current.CancellationToken);
        Assert.Null(emptyResult.Diagnostics!.MinElevation);
        Assert.Null(emptyResult.Diagnostics.MaxElevation);

        ElevationGrid populated = UniformGrid(3, 3, 4d);
        SimplificationResult populatedResult = await simplifier.SimplifyAsync(populated, new SimplificationRequest(), TestContext.Current.CancellationToken);
        Assert.NotNull(populatedResult.Diagnostics!.MinElevation);
        Assert.NotNull(populatedResult.Diagnostics.MaxElevation);
        Assert.True(populatedResult.Diagnostics.MinElevation <= populatedResult.Diagnostics.MaxElevation);
    }

    [Fact]
    public async Task DiagnosticsNoDataOrExcludedCellCountMatchesTheActualNullCellCount()
    {
        (int Row, int Column)[] nullCells = [(0, 0), (1, 2), (2, 4), (4, 4), (5, 1), (6, 6), (8, 3)];
        HashSet<(int Row, int Column)> nullSet = [.. nullCells];
        ElevationGrid grid = BuildGrid(9, 9, (row, column) => nullSet.Contains((row, column)) ? null : 8d);
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(grid, new SimplificationRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(7, result.Diagnostics!.NoDataOrExcludedCellCount);
        Assert.Equal(74, result.Diagnostics.CandidatePointCount);
    }

    [Fact]
    public async Task DiagnosticsInteriorCandidatesExhaustedReflectsWhetherBudgetOrCandidateSupplyWasTheLimit()
    {
        ElevationGrid grid = UniformGrid(6, 6, 9d, rowOrder: GridRowOrder.SouthToNorth);
        GridTerrainSimplifier simplifier = new();
        const int perimeterCount = (2 * 6) + (2 * 6) - 4; // 20

        // pointBudget = perimeterCount + 25 = 45 exceeds candidates.Count = 36 (a 6x6 grid with no NODATA), so
        // this sub-case is intercepted by the Issue #24 retain-all branch (candidates.Count <= pointBudget)
        // before Pass 2 or Pass 3 ever runs; the retain-all branch hardcodes InteriorCandidatesExhausted to
        // true, which is what this assertion actually observes, not Pass 2 exhausting the 16 true interior
        // candidates on its own.
        SimplificationResult comfortable = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 25), TestContext.Current.CancellationToken);
        Assert.True(comfortable.Diagnostics!.InteriorCandidatesExhausted);

        SimplificationResult constrained = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: perimeterCount + 5), TestContext.Current.CancellationToken);
        Assert.False(constrained.Diagnostics!.InteriorCandidatesExhausted);
    }

    [Fact]
    public async Task DiagnosticsElevationUnitReflectsTheGridsActualVerticalReferenceUnitNotAHardcodedDefault()
    {
        // SolidGround Issue #7 review: every other test in this file builds grids through the shared
        // VerticalReference() helper (LengthUnit.Meter), so a hardcoded `elevationUnit: LengthUnit.Meter` (or
        // a read of the wrong reference) inside GridTerrainSimplifier.BuildDiagnostics would pass every other
        // test in this file unnoticed. This grid's vertical reference unit is deliberately non-default.
        ElevationGrid grid = UniformGrid(
            4, 4, 12d, verticalReference: new VerticalReference("NAVD88", LengthUnit.UsSurveyFoot, "Geoid12B"));
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(LengthUnit.UsSurveyFoot, result.Diagnostics!.ElevationUnit);
    }

    [Fact]
    public async Task DiagnosticsMaxAndMeanRemovedElevationResidualMatchAnIndependentlyComputedValueOverTheActualRemovedSet()
    {
        // SolidGround Issue #7 review: MaxRemovedElevationResidual/MeanRemovedElevationResidual were
        // previously only ever echoed back by the SimplificationDiagnostics constructor-validation tests,
        // never checked against a value GridTerrainSimplifier itself computed end-to-end from a real
        // SimplifyAsync run. This recomputes both statistics independently (ComputeElevationResidualIndependent
        // reimplements the elevation-residual formula from the "Curvature and elevation-residual scoring" section
        // of docs/architecture/terrain-aware-decimation.md) over the actual removed set -- every candidate cell that
        // is not among the retained samples -- identified via the grid's own GetCellCenter mapping.
        double?[,] elevations = BuildElevations(14, 14, MixedTerrainElevation);
        ElevationGrid grid = ToGrid(elevations);
        GridTerrainSimplifier simplifier = new();
        const int budget = 90;

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: budget), TestContext.Current.CancellationToken);

        Dictionary<(double X, double Y), (int Row, int Column)> positionsByCenter = new();
        for (int row = 0; row < 14; row++)
        {
            for (int column = 0; column < 14; column++)
            {
                Coordinate2D center = grid.GetCellCenter(row, column);
                positionsByCenter[(center.X, center.Y)] = (row, column);
            }
        }

        HashSet<(int Row, int Column)> retainedPositions = [.. result.RetainedSamples.Select(
            sample => positionsByCenter[(sample.Position.X, sample.Position.Y)])];

        double expectedMax = 0d;
        double residualSum = 0d;
        int removedCount = 0;
        for (int row = 0; row < 14; row++)
        {
            for (int column = 0; column < 14; column++)
            {
                if (elevations[row, column] is null || retainedPositions.Contains((row, column)))
                {
                    continue;
                }

                double residual = ComputeElevationResidualIndependent(elevations, row, column);
                expectedMax = Math.Max(expectedMax, residual);
                residualSum += residual;
                removedCount++;
            }
        }

        Assert.True(removedCount > 0, "This test only proves what it claims when the budget actually removes some candidates.");
        Assert.Equal(expectedMax, result.Diagnostics!.MaxRemovedElevationResidual);
        Assert.Equal(residualSum / removedCount, result.Diagnostics.MeanRemovedElevationResidual);
    }

    [Theory]
    [InlineData(-0.1, true)]
    [InlineData(1.1, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(double.NegativeInfinity, true)]
    [InlineData(0.0, false)]
    [InlineData(0.5, false)]
    [InlineData(1.0, false)]
    public void GridTerrainSimplifierConstructorRejectsAnOutOfRangeOrNonFiniteCoverageFloorFraction(double coverageFloorFraction, bool expectThrow)
    {
        if (expectThrow)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridTerrainSimplifier(coverageFloorFraction));
        }
        else
        {
            Exception? exception = Record.Exception(() => new GridTerrainSimplifier(coverageFloorFraction));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void SimplificationResultRejectsDiagnosticsWhoseSelectionCountsDoNotMatchTheRetainedSampleCount()
    {
        SimplificationDiagnostics diagnostics = new(
            candidatePointCount: 3, noDataOrExcludedCellCount: 0, structuralCandidateCount: 2, structuralPointCount: 2,
            curvatureSelectedPointCount: 0, coverageFloorPointCount: 0, uniformlySampledPointCount: 0,
            interiorCandidatesExhausted: true, minElevation: 0d, maxElevation: 1d, elevationUnit: LengthUnit.Meter,
            maxRemovedCurvatureMagnitude: 0d, meanRemovedCurvatureMagnitude: 0d, maxRemovedElevationResidual: 0d, meanRemovedElevationResidual: 0d);
        TerrainSample[] retained = [new(new Coordinate3D(1d, 2d, 3d))];
        SimplificationRequest request = new(pointBudget: 10);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new SimplificationResult(3, retained, request, diagnostics));
        Assert.Equal("diagnostics", exception.ParamName);
    }

    [Fact]
    public void SimplificationResultRejectsDiagnosticsWhoseCandidatePointCountDisagreesWithTheOriginalPointCount()
    {
        SimplificationDiagnostics diagnostics = new(
            candidatePointCount: 5, noDataOrExcludedCellCount: 0, structuralCandidateCount: 0, structuralPointCount: 0,
            curvatureSelectedPointCount: 0, coverageFloorPointCount: 0, uniformlySampledPointCount: 0,
            interiorCandidatesExhausted: true, minElevation: 0d, maxElevation: 10d, elevationUnit: LengthUnit.Meter,
            maxRemovedCurvatureMagnitude: 0d, meanRemovedCurvatureMagnitude: 0d, maxRemovedElevationResidual: 0d, meanRemovedElevationResidual: 0d);
        SimplificationRequest request = new(pointBudget: 10);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new SimplificationResult(6, [], request, diagnostics));
        Assert.Equal("diagnostics", exception.ParamName);
    }

    [Fact]
    public void SimplificationResultWithoutDiagnosticsStillConstructsAndDiagnosticsIsNull()
    {
        TerrainSample[] retained = [new(new Coordinate3D(1d, 2d, 3d))];
        SimplificationRequest request = new(1, SimplificationMethod.CurvatureAware);

        SimplificationResult resultWithoutDiagnosticsArgument = new(3, retained, request);
        Assert.Null(resultWithoutDiagnosticsArgument.Diagnostics);
        Assert.Equal(2, resultWithoutDiagnosticsArgument.RemovedPointCount);

        SimplificationResult resultWithExplicitNull = new(3, retained, request, diagnostics: null);
        Assert.Null(resultWithExplicitNull.Diagnostics);
    }

    [Fact]
    public async Task EachRetainedSampleCoordinateMatchesTheGridsOwnGetCellCenterCalculation()
    {
        ElevationGrid grid = BuildGrid(
            6, 6, (row, column) => (row * 1000d) + column,
            cellSizeX: 2.5d, cellSizeY: 1.5d, rowOrder: GridRowOrder.SouthToNorth,
            anchor: new Coordinate2D(1000d, 2000d));
        GridTerrainSimplifier simplifier = new();

        SimplificationResult result = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 100), TestContext.Current.CancellationToken);

        Assert.Equal(36, result.RetainedPointCount);
        foreach (TerrainSample sample in result.RetainedSamples)
        {
            int row = (int)Math.Floor(sample.Position.Elevation / 1000d);
            int column = (int)Math.Round(sample.Position.Elevation - (row * 1000d));
            Coordinate2D expectedCenter = grid.GetCellCenter(row, column);
            Assert.Equal(expectedCenter.X, sample.Position.X);
            Assert.Equal(expectedCenter.Y, sample.Position.Y);
            Assert.Equal(grid.GetElevation(row, column), (double?)sample.Position.Elevation);
        }
    }

    [Fact]
    public void SimplificationDiagnosticsConstructorRejectsInvalidCountsBoundsUnitAndErrorStatistics()
    {
        // Implemented as a Fact rather than an xUnit Theory: each invalid combination overrides a different
        // subset of the 15 constructor parameters, which does not fit InlineData's flat-argument-list shape.
        Assert.Null(Record.Exception(() => ValidDiagnostics()));

        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(candidatePointCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(noDataOrExcludedCellCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(structuralCandidateCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(structuralPointCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(curvatureSelectedPointCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(coverageFloorPointCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(uniformlySampledPointCount: -1));
        Assert.Throws<ArgumentException>(() => ValidDiagnostics(structuralCandidateCount: 2, structuralPointCount: 3));
        Assert.Throws<ArgumentException>(() => ValidDiagnostics(minElevation: null, maxElevation: 10d));
        Assert.Throws<ArgumentException>(() => ValidDiagnostics(minElevation: 0d, maxElevation: null));
        Assert.Throws<ArgumentException>(() => ValidDiagnostics(minElevation: null, maxElevation: null));
        Assert.Throws<ArgumentException>(() => ValidDiagnostics(minElevation: 20d, maxElevation: 10d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(minElevation: double.NaN, maxElevation: 10d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(minElevation: 0d, maxElevation: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(elevationUnit: (LengthUnit)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(maxRemovedCurvatureMagnitude: -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(meanRemovedCurvatureMagnitude: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(maxRemovedElevationResidual: double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValidDiagnostics(meanRemovedElevationResidual: -1d));
    }

    [Fact]
    public void SimplificationDiagnosticsConstructorAllowsAMeanStatisticSlightlyAboveItsMaximumFromFloatingPointSummation()
    {
        // Orchestrator addendum for SolidGround Issue #7: unlike minElevation/maxElevation, the four removed
        // curvature/residual statistics are independently accumulated floating-point sums, so a mean can round
        // a single ulp above a max computed from the same equal inputs (0.1 + 0.1 + 0.1, divided by 3, rounds
        // to slightly more than 0.1). This must construct successfully, not throw.
        double max = 0.1d;
        double mean = (0.1d + 0.1d + 0.1d) / 3d;
        Assert.True(mean > max, "This test only proves what it claims when the example actually rounds above the maximum.");

        Assert.Null(Record.Exception(() => ValidDiagnostics(maxRemovedCurvatureMagnitude: max, meanRemovedCurvatureMagnitude: mean)));
        Assert.Null(Record.Exception(() => ValidDiagnostics(maxRemovedElevationResidual: max, meanRemovedElevationResidual: mean)));
    }

    private static int CountNearRidgeColumn(IReadOnlyList<TerrainSample> samples, int ridgeColumn)
    {
        int count = 0;
        foreach (TerrainSample sample in samples)
        {
            int column = (int)Math.Round(sample.Position.X - 0.5d);
            if (Math.Abs(column - ridgeColumn) <= 1)
            {
                count++;
            }
        }
        return count;
    }

    private static double?[,] FlatGridWithInteriorHoleElevations() =>
        BuildElevations(10, 10, (row, column) => row is >= 4 and <= 5 && column is >= 4 and <= 5 ? null : 7d);

    private static double? MixedTerrainElevation(int row, int column)
    {
        if (row % 7 == 3 && column % 5 == 4)
        {
            return null; // scattered, deterministic NODATA holes.
        }

        double ridge = 12d - (0.4d * Math.Abs(column - 4));
        double swale = -0.3d * Math.Abs(row - 9);
        return ridge + swale;
    }

    /// <summary>A 20x20 grid with a few deterministic, interior-only NODATA holes (SolidGround Issue #24's
    /// retain-all branch tests): "interior" here means the four holes never touch row/column 0 or 19, so every
    /// structural point they create comes from the holes themselves, not the grid boundary.</summary>
    private static double? TightSurplusElevation(int row, int column)
    {
        if ((row, column) is (5, 5) or (10, 12) or (14, 7) or (8, 15))
        {
            return null; // deterministic interior NODATA holes, away from the grid edge.
        }

        double ridge = 12d - (0.3d * Math.Abs(column - 10));
        double swale = -0.2d * Math.Abs(row - 10);
        return ridge + swale;
    }

    /// <summary>Independently reimplements the Pass 0 row-major candidate scan's ordering (skipping NODATA) so
    /// a retain-all test can assert the exact retained sample ordering without depending on any of
    /// <see cref="GridTerrainSimplifier"/>'s own internal sorts.</summary>
    private static List<Coordinate3D> ExpectedRowMajorPositions(ElevationGrid grid, Func<int, int, double?> valueAt, int rowCount, int columnCount)
    {
        List<Coordinate3D> positions = [];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                double? elevation = valueAt(row, column);
                if (elevation is null)
                {
                    continue;
                }

                Coordinate2D center = grid.GetCellCenter(row, column);
                positions.Add(new Coordinate3D(center.X, center.Y, elevation.Value));
            }
        }
        return positions;
    }

    private static SimplificationDiagnostics ValidDiagnostics(
        int candidatePointCount = 10,
        int noDataOrExcludedCellCount = 2,
        int structuralCandidateCount = 4,
        int structuralPointCount = 4,
        int curvatureSelectedPointCount = 3,
        int coverageFloorPointCount = 3,
        int uniformlySampledPointCount = 0,
        bool interiorCandidatesExhausted = true,
        double? minElevation = 0d,
        double? maxElevation = 10d,
        LengthUnit elevationUnit = LengthUnit.Meter,
        double maxRemovedCurvatureMagnitude = 2d,
        double meanRemovedCurvatureMagnitude = 1d,
        double maxRemovedElevationResidual = 2d,
        double meanRemovedElevationResidual = 1d) =>
        new(
            candidatePointCount, noDataOrExcludedCellCount, structuralCandidateCount, structuralPointCount,
            curvatureSelectedPointCount, coverageFloorPointCount, uniformlySampledPointCount, interiorCandidatesExhausted,
            minElevation, maxElevation, elevationUnit, maxRemovedCurvatureMagnitude, meanRemovedCurvatureMagnitude,
            maxRemovedElevationResidual, meanRemovedElevationResidual);

    /// <summary>Independently reimplements the 8-neighbor missing-neighbor count for test verification only.</summary>
    private static int CountMissingNeighborsIndependent(double?[,] elevations, int row, int column)
    {
        int rowCount = elevations.GetLength(0);
        int columnCount = elevations.GetLength(1);
        int missing = 0;
        for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (int columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                if (rowOffset == 0 && columnOffset == 0)
                {
                    continue;
                }

                int neighborRow = row + rowOffset;
                int neighborColumn = column + columnOffset;
                if (neighborRow < 0 || neighborRow >= rowCount || neighborColumn < 0 || neighborColumn >= columnCount
                    || elevations[neighborRow, neighborColumn] is null)
                {
                    missing++;
                }
            }
        }
        return missing;
    }

    /// <summary>Independently reimplements structural-candidate counting, per the "Structural classification: one
    /// rule for grid edges, NODATA holes, and parcel boundaries" section of
    /// docs/architecture/terrain-aware-decimation.md, for test verification only.</summary>
    private static int CountStructuralCandidates(double?[,] elevations)
    {
        int rowCount = elevations.GetLength(0);
        int columnCount = elevations.GetLength(1);
        int count = 0;
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                if (elevations[row, column] is not null && CountMissingNeighborsIndependent(elevations, row, column) > 0)
                {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>Independently reimplements the elevation-residual formula from the "Curvature and
    /// elevation-residual scoring" section of docs/architecture/terrain-aware-decimation.md, for test
    /// verification only.</summary>
    private static double ComputeElevationResidualIndependent(double?[,] elevations, int row, int column)
    {
        int rowCount = elevations.GetLength(0);
        int columnCount = elevations.GetLength(1);
        double elevation = elevations[row, column]!.Value;

        double? west = column - 1 >= 0 ? elevations[row, column - 1] : null;
        double? east = column + 1 < columnCount ? elevations[row, column + 1] : null;
        double? north = row - 1 >= 0 ? elevations[row - 1, column] : null;
        double? south = row + 1 < rowCount ? elevations[row + 1, column] : null;

        double sum = 0d;
        int count = 0;
        if (west is double westValue) { sum += westValue; count++; }
        if (east is double eastValue) { sum += eastValue; count++; }
        if (north is double northValue) { sum += northValue; count++; }
        if (south is double southValue) { sum += southValue; count++; }

        return count > 0 ? Math.Abs(elevation - (sum / count)) : 0d;
    }

    private static double?[,] BuildElevations(int rowCount, int columnCount, Func<int, int, double?> valueAt)
    {
        double?[,] values = new double?[rowCount, columnCount];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                values[row, column] = valueAt(row, column);
            }
        }
        return values;
    }

    private static ElevationGrid ToGrid(
        double?[,] elevations,
        double cellSizeX = 1d,
        double cellSizeY = 1d,
        GridRowOrder rowOrder = GridRowOrder.NorthToSouth,
        GridAnchorConvention anchorConvention = GridAnchorConvention.LowerLeftCorner,
        Coordinate2D? anchor = null,
        HorizontalReference? reference = null,
        VerticalReference? verticalReference = null) =>
        new(
            reference ?? ProjectedReference(), verticalReference ?? VerticalReference(), anchor ?? new Coordinate2D(0d, 0d),
            cellSizeX, cellSizeY, anchorConvention, rowOrder, elevations);

    private static ElevationGrid BuildGrid(
        int rowCount,
        int columnCount,
        Func<int, int, double?> valueAt,
        double cellSizeX = 1d,
        double cellSizeY = 1d,
        GridRowOrder rowOrder = GridRowOrder.NorthToSouth,
        GridAnchorConvention anchorConvention = GridAnchorConvention.LowerLeftCorner,
        Coordinate2D? anchor = null,
        HorizontalReference? reference = null,
        VerticalReference? verticalReference = null) =>
        ToGrid(BuildElevations(rowCount, columnCount, valueAt), cellSizeX, cellSizeY, rowOrder, anchorConvention, anchor, reference, verticalReference);

    private static ElevationGrid UniformGrid(
        int rowCount,
        int columnCount,
        double elevation,
        double cellSizeX = 1d,
        double cellSizeY = 1d,
        GridRowOrder rowOrder = GridRowOrder.NorthToSouth,
        GridAnchorConvention anchorConvention = GridAnchorConvention.LowerLeftCorner,
        Coordinate2D? anchor = null,
        HorizontalReference? reference = null,
        VerticalReference? verticalReference = null) =>
        BuildGrid(rowCount, columnCount, (_, _) => elevation, cellSizeX, cellSizeY, rowOrder, anchorConvention, anchor, reference, verticalReference);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
