using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Esri;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real Esri World Geocoding Service, with <c>forStorage=true</c>. Never
/// prints the API key or the real address. Dynamically skipped unless <c>SOLIDGROUND_ARCGIS_LIVE</c> is
/// exactly <c>"1"</c>, <c>ARCGIS_API_KEY</c> is a non-empty value, and
/// <c>SOLIDGROUND_GEOCODER_LIVE_ADDRESS</c> is a non-empty real address, so it never runs unattended (for
/// example in the repository's self-hosted CI, which sets none of them).
/// </summary>
[Collection(EsriEnvironmentCollectionDefinition.Name)]
public sealed class EsriGeocoderLiveTests
{
    [Fact]
    public async Task GeocodesARealAddressFromTheConfiguredEnvironmentVariable()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_ARCGIS_LIVE");
        string? apiKey = Environment.GetEnvironmentVariable("ARCGIS_API_KEY");
        string? address = Environment.GetEnvironmentVariable("SOLIDGROUND_GEOCODER_LIVE_ADDRESS");
        if (liveFlag != "1" || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(address))
        {
            Assert.Skip("Set SOLIDGROUND_ARCGIS_LIVE=1, ARCGIS_API_KEY, and SOLIDGROUND_GEOCODER_LIVE_ADDRESS to run the live Esri test.");
        }

        using var httpClient = new HttpClient();
        var geocoder = new EsriGeocoder(httpClient, new EnvironmentEsriApiKeyProvider());

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest(address), TestContext.Current.CancellationToken);

        Assert.NotEmpty(acquisition.Candidates);
        // Second line of defense, mirroring OpenTopographyLiveTests' own redaction re-assertion: the raw key
        // travels only in the Authorization header and must never surface in any returned candidate field.
        Assert.All(acquisition.Candidates, candidate =>
        {
            Assert.DoesNotContain(apiKey, candidate.MatchedAddress, StringComparison.Ordinal);
            Assert.Equal(EsriGeocoder.AttributionNotice, candidate.Attribution);
        });
    }
}
