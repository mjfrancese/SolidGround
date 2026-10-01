using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

public sealed class CountyParcelServiceMetadataTests
{
    [Fact]
    public void RejectsARegisteredFieldAbsentFromSelectedLayerMetadata()
    {
        CountyParcelRegistryEntry registration = new()
        {
            Geoid = "99999", DisplayName = "Synthetic", ServiceBaseUrl = "https://parcels.example.invalid/FeatureServer", LayerIndex = 3,
            FieldMap = new CountyParcelFieldMap { ParcelId = "PARCEL", SitusAddress = "SITUS" }, LicenseDisclaimerText = "Synthetic license.",
        };
        CountyParcelServiceMetadata metadata = new(new Uri(registration.ServiceBaseUrl), [new CountyParcelServiceLayerMetadata(3, "Parcels", new HashSet<string>(["PARCEL"]))]);

        FormatException error = Assert.Throws<FormatException>(() => CountyParcelServiceMetadataValidator.ValidateRegistration(metadata, registration));

        Assert.Contains("SITUS", error.Message, StringComparison.Ordinal);
    }
}
