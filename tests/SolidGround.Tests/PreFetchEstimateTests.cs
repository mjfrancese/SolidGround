using SolidGround.Core.Processing;
using SolidGround.Core.Workflow;

namespace SolidGround.Tests;

public sealed class PreFetchEstimateTests
{
    [Fact]
    public void RadiusEstimateIsPureAndLabelsItsTwoRequestCostModel()
    {
        PreFetchEstimate estimate = PreFetchEstimator.FromRadius(new RadiusAoiSettings { CenterLatitude = 41, CenterLongitude = -93, RadiusMeters = 10 });
        Assert.Equal(2, estimate.OpenTopographyRequestCount);
        Assert.Equal(315L, estimate.ApproximateOneMeterSamples);
        Assert.Contains("not an entitlement", estimate.Label, StringComparison.Ordinal);
    }
}
