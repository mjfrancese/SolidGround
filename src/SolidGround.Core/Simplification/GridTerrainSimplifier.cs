using SolidGround.Core.Geometry;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Simplification;

/// <summary>A simplifier could not produce a result for the requested terrain and options.</summary>
public sealed class TerrainSimplificationException : InvalidOperationException
{
    public TerrainSimplificationException()
    {
    }

    public TerrainSimplificationException(string message)
        : base(message)
    {
    }

    public TerrainSimplificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The first concrete <see cref="ITerrainSimplifier"/>. Accepts only <see cref="ElevationGrid"/> because
/// structural classification and curvature scoring need raster adjacency. Implements
/// <see cref="SimplificationMethod.CurvatureAware"/> (terrain-aware, the request default) and
/// <see cref="SimplificationMethod.UniformSampler"/> (a labeled comparison baseline that never protects
/// structure). Rejects <see cref="SimplificationMethod.TinError"/>. Stateless aside from its configured
/// coverage-floor fraction; safe to reuse concurrently across calls.
/// </summary>
public sealed class GridTerrainSimplifier : ITerrainSimplifier
{
    /// <summary>Fraction of budget remaining after structural retention reserved for spatial coverage rather than pure curvature ranking.</summary>
    public const double DefaultCoverageFloorFraction = 0.2d;

    private readonly double coverageFloorFraction;

    public GridTerrainSimplifier(double coverageFloorFraction = DefaultCoverageFloorFraction)
    {
        if (!double.IsFinite(coverageFloorFraction) || coverageFloorFraction < 0d || coverageFloorFraction > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(coverageFloorFraction), coverageFloorFraction,
                "Coverage floor fraction must be finite and within [0, 1].");
        }
        this.coverageFloorFraction = coverageFloorFraction;
    }

