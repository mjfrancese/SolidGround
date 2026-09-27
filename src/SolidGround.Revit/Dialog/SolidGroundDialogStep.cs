namespace SolidGround.Revit.Dialog;

/// <summary>
/// The interactive dialog's content-model sections, in fixed display order (SolidGround Issue #31, PH3-4: see
/// docs/architecture/revit-interactive-dialog.md "Content model and sections", "Flow/state model"). Drives
/// which panel <see cref="SolidGroundDialog"/>'s content area shows; <see cref="SolidGroundDialogViewModel.CurrentStep"/>
/// is bound to this. <see cref="AoiSourceChoice"/> is owner decision 1's own addition ahead of the originally
/// designed ten sections (a new step 0, not renumbering any of the other ten); every other member below
/// matches that design's own numbered list exactly.
/// </summary>
internal enum SolidGroundDialogStep
{
    /// <summary>Step 0 (owner decision 1): <see cref="DialogAoiSource"/>.</summary>
    AoiSourceChoice,

    /// <summary>Step 1: address entry (a street address, or a "latitude, longitude" pair).</summary>
    AddressEntry,

    /// <summary>Step 2: geocode candidates.</summary>
    GeocodeCandidates,

    /// <summary>Step 3: parcel candidates with legal-description preview.</summary>
    ParcelCandidates,

    /// <summary>Step 4: AOI buffer.</summary>
    Buffer,

    /// <summary>Step 5: point budget (<c>Revit.ini</c> guard warning).</summary>
    PointBudget,

    /// <summary>Step 6: unit choice.</summary>
    UnitChoice,

    /// <summary>Step 7: level/toposolid-type.</summary>
    LevelAndToposolidType,

    /// <summary>Step 8: shared-coordinates opt-in checkbox.</summary>
    SharedCoordinatesOptIn,

    /// <summary>Step 9: provenance/accuracy preview.</summary>
    ProvenancePreview,

    /// <summary>Step 10: Preflight summary with Create/Cancel.</summary>
    PreflightSummary,
}

/// <summary>
/// The dialog's very first choice (SolidGround Issue #31, PH3-4, owner decision 1, implemented with its
/// recommended default): whether this run's area of
/// interest comes from an interactive parcel lookup (<see cref="FindParcel"/>), or from today's
/// settings-driven bounding-box/radius/parcel configuration (<see cref="UseSettingsFile"/>, unchanged from
/// before this issue). Kept as its own small, top-level enum -- not folded into a larger flag or state type,
/// and consulted from exactly one place, <see cref="SolidGroundDialogViewModel.ActiveStepOrder"/> -- so this
/// one choice stays easy to find and easy to change. See docs/architecture/revit-interactive-dialog.md
/// "Content model and sections" step 0 and "AOI and provenance".
/// </summary>
internal enum DialogAoiSource
{
    /// <summary>Steps 1-4 (address entry through buffer) run; the dialog resolves a <c>ParcelGeometryAoi</c> itself.</summary>
    FindParcel,

    /// <summary>Steps 1-4 are skipped entirely; the run's area of interest comes from <c>settings.json</c>'s <c>areaOfInterest</c> section, exactly as it did before this issue.</summary>
    UseSettingsFile,
}
