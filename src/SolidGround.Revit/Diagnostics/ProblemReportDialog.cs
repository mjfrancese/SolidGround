using System.Globalization;
using System.Text;

namespace SolidGround.Revit.Diagnostics;

/// <summary>
/// Bounded presentation for a list of Preflight (or other) rejection reasons.
/// </summary>
/// <remarks>
/// A Revit <see cref="Autodesk.Revit.UI.TaskDialog"/> auto-sizes to its content and does not scroll, so an
/// unbounded problem list can grow the dialog past the screen and take its own close button with it
/// (a lesson learned from a related internal project's own incident, adopted here as the reason for a
/// hard cap). This caps the
/// dialog body at <see cref="MaxInlineProblems"/> lines and always writes the complete list to a file in
/// the log folder, naming that file's path in the returned text, so a long list is capped in the dialog but
/// never actually lost (AGENTS.md "Revit add-in conventions" section 6).
/// </remarks>
internal static class ProblemReportDialog
{
    internal const int MaxInlineProblems = 8;

    /// <summary>
    /// Builds the full <c>TaskDialog</c> body for a rejection: <paramref name="headline"/> followed by every
    /// problem when there are <see cref="MaxInlineProblems"/> or fewer, otherwise the first
    /// <see cref="MaxInlineProblems"/> plus a count of how many were omitted. Whenever there is at least one
    /// problem, the complete list is also written to a timestamped file under
    /// <paramref name="logDirectory"/> (or its absence is stated instead, if there is no log directory or
    /// the write failed), and that path is always named in the returned text.
    /// </summary>
    internal static string BuildRejectionBody(string headline, IReadOnlyList<string> problems, string? logDirectory)
    {
        ArgumentNullException.ThrowIfNull(headline);
        ArgumentNullException.ThrowIfNull(problems);

        if (problems.Count == 0)
        {
            return headline;
        }

        string? reportPath = TryWriteFullReport(headline, problems, logDirectory);

        StringBuilder body = new();
        body.AppendLine(headline);
        body.AppendLine();

        int shown = Math.Min(MaxInlineProblems, problems.Count);
        for (int index = 0; index < shown; index++)
        {
            body.Append("- ").AppendLine(problems[index]);
        }

        int omitted = problems.Count - shown;
        if (omitted > 0)
        {
            body.AppendLine(string.Create(CultureInfo.InvariantCulture, $"... and {omitted} more."));
        }

        body.AppendLine();
        body.AppendLine(reportPath is not null
            ? $"Complete list written to: {reportPath}"
            : "The complete list could not be written to the log folder.");

        return body.ToString().TrimEnd();
    }

    private static string? TryWriteFullReport(string headline, IReadOnlyList<string> problems, string? logDirectory)
    {
        if (string.IsNullOrEmpty(logDirectory))
        {
            return null;
        }

        try
        {
            string path = Path.Combine(
                logDirectory,
                string.Create(CultureInfo.InvariantCulture, $"SolidGround.Revit-preflight-{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ss}.txt"));

            StringBuilder text = new();
            text.AppendLine(headline);
            text.AppendLine();
            for (int index = 0; index < problems.Count; index++)
            {
                text.Append(index + 1).Append(". ").AppendLine(problems[index]);
            }

            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (Exception ex)
        {
            AddInLog.Error("Could not write the full preflight problem report to disk.", ex);
            return null;
        }
    }
}
