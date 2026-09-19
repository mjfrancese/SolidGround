namespace SolidGround.Cli;

/// <summary>
/// Thrown for CLI-detected conditions that are not a Core exception type but are conceptually a processing
/// failure: a <c>run</c>/<c>fetch</c> transform-versus-grid reference mismatch, and any
/// <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> the CLI itself raises while writing a
/// raster set or creating its output directory (the raster set has no Core-owned exporter). Always maps to
/// <see cref="CliExitCodes.Processing"/>. See docs/architecture/cli-workflow.md's "Exit codes and error
/// classes" section for the full mapping this exception type participates in, and its "Raster set
/// persistence" section for why raster-set writes need their own exception type here rather than reusing a
/// Core one.
/// </summary>
public sealed class CliProcessingException : Exception
{
    public CliProcessingException(string message) : base(message) { }

    public CliProcessingException(string message, Exception innerException) : base(message, innerException) { }
}
