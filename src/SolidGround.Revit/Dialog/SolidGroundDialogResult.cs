using SolidGround.Core.Aois;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Units;
using SolidGround.Revit.Settings;
using SolidGround.Revit.Processing;
using SolidGround.Core.Sources.BuildingOutlines;

namespace SolidGround.Revit.Dialog;

/// <summary>Validated choices returned by the guided dialog before command-time Preflight.</summary>
internal sealed record SolidGroundDialogResult(
    DialogAoiSource AoiSource,
    AreaOfInterest? Aoi,
    NamedElevationCandidate Level,
    NamedCandidate ToposolidType,
    LengthUnit OutputUnit,
    int PointBudget,
    bool WriteSharedCoordinatesIfAbsent,
    AddressParcelProvenance? AddressParcel,
    RevitSettings? EffectiveSettings = null,
    PreparedTerrainSnapshot? PreparedTerrain = null,
    BuildingFloorReference? FloorReference = null,
    bool EstimatedSharedCoordinatesAcknowledged = false,
    BuildingOutlineProvenance? BuildingOutlineContext = null);
