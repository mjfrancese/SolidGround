using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.Geocodio;

namespace SolidGround.Core.Sources;

/// <summary>
/// Builds the <see cref="IAddressGeocoder"/> selected by <see cref="AddressGeocoderSettings.Provider"/>. A
/// keyed provider's key is resolved lazily, at <see cref="IAddressGeocoder.GeocodeAsync"/> call time, not
/// here -- constructing the geocoder never touches the environment or a user-secrets file.
/// </summary>
public static class AddressGeocoderFactory
{
    public static IAddressGeocoder Create(AddressGeocoderSettings settings, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(httpClient);

        return settings.Provider switch
        {
            AddressGeocoderProvider.Census => new CensusGeocoder(httpClient),
            AddressGeocoderProvider.Geocodio => new GeocodioGeocoder(httpClient, new EnvironmentGeocodioApiKeyProvider()),
            AddressGeocoderProvider.Esri => new EsriGeocoder(httpClient, new EnvironmentEsriApiKeyProvider()),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Provider, "Unsupported address geocoder provider."),
        };
    }
}
