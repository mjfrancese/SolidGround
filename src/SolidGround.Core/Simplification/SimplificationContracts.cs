using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Core.Simplification;

/// <summary>Algorithms an elevation simplifier may implement.</summary>
public enum SimplificationMethod
{
    CurvatureAware,
    TinError,
    UniformSampler,
}

/// <summary>Defines the requested output budget and selection strategy.</summary>
public sealed record SimplificationRequest
{
    public SimplificationRequest(int pointBudget = 15000, SimplificationMethod method = SimplificationMethod.CurvatureAware)
    {
        if (pointBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointBudget), pointBudget, "Point budget must be positive.");
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported simplification method.");
        }

        PointBudget = pointBudget;
        Method = method;
    }

    public int PointBudget { get; }
    public SimplificationMethod Method { get; }
}

/// <summary>Algorithm seam for retaining terrain samples within a requested point budget.</summary>
public interface ITerrainSimplifier
{
    ValueTask<SimplificationResult> SimplifyAsync(
        ElevationData terrain,
        SimplificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Accounts for samples retained by a simplifier without defining its algorithm.</summary>
public sealed class SimplificationResult
{
    private readonly TerrainSample[] retainedSamples;

    public SimplificationResult(
        int originalPointCount,
        IEnumerable<TerrainSample> retainedSamples,
        SimplificationRequest request,
        SimplificationDiagnostics? diagnostics = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalPointCount);

        ArgumentNullException.ThrowIfNull(retainedSamples);
        ArgumentNullException.ThrowIfNull(request);

        this.retainedSamples = retainedSamples.ToArray();
        if (this.retainedSamples.Any(sample => sample is null) || this.retainedSamples.Length > originalPointCount || this.retainedSamples.Length > request.PointBudget)
        {
            throw new ArgumentException("Retained samples must be non-null and cannot exceed the original count.", nameof(retainedSamples));
        }

        if (diagnostics is not null)
        {
            if (diagnostics.CandidatePointCount != originalPointCount)
            {
                throw new ArgumentException("Diagnostics candidate point count must equal the original point count.", nameof(diagnostics));
            }

            int diagnosedRetainedCount = diagnostics.StructuralPointCount + diagnostics.CurvatureSelectedPointCount
                + diagnostics.CoverageFloorPointCount + diagnostics.UniformlySampledPointCount;
            if (diagnosedRetainedCount != this.retainedSamples.Length)
            {
                throw new ArgumentException("Diagnostics selection counts must sum to the retained sample count.", nameof(diagnostics));
            }
        }

        OriginalPointCount = originalPointCount;
        Request = request;
        Diagnostics = diagnostics;
    }

    public int OriginalPointCount { get; }
    public int RetainedPointCount => retainedSamples.Length;
    public int RemovedPointCount => OriginalPointCount - RetainedPointCount;
    public SimplificationRequest Request { get; }
    public IReadOnlyList<TerrainSample> RetainedSamples => Array.AsReadOnly(retainedSamples);
    public SimplificationDiagnostics? Diagnostics { get; }
}

/// <summary>
/// Quantifies what a simplifier saw and kept: full-input accounting, why each retained point survived, the
/// source elevation range, and how much curvature/relief was left out. Every count and statistic is computed
/// over the FULL source terrain the simplifier received, not only the retained subset, so a caller can see
/// what a tight budget excluded.
/// </summary>
public sealed record SimplificationDiagnostics
{
    public SimplificationDiagnostics(
        int candidatePointCount,
        int noDataOrExcludedCellCount,
        int structuralCandidateCount,
        int structuralPointCount,
        int curvatureSelectedPointCount,
        int coverageFloorPointCount,
        int uniformlySampledPointCount,
        bool interiorCandidatesExhausted,
        double? minElevation,
        double? maxElevation,
        LengthUnit elevationUnit,
        double maxRemovedCurvatureMagnitude,
        double meanRemovedCurvatureMagnitude,
        double maxRemovedElevationResidual,
        double meanRemovedElevationResidual)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidatePointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(noDataOrExcludedCellCount);
        ArgumentOutOfRangeException.ThrowIfNegative(structuralCandidateCount);
        ArgumentOutOfRangeException.ThrowIfNegative(structuralPointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(curvatureSelectedPointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(coverageFloorPointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(uniformlySampledPointCount);

        if (structuralPointCount > structuralCandidateCount)
        {
            throw new ArgumentException("Retained structural points cannot exceed structural candidates.", nameof(structuralPointCount));
        }

        if ((minElevation is null) != (maxElevation is null))
        {
            throw new ArgumentException("Minimum and maximum elevation must both be present or both be null.", nameof(minElevation));
        }

        if ((candidatePointCount == 0) != (minElevation is null))
        {
            throw new ArgumentException(
                "Elevation bounds must be present exactly when the candidate point count is positive.", nameof(minElevation));
        }

        if (minElevation is not null)
        {
            if (!double.IsFinite(minElevation.Value) || !double.IsFinite(maxElevation!.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(minElevation), "Elevation bounds must be finite.");
            }
            if (minElevation.Value > maxElevation.Value)
            {
                throw new ArgumentException("Minimum elevation cannot exceed maximum elevation.", nameof(minElevation));
            }
        }

        _ = LengthConverter.MetersPerUnit(elevationUnit); // throws ArgumentOutOfRangeException for an undefined unit

        // No meanX-cannot-exceed-maxX cross-check here (SolidGround Issue #7), unlike the minElevation/
        // maxElevation pair above: maxRemovedCurvatureMagnitude/meanRemovedCurvatureMagnitude and
        // maxRemovedElevationResidual/meanRemovedElevationResidual are independently accumulated
        // floating-point sums (see GridTerrainSimplifier removed-candidate accounting), and IEEE-754
        // summation of equal values can round a computed mean a single ulp above the computed max even
        // though every underlying value is at most that max (for example 0.1 + 0.1 + 0.1, divided by 3,
        // rounds to slightly more than 0.1). Rejecting that as invalid input would make an entirely
        // correct, honestly computed diagnostics value throw. Only finiteness and non-negativity are
        // validated for these four statistics.
        ValidateNonNegativeFinite(maxRemovedCurvatureMagnitude, nameof(maxRemovedCurvatureMagnitude));
        ValidateNonNegativeFinite(meanRemovedCurvatureMagnitude, nameof(meanRemovedCurvatureMagnitude));
        ValidateNonNegativeFinite(maxRemovedElevationResidual, nameof(maxRemovedElevationResidual));
        ValidateNonNegativeFinite(meanRemovedElevationResidual, nameof(meanRemovedElevationResidual));

        CandidatePointCount = candidatePointCount;
        NoDataOrExcludedCellCount = noDataOrExcludedCellCount;
        StructuralCandidateCount = structuralCandidateCount;
        StructuralPointCount = structuralPointCount;
        CurvatureSelectedPointCount = curvatureSelectedPointCount;
        CoverageFloorPointCount = coverageFloorPointCount;
        UniformlySampledPointCount = uniformlySampledPointCount;
        InteriorCandidatesExhausted = interiorCandidatesExhausted;
        MinElevation = minElevation;
        MaxElevation = maxElevation;
        ElevationUnit = elevationUnit;
        MaxRemovedCurvatureMagnitude = maxRemovedCurvatureMagnitude;
        MeanRemovedCurvatureMagnitude = meanRemovedCurvatureMagnitude;
        MaxRemovedElevationResidual = maxRemovedElevationResidual;
        MeanRemovedElevationResidual = meanRemovedElevationResidual;
    }

    private static void ValidateNonNegativeFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and non-negative.");
        }
    }

