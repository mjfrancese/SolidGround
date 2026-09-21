using Autodesk.Revit.DB;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// Accumulates every <see cref="FailureMessageAccessor"/> line <see cref="ToposolidCreationFailurePreprocessor"/>
/// observes, plus whether any of them was blocking-severity (<see cref="FailureSeverity.Error"/> or
/// <see cref="FailureSeverity.DocumentCorruption"/>), so the command can check that flag itself after
/// <c>Regenerate()</c>/<c>Commit()</c> return, on top of the preprocessor's own <see cref="FailureProcessingResult.ProceedWithRollBack"/>
/// signal (design record §6.5).
/// </summary>
internal sealed class ToposolidCreationFailureLog
{
    private readonly List<string> messages = [];

    internal IReadOnlyList<string> Messages => messages;

    internal bool HasBlockingFailure { get; private set; }

    internal void Add(string message, bool isBlocking)
    {
        ArgumentNullException.ThrowIfNull(message);
        messages.Add(message);
        if (isBlocking)
        {
            HasBlockingFailure = true;
        }
    }
}

/// <summary>
/// Logs every <see cref="FailureMessageAccessor"/> Revit reports during the toposolid-creation transaction
/// (severity, description, and -- best effort -- its <see cref="FailureDefinitionId"/>) into both
/// <see cref="AddInLog"/> and a caller-supplied <see cref="ToposolidCreationFailureLog"/> the command later
/// folds into a rejection dialog. Requests rollback on any <see cref="FailureSeverity.Error"/> or
/// <see cref="FailureSeverity.DocumentCorruption"/> message; otherwise deletes every warning and lets the
/// transaction continue. Never throws back into Revit: a defensive catch forces
/// <see cref="FailureProcessingResult.ProceedWithRollBack"/> on its own internal failure. See SolidGround
/// Issue #15's design record §2.4 row 26. Every member used here is verified against
/// <c>apidump/out/Autodesk.Revit.DB.{IFailuresPreprocessor,FailuresAccessor,FailureMessageAccessor,
/// FailureSeverity,FailureProcessingResult}.txt</c>.
/// </summary>
internal sealed class ToposolidCreationFailurePreprocessor : IFailuresPreprocessor
{
    private readonly ToposolidCreationFailureLog log;

    internal ToposolidCreationFailurePreprocessor(ToposolidCreationFailureLog log)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        try
        {
            IList<FailureMessageAccessor> messages = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor message in messages)
            {
                FailureSeverity severity = message.GetSeverity();
                string description = message.GetDescriptionText();
                // FailureDefinitionId.Guid is not confirmed against the installed dump (GuidEnum itself was
                // never dumped this session), so this logs the object's own ToString() rather than assuming
                // a member this record cannot verify.
                string definitionId = message.GetFailureDefinitionId()?.ToString() ?? "(unknown)";
                string line = $"[{severity}] {description} ({definitionId})";

                bool isBlocking = severity is FailureSeverity.Error or FailureSeverity.DocumentCorruption;
                log.Add(line, isBlocking);
                AddInLog.Warning($"Toposolid creation failure message: {line}");
            }

            if (log.HasBlockingFailure)
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }

            foreach (FailureMessageAccessor message in messages)
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(message);
                }
            }

            return FailureProcessingResult.Continue;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("ToposolidCreationFailurePreprocessor failed while processing Revit failure messages; forcing rollback.", ex);
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
