namespace SolidGround.Core.Sources;

/// <summary>
/// Resolves a street address to zero or more ranked, approximate WGS 84 coordinate candidates. Sibling to
/// <see cref="IElevationSource"/>: mechanism (this interface) is kept separate from policy (which
/// implementation is selected), the same separation <see cref="IElevationSource"/> already established for
/// elevation acquisition. See <see cref="AddressGeocoderFactory"/> for provider selection and
/// docs/architecture/address-geocoding.md for the full design record.
/// </summary>
public interface IAddressGeocoder
{
    /// <exception cref="AddressGeocoderException">
    /// The address could not be geocoded: no API key is configured for a keyed provider, the provider
    /// rejected the request, the provider reported zero candidates, a transport-level failure occurred, or
    /// the response could not be interpreted. See the concrete provider's own exception types.
    /// </exception>
    ValueTask<AddressGeocodeAcquisition> GeocodeAsync(
        AddressGeocodeRequest request,
        CancellationToken cancellationToken = default);
}
