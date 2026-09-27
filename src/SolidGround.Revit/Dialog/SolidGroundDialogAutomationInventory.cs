namespace SolidGround.Revit.Dialog;

/// <summary>
/// Hand-maintained inventory of this dialog's interactive controls (SolidGround Issue #31, PH3-4: see
/// docs/architecture/revit-interactive-dialog.md "Tests"). <see cref="ExpectedControlCount"/> is bumped by
/// hand -- and the matching accessibility name call added or removed in <see cref="SolidGroundDialog"/> --
/// exactly when an interactive control is added to or removed from this dialog.
/// <c>RevitInteractiveDialogTests.SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl</c>
/// asserts these two numbers stay equal by reading both directly from source text, so neither can silently
/// drift from the other. (That test's own literal call-site text is deliberately never quoted verbatim in
/// this doc comment: this file lives under the same <c>Dialog/*.cs</c> glob that check scans, so quoting it
/// here would inflate the very count being asserted.)
/// </summary>
internal static class SolidGroundDialogAutomationInventory
{
    /// <summary>
    /// The exact number of interactive controls (a control the operator can type into, click, check, or
    /// choose from -- never a read-only <c>TextBlock</c>) this dialog declares across every step:
    /// <list type="number">
    /// <item>Step 0 (AOI source choice): 2 <c>RadioButton</c>s ("Find a parcel" / "Use the area in the settings file").</item>
    /// <item>Step 1 (address entry): 1 <c>TextBox</c> + 1 "Find" <c>Button</c>.</item>
    /// <item>Step 2 (geocode candidates): 1 <c>ListBox</c>.</item>
    /// <item>Step 3 (parcel candidates): 1 "Find parcel" <c>Button</c> + 1 <c>ListBox</c>.</item>
    /// <item>Step 4 (buffer): 1 <c>TextBox</c>.</item>
    /// <item>Step 5 (point budget): 1 <c>TextBox</c>.</item>
    /// <item>Step 6 (unit choice): 2 <c>RadioButton</c>s.</item>
    /// <item>Step 7 (level/toposolid type): 2 <c>ComboBox</c>es.</item>
    /// <item>Step 8 (shared-coordinates opt-in): 1 <c>CheckBox</c>.</item>
    /// <item>Step 9 (provenance preview): none (read-only).</item>
    /// <item>Step 10 (Preflight summary): none of its own (read-only recap; Create/Cancel live in the persistent navigation bar below).</item>
    /// <item>Persistent navigation bar (every step): Cancel, Back, Next, and Create <c>Button</c>s (4).</item>
    /// </list>
    /// Total: 2 + 2 + 1 + 2 + 1 + 1 + 2 + 2 + 1 + 4 = 18.
    /// </summary>
    internal const int ExpectedControlCount = 18;
}
