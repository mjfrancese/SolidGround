using SolidGround.Core.Hosting;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Dialog;

/// <summary>Immutable host-resolved dependencies and preferences for one guided dialog run.</summary>
/// <remarks>All lookup work copies the relevant values into a revision ticket before awaiting I/O.</remarks>
internal sealed record SolidGroundDialogInputs(
    IAddressGeocoder Geocoder,
    AddressGeocoderProvider GeocoderProvider,
    IParcelBoundarySource? ParcelSource,
    IReadOnlyList<NamedElevationCandidate> LevelCandidates,
    IReadOnlyList<NamedCandidate> ToposolidTypeCandidates,
    string? ConfiguredLevelName,
    string? ConfiguredToposolidTypeName,
    LengthUnit PrefilledOutputUnit,
    int PrefilledPointBudget,
    bool PrefilledWriteSharedCoordinatesIfAbsent,
    bool DocumentAlreadyHasSharedCoordinates,
    RevitIniToposolidThresholds.Thresholds RevitIniThresholds,
    string RevitIniPath,
    int NetworkTimeoutSeconds,
    AoiSettings ConfiguredAreaOfInterest,
    double NearbySearchRadiusMeters,
    TerrainAcquisitionMode Mode,
    RevitSettings? Settings = null,
    Func<RevitSettings, RevitSettings?>? EditSettings = null);
