using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Commands;

/// <summary>Opens persistent SolidGround preferences. This command intentionally has no document dependency.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        DialogPalette? palette = null;
        try
        {
            // UIThemeManager.CurrentTheme and UIApplication.MainWindowHandle were verified against the
            // installed Revit 2027 (27.0.10.13) API XML; see revit-usability-settings-and-workflow.md.
            palette = DialogTheme.Resolve(UIThemeManager.CurrentTheme, SystemParameters.HighContrast);
            RevitSettings? current = RevitSettingsIo.LoadForUi(owner: null);
            if (current is null) return Result.Cancelled;
            return RevitSettingsIo.Edit(commandData.Application.MainWindowHandle, current, palette) is null
                ? Result.Cancelled
                : Result.Succeeded;
        }
        catch (UiSettingsRepairRequiredException)
        {
            return RevitSettingsIo.Edit(commandData.Application.MainWindowHandle, current: null, palette!) is null
                ? Result.Cancelled
                : Result.Succeeded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            message = "SolidGround could not open Settings. See the SolidGround log for details.";
            Diagnostics.AddInLog.Error("Could not open SolidGround Settings.", ex);
            TaskDialog dialog = new("SolidGround Settings")
            {
                MainInstruction = "SolidGround could not open Settings.",
                MainContent = message,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
            return Result.Failed;
        }
    }
}
