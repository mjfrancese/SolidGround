namespace SolidGround.Revit.Settings;

/// <summary>
/// The Revit-only half of the one flat settings document: optional configured names used as the interactive
/// dialog's own prefill default (SolidGround Issue #31, PH3-4) for its Level/ToposolidType choosers. See
/// SolidGround Issue #15's design record §2.4 row 19 and §4.1's <c>level.name</c>/<c>toposolidType.name</c>
/// rows. A blank or missing name falls back to the locked default-selection rule (§7.3): lowest
/// <c>Level.Elevation</c> for <see cref="LevelName"/>, first by ordinal name for <see cref="ToposolidTypeName"/>
/// -- the operator can still override either pick in the dialog before confirming.
/// </summary>
internal sealed record RevitTargetSettings(string? LevelName, string? ToposolidTypeName);
