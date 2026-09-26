using System.Globalization;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

/// <summary>
/// Opt-in end-to-end test against a real, host-configured county ArcGIS parcel service. Mirrors
/// <c>EsriGeocoderLiveTests</c>'s exact skip-never-fail style: read-only environment-variable access, no
/// mutation of the real process environment, so no <c>[CollectionDefinition(DisableParallelization = true)]</c>
/// is needed. This MVP registry format carries no per-entry key/token, so there is no key variable to check.
/// CI never sets any of these five variables.
/// </summary>
public sealed class CountyParcelRegistryLiveTests
{
    [Fact]
    public async Task ResolvesAgainstARealHostConfiguredCountyParcelService()
    {
        string? liveFlag = Environment.GetEnvironmentVariable("SOLIDGROUND_COUNTY_PARCEL_LIVE");
        string? registryPath = Environment.GetEnvironmentVariable("SOLIDGROUND_COUNTY_PARCEL_REGISTRY_PATH");
        string? geoid = Environment.GetEnvironmentVariable("SOLIDGROUND_COUNTY_PARCEL_GEOID");
        string? latitudeText = Environment.GetEnvironmentVariable("SOLIDGROUND_COUNTY_PARCEL_LIVE_LATITUDE");
        string? longitudeText = Environment.GetEnvironmentVariable("SOLIDGROUND_COUNTY_PARCEL_LIVE_LONGITUDE");

        // Both TryParse calls run unconditionally (not inside the ||-chain below) so latitude/longitude are
        // always definitely assigned afterward, regardless of which branch the compiler's flow analysis
        // believes is reachable once Assert.Skip is involved.
        bool latitudeParsed = double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude);
        bool longitudeParsed = double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude);

        if (liveFlag != "1"
            || string.IsNullOrEmpty(registryPath)
            || string.IsNullOrEmpty(geoid)
            || !latitudeParsed
            || !longitudeParsed)
        {
            Assert.Skip(
                "Set SOLIDGROUND_COUNTY_PARCEL_LIVE=1, SOLIDGROUND_COUNTY_PARCEL_REGISTRY_PATH, " +
                "SOLIDGROUND_COUNTY_PARCEL_GEOID, SOLIDGROUND_COUNTY_PARCEL_LIVE_LATITUDE, and " +
                "SOLIDGROUND_COUNTY_PARCEL_LIVE_LONGITUDE to run the live county parcel test.");
        }

        CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
        using var httpClient = new HttpClient();
        var source = new CountyParcelRegistrySource(httpClient, registry, geoid);

        // The supplied point might sit outside every parcel the live service knows about, so this proves the
        // request/parse pipeline completes end-to-end, never a guaranteed match.
        ParcelBoundaryAcquisition acquisition = await source.FindAsync(
            new ParcelPointQuery(latitude, longitude), TestContext.Current.CancellationToken);

        Assert.NotNull(acquisition);
    }
}
