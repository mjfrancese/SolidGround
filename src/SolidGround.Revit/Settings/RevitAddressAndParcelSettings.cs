using SolidGround.Core.Sources;

namespace SolidGround.Revit.Settings;

/// <summary>
/// Configures which <see cref="IAddressGeocoder"/>/<see cref="IParcelBoundarySource"/> the interactive dialog
/// (SolidGround Issue #31, PH3-4) uses. <see cref="CountyRegistryPath"/> wins when non-blank; else
/// <see cref="LocalParcelFilePath"/> wins when non-blank; else the dialog shows a clear inline configuration
/// error the first time a parcel lookup is attempted (never at settings-load time -- mirrors how a missing
/// <c>OPENTOPOGRAPHY_API_KEY</c> is only ever reported, never thrown, at the point a fetch is attempted). See
/// docs/architecture/revit-interactive-dialog.md's "Settings interaction: prefill, not override".
/// </summary>
/// <param name="GeocoderProvider">Which <see cref="IAddressGeocoder"/> the dialog's address entry step uses. Defaults to <see cref="AddressGeocoderProvider.Census"/> (free, keyless).</param>
/// <param name="CountyRegistryPath">A machine-local county parcel registry file path (see <c>CountyParcelRegistry.Load</c>), or <see langword="null"/>/blank when not configured.</param>
/// <param name="CountyGeoidOverride">
/// A fixed 5-digit Census county GEOID, or <see langword="null"/>/blank to auto-resolve one from the confirmed
/// geocode candidate's own coordinates via <c>CensusCountyLookup.FindCountyGeoidAsync</c> (mirroring
/// <c>SolidGround.Cli.Commands.ParcelCommand</c>'s own existing auto-GEOID behavior) -- see
/// <see cref="SolidGround.Core.Sources.CountyParcels.AutoGeoidCountyParcelSource"/>.
/// </param>
/// <param name="LocalParcelFilePath">A local GeoJSON parcel export path (see <c>LocalParcelFileOptions.Path</c>), used only when <see cref="CountyRegistryPath"/> is blank.</param>
/// <param name="LocalParcelFileSourceLabel">A human-readable label for <see cref="LocalParcelFilePath"/> (see <c>LocalParcelFileOptions.SourceLabel</c>).</param>
/// <param name="LocalParcelFileLicenseDisclaimerText">The local file's own license/disclaimer text, surfaced verbatim (see <c>LocalParcelFileOptions.LicenseDisclaimerText</c>).</param>
internal sealed record RevitAddressAndParcelSettings(
    AddressGeocoderProvider GeocoderProvider,
    string? CountyRegistryPath,
    string? CountyGeoidOverride,
    string? LocalParcelFilePath,
    string? LocalParcelFileSourceLabel,
    string? LocalParcelFileLicenseDisclaimerText);
