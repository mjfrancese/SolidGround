using System.Diagnostics;

namespace SolidGround.Revit.Diagnostics;

/// <summary>
/// Process-wide <see cref="Trace"/> listener wiring for SolidGround.Revit.
/// </summary>
/// <remarks>
/// <para>
/// Log location is machine-wide, under <c>%ProgramData%\SolidGround\Revit\Logs\</c>, built from
/// <see cref="Environment.SpecialFolder.CommonApplicationData"/>, never a literal path (AGENTS.md "Revit
/// add-in conventions" section 5; conventions note section 6). If that folder cannot be created (for
/// example, a locked-down machine where the current user cannot write to ProgramData), this falls back to
/// the per-user <c>%LOCALAPPDATA%\SolidGround\Revit\Logs\</c> folder and logs that it did so; the add-in
/// itself still installs per-user regardless of where its logs end up.
/// </para>
/// <para>
/// Every member here swallows its own exceptions and never throws back into Revit's pipeline (AGENTS.md
/// "Revit add-in conventions" section 6: "Diagnostics must never throw back into Revit."). Any URL or
/// query string a caller logs through this class must already be redacted through
/// <see cref="SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction"/> before it reaches these
/// methods; this class does not redact on the caller's behalf.
/// </para>
/// </remarks>
internal static class AddInLog
{
    private const string LogFileNamePrefix = "SolidGround.Revit-";
    private const string TraceListenerName = "SolidGround.Revit";

    private static readonly object Gate = new();
    private static TextWriterTraceListener? listener;

    /// <summary>The directory the active listener writes to, or null when initialization failed or has not run.</summary>
    internal static string? LogDirectory { get; private set; }

    /// <summary>
    /// Idempotent. Creates the log directory (falling back to the per-user location if needed) and attaches
    /// a daily-file <see cref="TextWriterTraceListener"/>. Safe to call from <c>OnStartup</c>; does nothing
    /// if a listener is already attached, and leaves tracing silently disabled for the session if neither
    /// candidate directory could be created.
    /// </summary>
    internal static void Initialize()
    {
        lock (Gate)
        {
            if (listener is not null)
            {
                return;
            }

            try
            {
                string? directory = TryCreateLogDirectory(out bool usedFallback);
                if (directory is null)
                {
                    return;
                }

                string path = Path.Combine(directory, $"{LogFileNamePrefix}{DateTime.UtcNow:yyyy-MM-dd}.log");
                FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                StreamWriter writer = new(stream) { AutoFlush = true };
                listener = new TextWriterTraceListener(writer, TraceListenerName);
                Trace.Listeners.Add(listener);
                Trace.AutoFlush = true;
                LogDirectory = directory;

                if (usedFallback)
                {
                    Warning($"Machine-wide log folder was not writable; logging to the per-user fallback at '{directory}' instead.");
                }

                Info("SolidGround.Revit diagnostics initialized.");
            }
            catch (Exception ex)
            {
                // Initialization must never throw back into Revit's OnStartup. Leave tracing disabled.
                Debug.WriteLine($"SolidGround.Revit: AddInLog.Initialize failed: {ex}");
                listener = null;
                LogDirectory = null;
            }
        }
    }

    /// <summary>Flushes and detaches the listener. Safe to call from <c>OnShutdown</c>; never throws.</summary>
    internal static void Shutdown()
    {
        lock (Gate)
        {
            try
            {
                if (listener is not null)
                {
                    Info("SolidGround.Revit diagnostics shutting down.");
                    Trace.Listeners.Remove(listener);
                    listener.Flush();
                    listener.Close();
                    listener.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SolidGround.Revit: AddInLog.Shutdown failed: {ex}");
            }
            finally
            {
                listener = null;
                LogDirectory = null;
            }
        }
    }

    internal static void Info(string message) => TraceSafely(() => Trace.TraceInformation(Prefix(message)));

    internal static void Warning(string message) => TraceSafely(() => Trace.TraceWarning(Prefix(message)));

    internal static void Error(string message) => TraceSafely(() => Trace.TraceError(Prefix(message)));

    internal static void Error(string message, Exception exception) =>
        TraceSafely(() => Trace.TraceError(Prefix($"{message} {exception.GetType().Name}: {exception.Message}")));

    private static string Prefix(string message) => $"[{DateTime.UtcNow:O}] {message}";

    private static void TraceSafely(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SolidGround.Revit: logging call failed: {ex}");
        }
    }

    private static string? TryCreateLogDirectory(out bool usedFallback)
    {
        usedFallback = false;
        string machineWide = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SolidGround",
            "Revit",
            "Logs");
        if (TryEnsureDirectory(machineWide))
        {
            return machineWide;
        }

        usedFallback = true;
        string perUser = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolidGround",
            "Revit",
            "Logs");
        return TryEnsureDirectory(perUser) ? perUser : null;
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
