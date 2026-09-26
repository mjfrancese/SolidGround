using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real Census Geocoder. Census is keyless, so this test is gated by a
/// flag and a real address only -- never a key. Dynamically skipped unless <c>SOLIDGROUND_CENSUS_LIVE</c> is
/// exactly <c>"1"</c> and <c>SOLIDGROUND_GEOCODER_LIVE_ADDRESS</c> is a non-empty real address, so it never
/// runs unattended (for example in the repository's self-hosted CI, which sets neither).
/// </summary>
public sealed class CensusGeocoderLiveTests
{
    [Fact]
    public async Task GeocodesARealAddressFromTheConfiguredEnvironmentVariable()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_CENSUS_LIVE");
        string? address = Environment.GetEnvironmentVariable("SOLIDGROUND_GEOCODER_LIVE_ADDRESS");
        if (liveFlag != "1" || string.IsNullOrEmpty(address))
        {
            Assert.Skip("Set SOLIDGROUND_CENSUS_LIVE=1 and SOLIDGROUND_GEOCODER_LIVE_ADDRESS to run the live Census Geocoder test.");
        }

        using var httpClient = new HttpClient();
        var geocoder = new CensusGeocoder(httpClient);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest(address), TestContext.Current.CancellationToken);

        Assert.NotEmpty(acquisition.Candidates);
        Assert.All(acquisition.Candidates, candidate => Assert.Equal(CensusGeocoder.AttributionNotice, candidate.Attribution));
    }
}
