using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real OpenTopography endpoint. It never prints the API key or any
/// request URI. It is dynamically skipped unless both <c>SOLIDGROUND_OPENTOPOGRAPHY_LIVE</c> is exactly
/// <c>"1"</c> and <c>OPENTOPOGRAPHY_API_KEY</c> is a non-empty value, so it never runs unattended (for
/// example in the repository's self-hosted CI, which sets neither). As of Issue #21's 2026-09-19 setup,
/// OpenTopography's USGS 1 m response carries no reference metadata of its own, so this always exercises
/// the hybrid, two-request flow and its GeoTIFF-GeoKeys evidence; see
/// docs/architecture/opentopography-usgs1m-source.md's "Two-request contract, verified 2026-09-19" section.
/// </summary>
[Collection(OpenTopographyEnvironmentCollectionDefinition.Name)]
public sealed class OpenTopographyLiveTests
{
    [Fact]
    public async Task FetchesASmallRealAreaAroundTheExampleSiteLaneScenario()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_OPENTOPOGRAPHY_LIVE");
        string? apiKey = Environment.GetEnvironmentVariable("OPENTOPOGRAPHY_API_KEY");
        if (liveFlag != "1" || string.IsNullOrEmpty(apiKey))
        {
            Assert.Skip("Set SOLIDGROUND_OPENTOPOGRAPHY_LIVE=1 and OPENTOPOGRAPHY_API_KEY to run the live OpenTopography test.");
        }

        using var httpClient = new HttpClient();
        var source = new OpenTopographyUsgs1mSource(httpClient, new EnvironmentOpenTopographyApiKeyProvider());
        const double centerLatitude = [withheld];
        const double centerLongitude = [withheld];
        const double delta = 0.0007;
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(
            centerLongitude - delta,
            centerLatitude - delta,
            centerLongitude + delta,
            centerLatitude + delta));

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(request, TestContext.Current.CancellationToken);
        ElevationAcquisition acquisition = result.Acquisition;

        Assert.Equal("OpenTopography", acquisition.Source.SourceName);
        Assert.Equal("USGS1m", acquisition.Source.DatasetIdentifier);
        Assert.NotNull(acquisition.Data);

        Assert.Equal(OpenTopographyReferenceSource.GeoTiffGeoKeys, result.Evidence.ReferenceSource);
        Assert.Equal(ReferenceOrigin.SourceMetadataResponse, result.Evidence.HorizontalReferenceOrigin);
        Assert.Equal(ReferenceOrigin.DatasetDocumentation, result.Evidence.VerticalReferenceOrigin);

        OpenTopographyMetadataRequestEvidence metadataRequest = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.Equal(26915, metadataRequest.ProjectedCoordinateSystemCode);

        HorizontalReference horizontal = acquisition.Data.HorizontalReference;
        Assert.Equal("EPSG:26915", horizontal.CoordinateReferenceSystem);
        Assert.Equal(HorizontalReferenceKind.Projected, horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, horizontal.Unit.LinearUnit);

        VerticalReference vertical = acquisition.Data.VerticalReference;
        Assert.Equal("NAVD88", vertical.Datum);
        Assert.Equal(LengthUnit.Meter, vertical.Unit);

        Assert.DoesNotContain(apiKey, result.Evidence.RedactedRequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, metadataRequest.RedactedRequestUri, StringComparison.Ordinal);
    }
}
