using SolidGround.Core.Hosting;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// Every input <see cref="SolidGroundDialogViewModel"/> needs at construction time, gathered by whatever
/// constructs it -- a later stage's <c>SolidGroundDialogHost.ShowModal</c>, or this stage's own throwaway,
/// uncommitted local WPF host used for visual iteration (SolidGround Issue #31, PH3-4, Stage C: see
/// docs/architecture/revit-interactive-dialog.md "Flow/state model"). Every field here is a plain Core type
/// or primitive -- none is Revit-API-typed -- so the view-model itself never depends on <c>Document</c>,
/// <c>Level</c>, <c>ToposolidType</c>, or any other Revit API type: whatever constructs this record is
/// responsible for resolving those against a real, open <c>Document</c> first (mirroring how
/// <c>LevelAndTypeResolver</c> already separates "project a Revit element into a Revit-free candidate" from
/// "let a Core selector choose one").
/// </summary>
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
    AoiSettings ConfiguredAreaOfInterest);
