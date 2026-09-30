namespace SolidGround.Revit.Dialog;

/// <summary>Hand-maintained inventory of named interactive controls in the three-stage creation dialog.</summary>
internal static class SolidGroundDialogAutomationInventory
{
    // Location: mode + address + latitude + longitude + six Other-area inputs (10); Parcel: two lists (2);
    // Review: level, type, shared-coordinate check (3); shell: Settings, Back, Cancel, and four alternate primary buttons (7).
    internal const int ExpectedControlCount = 22;
}
