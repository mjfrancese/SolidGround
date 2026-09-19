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
    public const int Cancelled = 130;
}