    public ValueTask<SimplificationResult> SimplifyAsync(
        ElevationData terrain, SimplificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(request);
        if (terrain is not ElevationGrid grid)
        {
            throw new ArgumentException(
                $"{nameof(GridTerrainSimplifier)} requires an {nameof(ElevationGrid)} because structural and " +
                $"curvature classification need raster adjacency; received {terrain.GetType().Name}.",
                nameof(terrain));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return request.Method switch
        {
            SimplificationMethod.TinError => throw new TerrainSimplificationException(
                $"{nameof(SimplificationMethod.TinError)} is not implemented by {nameof(GridTerrainSimplifier)}. " +
                $"Use {nameof(SimplificationMethod.CurvatureAware)} (the default, terrain-aware method) or " +
                $"{nameof(SimplificationMethod.UniformSampler)} (a labeled comparison baseline) instead."),
            SimplificationMethod.UniformSampler => new ValueTask<SimplificationResult>(SimplifyUniform(grid, request)),
            _ => new ValueTask<SimplificationResult>(SimplifyCurvatureAware(grid, request, cancellationToken)),
        };
    }

    /// <summary>One valid (non-null) source cell and every metric computed for it during Pass 0.</summary>
    private readonly record struct Candidate(int Row, int Column, double Elevation, int MissingNeighborCount, double CurvatureMagnitude, double ElevationResidual);

    private SimplificationResult SimplifyCurvatureAware(ElevationGrid grid, SimplificationRequest request, CancellationToken cancellationToken)
    {
        (List<Candidate> candidates, int noDataOrExcludedCellCount, double? minElevation, double? maxElevation) =
            BuildCandidates(grid, classifyStructural: true);

        int pointBudget = request.PointBudget;
        List<Candidate> structural = [];
        List<Candidate> nonStructural = [];
        foreach (Candidate candidate in candidates)
        {
            (candidate.MissingNeighborCount > 0 ? structural : nonStructural).Add(candidate);
        }

        List<Candidate> retainedStructural;
        int remainingBudget;
        if (structural.Count <= pointBudget)
        {
            retainedStructural = structural;
            remainingBudget = pointBudget - structural.Count;
        }
        else
        {
            retainedStructural = structural
                .OrderByDescending(candidate => candidate.MissingNeighborCount)
                .ThenByDescending(candidate => candidate.CurvatureMagnitude)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .Take(pointBudget)
                .ToList();
            remainingBudget = 0;
        }

        // Checked once here, between the structural (Pass 1) and curvature-ranked interior-fill (Pass 2)
        // passes: Pass 0 (candidate enumeration) and Pass 1 (structural retention) are both cheap, bounded
        // scans/sorts, so this is the one place a caller's cancellation is observed before the remaining,
        // potentially larger interior sort runs. See the "Error taxonomy" section of
        // docs/architecture/terrain-aware-decimation.md for the resulting OperationCanceledException contract.
        cancellationToken.ThrowIfCancellationRequested();

        // Retain-all branch (SolidGround Issue #24): when every candidate already fits the budget, retain the
        // full row-major candidate list directly instead of routing it through Pass 2/3's block-quota
        // bookkeeping. That bookkeeping exists to choose WHICH candidates to drop under a binding budget;
        // asked to keep everything, it can still drop a handful of non-structural candidates even though the
        // budget had room, because Pass 3 visits each spatial block at most once per call and a
        // remainingForCoverage candidate sharing a block with an already-picked one is never revisited. See
        // the "Three-pass budget allocation" section of docs/architecture/terrain-aware-decimation.md.
        if (candidates.Count <= pointBudget)
        {
            SimplificationDiagnostics retainAllDiagnostics = BuildDiagnostics(
                grid, candidates, candidates, noDataOrExcludedCellCount, minElevation, maxElevation,
                structuralCandidateCount: structural.Count,
                structuralPointCount: structural.Count,
                curvatureSelectedPointCount: nonStructural.Count,
                coverageFloorPointCount: 0,
                uniformlySampledPointCount: 0,
                interiorCandidatesExhausted: true);

            List<TerrainSample> retainAllSamples = [.. candidates
                .OrderBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .Select(candidate => ToSample(grid, candidate))];
            return new SimplificationResult(candidates.Count, retainAllSamples, request, retainAllDiagnostics);
        }

        List<Candidate> curvatureSelected = [];
        List<Candidate> coverageSelected = [];

        if (remainingBudget > 0 && nonStructural.Count > 0)
        {
            int coverageReserve = (int)Math.Floor(remainingBudget * coverageFloorFraction);
            int curvatureAllotment = remainingBudget - coverageReserve;

            List<Candidate> sortedByCurvature = nonStructural
                .OrderByDescending(candidate => candidate.CurvatureMagnitude)
                .ThenBy(candidate => candidate.Row)
                .ThenBy(candidate => candidate.Column)
                .ToList();

            int curvatureTaken = Math.Min(curvatureAllotment, sortedByCurvature.Count);
            curvatureSelected.AddRange(sortedByCurvature.Take(curvatureTaken));
            List<Candidate> remainingForCoverage = sortedByCurvature.Skip(curvatureTaken).ToList();
            int leftover = curvatureAllotment - curvatureTaken;
            int coverageBudgetFinal = coverageReserve + leftover;

            if (coverageBudgetFinal > 0 && remainingForCoverage.Count > 0)
            {
                long totalCells = (long)grid.RowCount * grid.ColumnCount;
                int blockSize = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)totalCells / coverageBudgetFinal)));

                List<IGrouping<(int BlockRow, int BlockColumn), Candidate>> blocksByRowThenColumn = remainingForCoverage
                    .GroupBy(candidate => (BlockRow: candidate.Row / blockSize, BlockColumn: candidate.Column / blockSize))
                    .OrderBy(block => block.Key.BlockRow)
                    .ThenBy(block => block.Key.BlockColumn)
                    .ToList();

                // Round-robin across BlockRows (SolidGround Issue #7 review): visiting every block in strict
                // (BlockRow asc, BlockColumn asc) order and stopping the instant coverageBudgetFinal picks are
                // made meant a coverageBudgetFinal smaller than the number of blocks spanned by the first
                // BlockRow(s) never reached any later BlockRow at all -- the opposite of this pass's spreading
                // purpose. Visiting the earliest not-yet-visited block of every BlockRow (BlockRow ascending)
                // before visiting any BlockRow's second block, and so on, guarantees every BlockRow that still
                // has an unselected candidate gets a pick before any BlockRow gets a second one.
                List<List<IGrouping<(int BlockRow, int BlockColumn), Candidate>>> blocksByBlockRow = [.. blocksByRowThenColumn
                    .GroupBy(block => block.Key.BlockRow)
                    .OrderBy(group => group.Key)
                    .Select(group => group.ToList())];

