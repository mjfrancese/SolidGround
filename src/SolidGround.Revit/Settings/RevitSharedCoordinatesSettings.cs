namespace SolidGround.Revit.Settings;

/// <summary>
/// The opt-in shared-coordinates write (SolidGround Issue #30, PH3-3): default off, strict-decoded, expressed
/// as a settings flag until Issue #31's dialog hosts a real checkbox. A new sibling record, not folded into
/// <see cref="RevitTargetSettings"/> (whose own doc comment scopes it specifically to Level/ToposolidType name
/// overrides, a different concern). See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Settings: the shared-coordinates opt-in"
/// section.
/// </summary>
internal sealed record RevitSharedCoordinatesSettings(bool WriteIfAbsent);
