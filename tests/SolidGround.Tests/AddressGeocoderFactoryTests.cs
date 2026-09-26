using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.Geocodio;

namespace SolidGround.Tests;

public sealed class AddressGeocoderFactoryTests
{
    [Fact]
    public void DefaultSettingsSelectCensus()
    {
        Assert.Equal(AddressGeocoderProvider.Census, new AddressGeocoderSettings().Provider);
    }

    [Fact]
    public void CreateReturnsACensusGeocoderForCensusProvider()
    {
        using var httpClient = new HttpClient();

        IAddressGeocoder geocoder = AddressGeocoderFactory.Create(new AddressGeocoderSettings { Provider = AddressGeocoderProvider.Census }, httpClient);

        Assert.IsType<CensusGeocoder>(geocoder);
    }

    [Fact]
    public void CreateReturnsAGeocodioGeocoderForGeocodioProvider()
    {
        using var httpClient = new HttpClient();

        IAddressGeocoder geocoder = AddressGeocoderFactory.Create(new AddressGeocoderSettings { Provider = AddressGeocoderProvider.Geocodio }, httpClient);

        Assert.IsType<GeocodioGeocoder>(geocoder);
    }

    [Fact]
    public void CreateReturnsAnEsriGeocoderForEsriProvider()
    {
        using var httpClient = new HttpClient();

        IAddressGeocoder geocoder = AddressGeocoderFactory.Create(new AddressGeocoderSettings { Provider = AddressGeocoderProvider.Esri }, httpClient);

        Assert.IsType<EsriGeocoder>(geocoder);
    }

    [Fact]
    public void CreateThrowsForAnUndefinedProviderValue()
    {
        using var httpClient = new HttpClient();
        var settings = new AddressGeocoderSettings { Provider = (AddressGeocoderProvider)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() => AddressGeocoderFactory.Create(settings, httpClient));
    }

    [Fact]
    public void CreateRejectsNullSettingsOrHttpClient()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => AddressGeocoderFactory.Create(null!, httpClient));
        Assert.Throws<ArgumentNullException>(() => AddressGeocoderFactory.Create(new AddressGeocoderSettings(), null!));
    }
}
