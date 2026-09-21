using System.Windows.Media.Imaging;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using SolidGround.Revit.Commands;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit;

/// <summary>
/// Revit 2027 <see cref="IExternalApplication"/> entry point for SolidGround. Creates the dedicated
/// "SolidGround" ribbon tab (owner decision 8, 2026-09-20) and its one <see cref="CreateToposolidCommand"/>
/// button. Implements <see cref="IExternalApplication"/> directly, with no static <c>Instance</c>:
/// <see cref="CreateToposolidCommand"/> is entirely self-contained and never needs to reach back into this
/// class (conventions note section 1).
/// </summary>
/// <remarks>
/// Revit API members used here (namespace-qualified) were verified directly against the installed Revit
/// 2027 SDK (27.0.10.13) for this task, in addition to the citations already recorded by Issue #13's
/// verification note (docs/architecture/revit-2027-verification-and-host-design.md):
/// <see cref="IExternalApplication"/>, <see cref="UIControlledApplication"/>, and
/// <see cref="Result"/> (item 13); <see cref="ControlledApplication.CurrentUserAddinsLocation"/>
/// and <see cref="ControlledApplication.AllUsersAddinsLocation"/> (item 4);
/// <see cref="UIControlledApplication.CreateRibbonTab(string)"/>,
/// <see cref="UIControlledApplication.CreateRibbonPanel(string, string)"/>,
/// <see cref="RibbonPanel.AddItem(RibbonItemData)"/>, and <see cref="PushButtonData"/>'s constructor,
/// <c>ToolTip</c>, <c>LongDescription</c>, <c>Image</c>, and <c>LargeImage</c> members (item 7).
/// <c>Autodesk.Revit.Exceptions.ArgumentException</c> as the documented exception
/// <see cref="UIControlledApplication.CreateRibbonTab(string)"/> throws for a duplicate tab name is this
/// task's own direct reading of RevitAPIUI.xml's doc comments, not previously enumerated by Issue #13.
/// </remarks>
public sealed class SolidGroundApplication : IExternalApplication
{
    private const string TabName = "SolidGround";
    private const string PanelName = "SolidGround";
    private const string CommandName = "CreateToposolidCommand";
    private const string SmallIconResourceName = "SolidGround.Revit.Resources.SolidGround.16.png";
    private const string LargeIconResourceName = "SolidGround.Revit.Resources.SolidGround.32.png";

    private const string ButtonToolTip =
        "Create a native Revit toposolid from a settings-file-configured area of interest, using either a live OpenTopography fetch or a local raster.";

    private const string ButtonLongDescription =
        "Reads %ProgramData%\\SolidGround\\Revit\\settings.json to acquire USGS 1-meter bare-earth elevation " +
        "(live from OpenTopography, or from a local AAIGrid .asc/.prj pair), clips it to the configured area " +
        "of interest, simplifies it to the configured point budget, and creates one native Revit Toposolid " +
        "inside a single transaction that is provably unchanged on any rejected path. The OPENTOPOGRAPHY_API_KEY " +
        "environment variable's value is never read, displayed, or logged. SolidGround is a site-form tool, not " +
        "a survey instrument, and never claims suitability for foundation-perimeter grading.";

    public Result OnStartup(UIControlledApplication application)
    {
        AddInLog.Initialize();

        try
        {
            AddInLog.Info(BuildIdentity.Current.ToLogLine());
            LogAddinsLocations(application);
            CreateRibbon(application);

            return Result.Succeeded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGroundApplication.OnStartup failed; the SolidGround ribbon may be missing or incomplete.", ex);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        AddInLog.Shutdown();
        return Result.Succeeded;
    }

    /// <summary>
    /// Logs, but never hardcodes, both add-ins locations (AGENTS.md "Revit 2027 rules"). A failure here is
    /// swallowed and logged as a warning only: it must never turn a working ribbon into a reported startup
    /// failure.
    /// </summary>
    private static void LogAddinsLocations(UIControlledApplication application)
    {
        try
        {
            ControlledApplication controlled = application.ControlledApplication;
            AddInLog.Info($"CurrentUserAddinsLocation: {controlled.CurrentUserAddinsLocation}");
            AddInLog.Info($"AllUsersAddinsLocation: {controlled.AllUsersAddinsLocation}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Warning($"Could not read one or both add-ins locations: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CreateRibbon(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // RevitAPIUI.xml documents this exact exception for "the tab name duplicates the name of
            // another tab in the Revit UI" -- for example, a repeated OnStartup within the same session
            // during isolated-context testing. Reuse the existing tab rather than failing startup.
            AddInLog.Info($"Ribbon tab '{TabName}' already exists; reusing it.");
        }

        RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
        string assemblyPath = typeof(SolidGroundApplication).Assembly.Location;

        PushButtonData buttonData = new(
            CommandName,
            "Create\nToposolid",
            assemblyPath,
            typeof(CreateToposolidCommand).FullName!)
        {
            ToolTip = ButtonToolTip,
            LongDescription = ButtonLongDescription,
            Image = LoadIcon(SmallIconResourceName),
            LargeImage = LoadIcon(LargeIconResourceName),
        };

        panel.AddItem(buttonData);
    }

    /// <summary>
    /// Loads one of the two embedded ribbon icon PNGs. Non-fatal by contract: a missing or corrupt
    /// resource logs a warning and returns null, which degrades the button to text-only rather than
    /// failing ribbon creation (conventions note section 4, matching the owner's other add-in add-in's own
    /// LoadRibbonIcon contract). <see cref="BitmapCacheOption.OnLoad"/> forces the complete decode while
    /// the manifest-resource stream is still open; the frame is then frozen so it is safe to hand to the
    /// ribbon across threads.
    /// </summary>
    private static BitmapFrame? LoadIcon(string logicalResourceName)
    {
        try
        {
            using Stream? stream = typeof(SolidGroundApplication).Assembly.GetManifestResourceStream(logicalResourceName);
            if (stream is null)
            {
                AddInLog.Warning($"Embedded ribbon icon '{logicalResourceName}' was not found; that button will render without it.");
                return null;
            }

            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return frame;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Warning($"Could not decode ribbon icon '{logicalResourceName}': {ex.GetType().Name}: {ex.Message}. That button will render without it.");
            return null;
        }
    }
}
