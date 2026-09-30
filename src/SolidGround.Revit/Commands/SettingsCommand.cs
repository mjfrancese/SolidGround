using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Commands;

/// <summary>Opens persistent SolidGround preferences. This command intentionally has no document dependency.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            RevitSettings current = RevitSettingsIo.LoadForUi(owner: null);
            RevitSettingsIo.Edit(owner: null, current);
            return Result.Succeeded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            message = "SolidGround could not open Settings. See the SolidGround log for details.";
            Diagnostics.AddInLog.Error("Could not open SolidGround Settings.", ex);
            return Result.Failed;
        }
    }
}
