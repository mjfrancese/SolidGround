using SolidGround.Core.Aois;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real OpenTopography endpoint. It never prints the API key or any
/// request URI. It is dynamically skipped unless both <c>SOLIDGROUND_OPENTOPOGRAPHY_LIVE</c> is exactly
/// <c>"1"</c> and <c>OPENTOPOGRAPHY_API_KEY</c> is a non-empty value, so it never runs unattended (for
/// example in the repository's self-hosted CI, which sets neither).
/// </summary>
[Collection(OpenTopographyEnvironmentCollectionDefinition.Name)]
public sealed class OpenTopographyLiveTests
{
    [Fact]
    public async Task FetchesASmallRealAreaAroundTheRobandeeLaneScenario()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_OPENTOPOGRAPHY_LIVE");
        string? apiKey = Environment.GetEnvironmentVariable("OPENTOPOGRAPHY_API_KEY");
        if (liveFlag != "1" || string.IsNullOrEmpty(apiKey))
        {
            Assert.Skip("Set SOLIDGROUND_OPENTOPOGRAPHY_LIVE=1 and OPENTOPOGRAPHY_API_KEY to run the live OpenTopography test.");
        }

        using var httpClient = new HttpClient();
        var source = new OpenTopographyUsgs1mSource(httpClient, new EnvironmentOpenTopographyApiKeyProvider());
        const double centerLatitude = 38.700186;
        const double centerLongitude = -90.477652;
        const double delta = 0.0007;
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(
            centerLongitude - delta,
            centerLatitude - delta,
            centerLongitude + delta,
            centerLatitude + delta));

        ElevationAcquisition acquisition = await source.AcquireAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("OpenTopography", acquisition.Source.SourceName);
        Assert.Equal("USGS1m", acquisition.Source.DatasetIdentifier);
        Assert.NotNull(acquisition.Data);
    }
}
