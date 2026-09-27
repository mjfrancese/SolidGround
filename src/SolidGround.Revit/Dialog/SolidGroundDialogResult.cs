using SolidGround.Core.Aois;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// The interactive dialog's final output, constructed only when the operator confirms "Create" on the last
/// step (SolidGround Issue #31, PH3-4: see docs/architecture/revit-interactive-dialog.md "Flow/state model",
/// "Result-code mapping"). Stage D wires this type in (review finding, minor, fixed):
/// <see cref="SolidGroundDialogHost.ShowModal"/> constructs the dialog and returns this record (or
/// <see langword="null"/> on Cancel) to <c>CreateToposolidCommand.ExecuteCore</c>'s Stage 0.5.
/// </summary>
/// <param name="AoiSource">
/// Which of the dialog's two top-level paths produced this result. When <see cref="DialogAoiSource.UseSettingsFile"/>,
/// <paramref name="Aoi"/> is always <see langword="null"/> and <paramref name="AddressParcel"/> is always
/// <see langword="null"/>: the caller is expected to fall back to today's existing settings-driven
/// <c>AoiSettingsFactory.Build</c> path, unchanged, exactly as it already worked before this issue.
/// </param>
/// <param name="Aoi">
/// The resolved area of interest, non-null exactly when <paramref name="AoiSource"/> is
/// <see cref="DialogAoiSource.FindParcel"/>.
/// </param>
internal sealed record SolidGroundDialogResult(
    DialogAoiSource AoiSource,
    AreaOfInterest? Aoi,
    NamedElevationCandidate Level,
    NamedCandidate ToposolidType,
    LengthUnit OutputUnit,
    int PointBudget,
    bool WriteSharedCoordinatesIfAbsent,
    AddressParcelProvenance? AddressParcel);