                List<IGrouping<(int BlockRow, int BlockColumn), Candidate>> blocks = [];
                int maxBlocksInAnyBlockRow = blocksByBlockRow.Count == 0 ? 0 : blocksByBlockRow.Max(blockRow => blockRow.Count);
                for (int wave = 0; wave < maxBlocksInAnyBlockRow; wave++)
                {
                    foreach (List<IGrouping<(int BlockRow, int BlockColumn), Candidate>> blockRow in blocksByBlockRow)
                    {
                        if (wave < blockRow.Count)
                        {
                            blocks.Add(blockRow[wave]);
                        }
                    }
                }

                foreach (IGrouping<(int BlockRow, int BlockColumn), Candidate> block in blocks)
                {
                    if (coverageSelected.Count >= coverageBudgetFinal)
                    {
                        break;
                    }

                    Candidate picked = block
                        .OrderByDescending(candidate => candidate.CurvatureMagnitude)
                        .ThenBy(candidate => candidate.Row)
                        .ThenBy(candidate => candidate.Column)
                        .First();
                    coverageSelected.Add(picked);
                }
            }
        }

        bool interiorCandidatesExhausted = (curvatureSelected.Count + coverageSelected.Count) >= nonStructural.Count;

        List<Candidate> retained = new(retainedStructural.Count + curvatureSelected.Count + coverageSelected.Count);
        retained.AddRange(retainedStructural);
        retained.AddRange(curvatureSelected);
        retained.AddRange(coverageSelected);
        retained = [.. retained.OrderBy(candidate => candidate.Row).ThenBy(candidate => candidate.Column)];

        SimplificationDiagnostics diagnostics = BuildDiagnostics(
            grid, candidates, retained, noDataOrExcludedCellCount, minElevation, maxElevation,
            structuralCandidateCount: structural.Count,
            structuralPointCount: retainedStructural.Count,
            curvatureSelectedPointCount: curvatureSelected.Count,
            coverageFloorPointCount: coverageSelected.Count,
            uniformlySampledPointCount: 0,
            interiorCandidatesExhausted: interiorCandidatesExhausted);

        List<TerrainSample> samples = [.. retained.Select(candidate => ToSample(grid, candidate))];
        return new SimplificationResult(candidates.Count, samples, request, diagnostics);
    }

    /// <summary>
    /// Reachable only via an explicit <see cref="SimplificationMethod.UniformSampler"/> request. Performs no
    /// structural or curvature classification: it selects <c>min(PointBudget, N)</c> candidates spanning the
    /// full row-major candidate list evenly, using <c>floor(i * N / budget)</c> (see the "The comparison sampler:
    /// UniformSampler" section of docs/architecture/terrain-aware-decimation.md). The removed-set
    /// curvature/residual statistics are still computed (Pass 0 is shared by both methods) purely for honest
    /// diagnostics; they play no part in which cells this method selects.
    /// </summary>
    private static SimplificationResult SimplifyUniform(ElevationGrid grid, SimplificationRequest request)
    {
        (List<Candidate> candidates, int noDataOrExcludedCellCount, double? minElevation, double? maxElevation) =
            BuildCandidates(grid, classifyStructural: false);

        int candidateCount = candidates.Count;
        int pointBudget = request.PointBudget;

        List<Candidate> retained;
        if (candidateCount <= pointBudget)
        {
            retained = new List<Candidate>(candidates);
        }
        else
        {
            retained = new List<Candidate>(pointBudget);
            for (int i = 0; i < pointBudget; i++)
            {
                // 64-bit arithmetic: i * candidateCount can exceed int range for a large grid.
                int index = (int)(((long)i * candidateCount) / pointBudget);
                retained.Add(candidates[index]);
            }
        }

        // Already row-major ascending by construction (candidates is row-major and the bucket indices used
        // above are strictly increasing in i), but every method re-sorts the retained set by (Row asc, Column
        // asc) as an explicit final step -- see the "Total ordering and determinism" section of
        // docs/architecture/terrain-aware-decimation.md -- so it is reapplied rather than relied upon implicitly.
        retained = [.. retained.OrderBy(candidate => candidate.Row).ThenBy(candidate => candidate.Column)];

        SimplificationDiagnostics diagnostics = BuildDiagnostics(
            grid, candidates, retained, noDataOrExcludedCellCount, minElevation, maxElevation,
            structuralCandidateCount: 0,
            structuralPointCount: 0,
            curvatureSelectedPointCount: 0,
            coverageFloorPointCount: 0,
            uniformlySampledPointCount: retained.Count,
            interiorCandidatesExhausted: true);

        List<TerrainSample> samples = [.. retained.Select(candidate => ToSample(grid, candidate))];
        return new SimplificationResult(candidates.Count, samples, request, diagnostics);
    }

    /// <summary>
    /// Pass 0: a single row-major scan shared by both methods. Always computes curvature/residual metrics
    /// (needed for removed-point diagnostics regardless of method); computes <see cref="Candidate.MissingNeighborCount"/>
    /// only when <paramref name="classifyStructural"/> is <see langword="true"/> (CurvatureAware), leaving it
    /// 0 for UniformSampler, which never classifies a cell as structural.
    /// </summary>
    private static (List<Candidate> Candidates, int NoDataOrExcludedCellCount, double? MinElevation, double? MaxElevation) BuildCandidates(
        ElevationGrid grid, bool classifyStructural)
    {
        int rowCount = grid.RowCount;
        int columnCount = grid.ColumnCount;
        List<Candidate> candidates = new(rowCount * columnCount);
        int noDataOrExcludedCellCount = 0;
        double? minElevation = null;
        double? maxElevation = null;

        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                double? elevation = grid.GetElevation(row, column);
                if (elevation is null)
                {
                    noDataOrExcludedCellCount++;
                    continue;
                }

                int missingNeighborCount = classifyStructural ? CountMissingNeighbors(grid, row, column) : 0;
                (double curvatureMagnitude, double elevationResidual) = ComputeCellMetrics(grid, row, column, elevation.Value);
                candidates.Add(new Candidate(row, column, elevation.Value, missingNeighborCount, curvatureMagnitude, elevationResidual));

                minElevation = minElevation is null ? elevation.Value : Math.Min(minElevation.Value, elevation.Value);
                maxElevation = maxElevation is null ? elevation.Value : Math.Max(maxElevation.Value, elevation.Value);
            }
        }

        return (candidates, noDataOrExcludedCellCount, minElevation, maxElevation);
    }

    /// <summary>
    /// Returns <see langword="null"/> when <paramref name="row"/>/<paramref name="column"/> is outside the
    /// grid, since <see cref="ElevationGrid.GetElevation"/> performs no bounds check of its own.
    /// </summary>
    private static double? SafeElevation(ElevationGrid grid, int row, int column)
    {
        if (row < 0 || row >= grid.RowCount || column < 0 || column >= grid.ColumnCount)
        {
            return null;
        }

        return grid.GetElevation(row, column);
    }

    /// <summary>Counts how many of the 8 Moore-neighborhood offsets are off-grid or NODATA (0-8).</summary>
    private static int CountMissingNeighbors(ElevationGrid grid, int row, int column)
    {
        int missingNeighborCount = 0;
        for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (int columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                if (rowOffset == 0 && columnOffset == 0)
                {
                    continue;
                }

                if (SafeElevation(grid, row + rowOffset, column + columnOffset) is null)
                {
                    missingNeighborCount++;
                }
            }
        }

        return missingNeighborCount;
    }

    /// <summary>Computes the curvature magnitude and elevation residual for one candidate cell, per the "Curvature
    /// and elevation-residual scoring" section of docs/architecture/terrain-aware-decimation.md.</summary>
    private static (double CurvatureMagnitude, double ElevationResidual) ComputeCellMetrics(ElevationGrid grid, int row, int column, double elevation)
    {
        double? west = SafeElevation(grid, row, column - 1);
        double? east = SafeElevation(grid, row, column + 1);
        double? north = SafeElevation(grid, row - 1, column);
        double? south = SafeElevation(grid, row + 1, column);

        double cxx = west is double westValue && east is double eastValue
            ? (westValue + eastValue - (2d * elevation)) / (grid.CellSizeX * grid.CellSizeX)
            : 0d;
        double cyy = north is double northValue && south is double southValue
            ? (northValue + southValue - (2d * elevation)) / (grid.CellSizeY * grid.CellSizeY)
            : 0d;
        double curvatureMagnitude = Math.Abs(cxx) + Math.Abs(cyy);

        double residualSum = 0d;
        int residualCount = 0;
        if (west is double westResidual) { residualSum += westResidual; residualCount++; }
        if (east is double eastResidual) { residualSum += eastResidual; residualCount++; }
        if (north is double northResidual) { residualSum += northResidual; residualCount++; }
        if (south is double southResidual) { residualSum += southResidual; residualCount++; }
        double elevationResidual = residualCount > 0 ? Math.Abs(elevation - (residualSum / residualCount)) : 0d;

        return (curvatureMagnitude, elevationResidual);
    }

    private static TerrainSample ToSample(ElevationGrid grid, Candidate candidate)
    {
        Coordinate2D center = grid.GetCellCenter(candidate.Row, candidate.Column);
        return new TerrainSample(new Coordinate3D(center.X, center.Y, candidate.Elevation));
    }

    /// <summary>
    /// Builds the shared <see cref="SimplificationDiagnostics"/> for either method: the removed set (Section
    /// 6 rule 5) is derived by filtering the already row-major-ordered full <paramref name="candidates"/> list,
    /// which preserves that ordering for the aggregate max/mean summation.
    /// </summary>
    private static SimplificationDiagnostics BuildDiagnostics(
        ElevationGrid grid,
        List<Candidate> candidates,
        List<Candidate> retained,
        int noDataOrExcludedCellCount,
        double? minElevation,
        double? maxElevation,
        int structuralCandidateCount,
        int structuralPointCount,
        int curvatureSelectedPointCount,
        int coverageFloorPointCount,
        int uniformlySampledPointCount,
        bool interiorCandidatesExhausted)
    {
        HashSet<(int Row, int Column)> retainedPositions = new(retained.Select(candidate => (candidate.Row, candidate.Column)));
        List<Candidate> removed = candidates.Where(candidate => !retainedPositions.Contains((candidate.Row, candidate.Column))).ToList();

        double maxRemovedCurvatureMagnitude = 0d;
        double curvatureSum = 0d;
        double maxRemovedElevationResidual = 0d;
        double residualSum = 0d;
        foreach (Candidate candidate in removed)
        {
            maxRemovedCurvatureMagnitude = Math.Max(maxRemovedCurvatureMagnitude, candidate.CurvatureMagnitude);
            curvatureSum += candidate.CurvatureMagnitude;
            maxRemovedElevationResidual = Math.Max(maxRemovedElevationResidual, candidate.ElevationResidual);
            residualSum += candidate.ElevationResidual;
        }

        double meanRemovedCurvatureMagnitude = removed.Count > 0 ? curvatureSum / removed.Count : 0d;
        double meanRemovedElevationResidual = removed.Count > 0 ? residualSum / removed.Count : 0d;

        return new SimplificationDiagnostics(
            candidatePointCount: candidates.Count,
            noDataOrExcludedCellCount: noDataOrExcludedCellCount,
            structuralCandidateCount: structuralCandidateCount,
            structuralPointCount: structuralPointCount,
            curvatureSelectedPointCount: curvatureSelectedPointCount,
            coverageFloorPointCount: coverageFloorPointCount,
            uniformlySampledPointCount: uniformlySampledPointCount,
            interiorCandidatesExhausted: interiorCandidatesExhausted,
            minElevation: minElevation,
            maxElevation: maxElevation,
            elevationUnit: grid.VerticalReference.Unit,
            maxRemovedCurvatureMagnitude: maxRemovedCurvatureMagnitude,
            meanRemovedCurvatureMagnitude: meanRemovedCurvatureMagnitude,
            maxRemovedElevationResidual: maxRemovedElevationResidual,
            meanRemovedElevationResidual: meanRemovedElevationResidual);
    }
}