    public int CandidatePointCount { get; }
    public int NoDataOrExcludedCellCount { get; }
    public int StructuralCandidateCount { get; }
    public int StructuralPointCount { get; }
    /// <summary>Structural candidates dropped because the requested budget could not hold them all.</summary>
    public int StructuralPointsOmittedForBudget => StructuralCandidateCount - StructuralPointCount;
    /// <summary>True when a structural (edge/hole-rim/boundary-rim) point had to be dropped to respect the budget.</summary>
    public bool StructuralBudgetShortfall => StructuralPointsOmittedForBudget > 0;
    public int CurvatureSelectedPointCount { get; }
    public int CoverageFloorPointCount { get; }
    public int UniformlySampledPointCount { get; }
    /// <summary>True when interior selection ran out of eligible candidates before it ran out of budget (always true for UniformSampler).</summary>
    public bool InteriorCandidatesExhausted { get; }
    public double? MinElevation { get; }
    public double? MaxElevation { get; }
    public LengthUnit ElevationUnit { get; }
    /// <summary>Largest discrete-curvature magnitude, in ElevationUnit per squared horizontal grid unit, among candidates NOT retained. A generalization-quality proxy, not a certified geometric error bound.</summary>
    public double MaxRemovedCurvatureMagnitude { get; }
    public double MeanRemovedCurvatureMagnitude { get; }
    /// <summary>Largest |elevation - average of available axis neighbors|, in ElevationUnit, among candidates NOT retained.</summary>
    public double MaxRemovedElevationResidual { get; }
    public double MeanRemovedElevationResidual { get; }
}
