namespace SolidGround.Cli;

/// <summary>
/// Thrown by argument parsing and validation, and by the CLI's own strict readers (the raster-source
/// sidecar and the user-secrets file). Always maps to <see cref="CliExitCodes.Usage"/>. See
/// docs/architecture/cli-workflow.md's "Exit codes and error classes" section for the full mapping this
/// exception type participates in.
/// </summary>
public sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }

    public CliUsageException(string message, Exception innerException) : base(message, innerException) { }
}
