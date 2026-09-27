using CommunityToolkit.Mvvm.ComponentModel;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// View-model shell for the interactive Revit-host dialog (SolidGround Issue #31, PH3-4:
/// docs/architecture/revit-interactive-dialog.md). Deliberately minimal in this stage: a plain
/// <see cref="ObservableObject"/> subclass (never <c>ObservableRecipient</c>; no
/// <c>IMessenger</c>/<c>WeakReferenceMessenger</c> anywhere in this feature -- see
/// docs/architecture/revit-interactive-dialog.md "MVVM shape (and why no messenger)") carrying a single
/// generator-backed placeholder property, whose only purpose is to prove that
/// <c>CommunityToolkit.Mvvm</c>'s <c>[ObservableProperty]</c> source generator actually runs against this
/// project's real <c>net10.0-windows7.0</c>/<c>UseWPF=true</c> build (docs/architecture/revit-interactive-dialog.md
/// "Package: CommunityToolkit.Mvvm 8.4.2"). The full content model -- address/geocode, parcel, buffer,
/// point budget, unit, level/toposolid-type, shared-coordinates opt-in, provenance preview, and Preflight
/// summary, each with its own bound properties and relay commands -- and the synchronous network bridge are
/// a later stage's own scope, not this one (docs/architecture/revit-interactive-dialog.md "Content model
/// and sections", "Threading and the network bridge").
/// </summary>
/// <remarks>
/// This type is never constructed except by <see cref="SolidGroundDialog"/>'s own shell constructor. It is
/// not reachable from <c>CreateToposolidCommand</c>, the ribbon, or any other command in this stage.
/// </remarks>
internal sealed partial class SolidGroundDialogViewModel : ObservableObject
{
    /// <summary>
    /// Stage B placeholder only. A later stage's real content model (docs/architecture/revit-interactive-dialog.md
    /// "Content model and sections") replaces this with the full set of bound properties named in that
    /// section; nothing in this codebase reads or writes this property today.
    /// </summary>
    [ObservableProperty]
    private string? _placeholderStatusText;
}
