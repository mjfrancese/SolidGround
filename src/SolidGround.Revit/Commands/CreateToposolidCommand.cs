using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Commands;

/// <summary>
/// The one SolidGround ribbon command for this milestone (Issue #14).
/// </summary>
/// <remarks>
/// <para>
/// Runs a read-only Preflight over the active document and reports the result in exactly one
/// <see cref="TaskDialog"/>; it never opens a <see cref="Transaction"/> and never creates a toposolid
/// (Issue #15 does that). SolidGround is a site-form tool, not a survey instrument.
/// </para>
/// <para>
/// <c>message</c> is deliberately left at its caller-provided empty value on every return path: Revit only
/// shows its own automatic result dialog when <c>message</c> is non-empty, so leaving it empty and always
/// showing exactly one dialog constructed here is what keeps every outcome to a single dialog.
/// </para>
/// <para>
/// Revit API members used here (namespace-qualified), verified directly against the installed Revit 2027
/// SDK (27.0.10.13) for this task: <see cref="IExternalCommand"/> and its <c>Execute(ExternalCommandData,
/// ref string, ElementSet)</c> signature, confirmed both by Issue #13 item 12/13 and by this task's own
/// local compile experiment against the real installed RevitAPIUI.dll (a plain, non-ref
/// <c>ElementSet elements</c> parameter compiles with zero errors); <see cref="TransactionAttribute"/>/
/// <see cref="TransactionMode"/> and <see cref="RegenerationAttribute"/>/<see cref="RegenerationOption"/>
/// (item 10); <see cref="Result"/> (items 2, 13); <c>ExternalCommandData.Application</c> to
/// <c>UIApplication.ActiveUIDocument</c> to <c>UIDocument.Document</c>, and
/// <c>Document.IsFamilyDocument</c> (item 15/16 "Level and type selection rule": "a confirmed installed-sdk
/// member"); <see cref="TaskDialog"/>'s constructor, <c>MainInstruction</c>, <c>MainContent</c>, and
/// <c>Show()</c> members (item 13).
/// </para>
/// </remarks>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CreateToposolidCommand : IExternalCommand
{
    private const string DialogTitle = "SolidGround";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            PreflightResult preflight = RunPreflight(commandData);
            AddInLog.Info($"CreateToposolidCommand Preflight completed with {preflight.Problems.Count} problem(s).");

            if (preflight.Problems.Count > 0)
            {
                ShowRejection(preflight);
                // A read-only Preflight opened no Transaction. Returning Cancelled, not Failed, avoids
                // clearing Revit's native Undo stack over a check that changed nothing (conventions note
                // section 4; verification note item 2, still a runtime-only open question).
                return Result.Cancelled;
            }

            ShowSuccess(preflight);
            return Result.Succeeded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("CreateToposolidCommand.Execute failed unexpectedly.", ex);
            TaskDialog.Show(
                DialogTitle,
                "SolidGround hit an unexpected problem and stopped. Nothing in the model changed." +
                Environment.NewLine + Environment.NewLine + ex.Message);
            // No Transaction was opened and the document is unchanged, so this returns Cancelled per
            // conventions section 4, not Failed. Failed is reserved for a document actually left in a bad
            // state; Issue #15's transactional code will need to decide that per path once it opens one.
            return Result.Cancelled;
        }
    }

    private static void ShowRejection(PreflightResult preflight)
    {
        string body = ProblemReportDialog.BuildRejectionBody(
            "Nothing changed. Correct every problem below and run this command again.",
            preflight.Problems,
            AddInLog.LogDirectory);

        AddInLog.Info("Showing Preflight rejection dialog.");
        TaskDialog dialog = new(DialogTitle)
        {
            MainInstruction = "SolidGround Preflight found a problem.",
            MainContent = body,
        };
        dialog.Show();
    }

    private static void ShowSuccess(PreflightResult preflight)
    {
        AddInLog.Info("Showing Preflight success dialog.");
        TaskDialog dialog = new(DialogTitle)
        {
            MainInstruction = "SolidGround Preflight passed.",
            MainContent = BuildSuccessBody(preflight),
        };
        dialog.Show();
    }

    /// <summary>
    /// Read-only by construction: reads document state and Core-level defaults only, opens no
    /// <see cref="Transaction"/>, and never reads the OpenTopography API key's value into any string --
    /// only whether <see cref="IOpenTopographyApiKeyProvider.GetApiKey"/> returned a non-null key at all.
    /// </summary>
    private static PreflightResult RunPreflight(ExternalCommandData commandData)
    {
        List<string> problems = [];

        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            problems.Add("No active Revit project is open. Open a project document and run this command again.");
        }
        else if (document.IsFamilyDocument)
        {
            problems.Add("The active document is a family document. Open a project document and run this command again.");
        }

        EnvironmentOpenTopographyApiKeyProvider keyProvider = new();
        if (keyProvider.GetApiKey() is null)
        {
            problems.Add("The OPENTOPOGRAPHY_API_KEY environment variable is not set (or is empty). Set it to a valid OpenTopography API key and restart Revit.");
        }

        SimplificationRequest defaultSimplification = new();
        return new PreflightResult(problems, defaultSimplification.PointBudget, LengthConverter.DefaultOutputUnit, document?.Title);
    }

    private static string BuildSuccessBody(PreflightResult preflight) => string.Join(
        Environment.NewLine,
        $"Project: {preflight.DocumentTitle}",
        "OpenTopography API key: present. (This check never reads, displays, or logs the key's value.)",
        $"Default point budget: {preflight.PointBudget:N0} points.",
        $"Default output unit: {DescribeUnit(preflight.OutputUnit)}.",
        string.Empty,
        "Creating a toposolid from a parcel or other area of interest arrives with a later SolidGround " +
        "milestone (Issue #15). SolidGround is a site-form tool, not a survey instrument, and this check " +
        "did not modify the model.");

    private static string DescribeUnit(LengthUnit unit) => unit switch
    {
        LengthUnit.UsSurveyFoot => "U.S. survey foot",
        LengthUnit.InternationalFoot => "international foot",
        LengthUnit.Meter => "meter",
        _ => unit.ToString(),
    };

    private sealed record PreflightResult(
        IReadOnlyList<string> Problems,
        int PointBudget,
        LengthUnit OutputUnit,
        string? DocumentTitle);
}
