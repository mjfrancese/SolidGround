using SolidGround.Core.Processing;

namespace SolidGround.Revit.Settings;

/// <summary>
/// The complete decoded settings for one <c>CreateToposolidCommand</c> run: the host-neutral
/// <see cref="Request"/> (validated by <see cref="TerrainRequestSettings.Validate"/>) plus the Revit-only
/// <see cref="Target"/> name overrides, the Revit-only <see cref="SharedCoordinates"/> opt-in (SolidGround
/// Issue #30, PH3-3), and the Revit-only <see cref="AddressAndParcel"/> geocoder/parcel-source configuration
/// the interactive dialog uses (SolidGround Issue #31, PH3-4). All real validation lives on
/// <see cref="TerrainRequestSettings"/> itself; this wrapper adds no new rules. See SolidGround Issue #15's
/// design record §2.4 row 20.
/// </summary>
internal sealed record RevitSettings(
    TerrainRequestSettings Request,
    RevitTargetSettings Target,
    RevitSharedCoordinatesSettings SharedCoordinates,
    RevitAddressAndParcelSettings AddressAndParcel);
