namespace SolidGround.Revit.Settings;

/// <summary>
/// The opt-in shared-coordinates write (SolidGround Issue #30, PH3-3): default off, strict-decoded. Read as the
/// interactive dialog's own checkbox prefill default (SolidGround Issue #31, PH3-4); the operator's own checkbox
/// state for the run about to happen always wins, with no write-back to this settings value. A new sibling
/// record, not folded into <see cref="RevitTargetSettings"/> (whose own doc comment scopes it specifically to
/// Level/ToposolidType name overrides, a different concern). See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Settings: the shared-coordinates opt-in"
/// section.
/// </summary>
internal sealed record RevitSharedCoordinatesSettings(bool WriteIfAbsent);
