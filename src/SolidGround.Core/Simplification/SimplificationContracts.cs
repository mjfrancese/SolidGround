using SolidGround.Core.Terrain;

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

    public SimplificationResult(int originalPointCount, IEnumerable<TerrainSample> retainedSamples, SimplificationRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(originalPointCount);

        ArgumentNullException.ThrowIfNull(retainedSamples);
        ArgumentNullException.ThrowIfNull(request);

        this.retainedSamples = retainedSamples.ToArray();
        if (this.retainedSamples.Any(sample => sample is null) || this.retainedSamples.Length > originalPointCount || this.retainedSamples.Length > request.PointBudget)
        {
            throw new ArgumentException("Retained samples must be non-null and cannot exceed the original count.", nameof(retainedSamples));
        }

        OriginalPointCount = originalPointCount;
        Request = request;
    }

    public int OriginalPointCount { get; }
    public int RetainedPointCount => retainedSamples.Length;
    public int RemovedPointCount => OriginalPointCount - RetainedPointCount;
    public SimplificationRequest Request { get; }
    public IReadOnlyList<TerrainSample> RetainedSamples => Array.AsReadOnly(retainedSamples);
}
