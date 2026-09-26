namespace SolidGround.Core.Sources;

/// <summary>Which <see cref="IAddressGeocoder"/> implementation to use. Defaults to <see cref="Census"/> -- free, keyless, federal-public-domain (AGENTS.md; docs/architecture/phase-3-interactive-add-in-research.md owner decision 1).</summary>
public enum AddressGeocoderProvider
{
    Census,
    Geocodio,
    Esri,
}

/// <summary>
/// Selects which <see cref="IAddressGeocoder"/> <see cref="AddressGeocoderFactory"/> builds. This type is
/// not wired into <c>TerrainRequestSettings</c>'s shipped JSON template in this issue -- that wiring belongs
/// to a future CLI or dialog issue that consumes <see cref="AddressGeocoderFactory"/> once it exists. This
/// type never checks whether a keyed provider's environment variable is set: that check belongs entirely to
/// the provider's own API key provider at <see cref="IAddressGeocoder.GeocodeAsync"/> call time, exactly
/// mirroring how <c>OpenTopographyUsgs1mSource</c> -- not <c>TerrainRequestSettings</c> -- owns its own
/// missing-key check.
/// </summary>
public sealed class AddressGeocoderSettings
{
    public AddressGeocoderProvider Provider { get; init; } = AddressGeocoderProvider.Census;
}
