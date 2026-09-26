using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Geocodio;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real Geocodio endpoint. Never prints the API key or the real address.
/// Dynamically skipped unless <c>SOLIDGROUND_GEOCODIO_LIVE</c> is exactly <c>"1"</c>, <c>GEOCODIO_API_KEY</c>
/// is a non-empty value, and <c>SOLIDGROUND_GEOCODER_LIVE_ADDRESS</c> is a non-empty real address, so it
/// never runs unattended (for example in the repository's self-hosted CI, which sets none of them).
/// </summary>
[Collection(GeocodioEnvironmentCollectionDefinition.Name)]
public sealed class GeocodioGeocoderLiveTests
{
    [Fact]
    public async Task GeocodesARealAddressFromTheConfiguredEnvironmentVariable()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_GEOCODIO_LIVE");
        string? apiKey = Environment.GetEnvironmentVariable("GEOCODIO_API_KEY");
        string? address = Environment.GetEnvironmentVariable("SOLIDGROUND_GEOCODER_LIVE_ADDRESS");
        if (liveFlag != "1" || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(address))
        {
            Assert.Skip("Set SOLIDGROUND_GEOCODIO_LIVE=1, GEOCODIO_API_KEY, and SOLIDGROUND_GEOCODER_LIVE_ADDRESS to run the live Geocodio test.");
        }

        using var httpClient = new HttpClient();
        var geocoder = new GeocodioGeocoder(httpClient, new EnvironmentGeocodioApiKeyProvider());

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest(address), TestContext.Current.CancellationToken);

        Assert.NotEmpty(acquisition.Candidates);
        // Second line of defense, mirroring OpenTopographyLiveTests' own redaction re-assertion: the raw key
        // travels only in the Authorization header and must never surface in any returned candidate field.
        Assert.All(acquisition.Candidates, candidate =>
        {
            Assert.DoesNotContain(apiKey, candidate.MatchedAddress, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, candidate.Attribution, StringComparison.Ordinal);
        });
    }
}
