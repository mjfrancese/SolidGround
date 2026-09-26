using System.Globalization;
using SolidGround.Core.Sources.Census;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against the real, keyless Census <c>geographies/coordinates</c> endpoint. Mirrors
/// <see cref="CensusGeocoderLiveTests"/>'s/<see cref="CountyParcelRegistryLiveTests"/>'s exact skip-never-fail
/// style: read-only environment-variable access, never a key (this endpoint takes none). CI never sets any of
/// these three variables.
/// </summary>
public sealed class CensusCountyLookupLiveTests
{
    [Fact]
    public async Task ResolvesAgainstTheRealKeylessCensusCountyLookupEndpoint()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE");
        string? latitudeText = Environment.GetEnvironmentVariable("SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LATITUDE");
        string? longitudeText = Environment.GetEnvironmentVariable("SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LONGITUDE");

        // Both TryParse calls run unconditionally (not inside the ||-chain below) so latitude/longitude are
        // always definitely assigned afterward, mirroring CountyParcelRegistryLiveTests's identical reasoning.
        bool latitudeParsed = double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude);
        bool longitudeParsed = double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude);

        if (liveFlag != "1" || !latitudeParsed || !longitudeParsed)
        {
            Assert.Skip(
                "Set SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE=1, SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LATITUDE, and " +
                "SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LONGITUDE to run the live Census county lookup test.");
        }

        using var httpClient = new HttpClient();
        var lookup = new CensusCountyLookup(httpClient);

        // The supplied point might sit outside the US entirely, so this proves the request/parse pipeline
        // completes end-to-end without throwing an unexpected-response exception, never a guaranteed match.
        try
        {
            string geoid = await lookup.FindCountyGeoidAsync(latitude, longitude, TestContext.Current.CancellationToken);
            Assert.Matches(@"\A\d{5}\z", geoid);
        }
        catch (CensusCountyLookupNoCountyException)
        {
            // A supplied point outside every county is still a successful end-to-end round trip.
        }
    }
}
