namespace SolidGround.Cli;

/// <summary>
/// The CLI's stable, documented process exit codes. See docs/architecture/cli-workflow.md's "Exit codes and
/// error classes" section for the full mapping from each exception type this process can observe to exactly
/// one of these codes.
/// </summary>
public static class CliExitCodes
{
    public const int Success = 0;
    public const int UnexpectedError = 1;
    public const int Usage = 2;
    public const int Authorization = 3;
    public const int SourceQuality = 4;
    public const int Processing = 5;

    /// <summary>
    /// The input was well-formed and the source responded correctly, but nothing matched: <c>geocode</c>'s
    /// provider reported zero candidates, or <c>parcel</c>'s source resolved zero candidates for the given
    /// point/address. See docs/architecture/cli-workflow.md's "Exit codes and error classes" section for why
    /// this is a new code rather than reusing <see cref="SourceQuality"/>.
    /// </summary>
    public const int NotFound = 6;

    public const int Cancelled = 130;
}
