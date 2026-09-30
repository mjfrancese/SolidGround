using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Elements;

/// <summary>Offers optional viewing actions after the creation transaction is committed.</summary>
internal static class CreationCompletionPresenter
{
    // RevitAPIUI.xml (27.0.10.13): TaskDialog.AddCommandLink, TaskDialogResult.CommandLink1/2,
    // UIDocument(Document), and UIDocument.ShowElements(ElementId), verified 2026-09-30.
    internal static void Show(Document document, ElementId terrainId, string body, string exportDirectory)
    {
        TaskDialog dialog = new("SolidGround")
        {
            MainInstruction = "SolidGround created the toposolid.",
            MainContent = body + Environment.NewLine + Environment.NewLine + "Save the model using Revit's Save command.",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show terrain", "Frame the created terrain in the current view.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open export folder", "Open the folder containing this run's exports.");
        TaskDialogResult action = dialog.Show();

        try
        {
            if (action == TaskDialogResult.CommandLink1)
            {
                new UIDocument(document).ShowElements(terrainId);
            }
            else if (action == TaskDialogResult.CommandLink2)
            {
                if (!Directory.Exists(exportDirectory))
                {
                    throw new DirectoryNotFoundException("The export folder is no longer available.");
                }

                using Process? process = Process.Start(new ProcessStartInfo(exportDirectory) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // A viewing/file action cannot change the already committed creation outcome.
            AddInLog.Warning($"The model was created, but the requested completion action failed: {ex.GetType().Name}.");
            TaskDialog.Show("SolidGround", "The toposolid was created and remains in the model. " +
                "The requested viewing action could not complete. Find the element by its ID or open the export folder through File Explorer.");
        }
    }
}
