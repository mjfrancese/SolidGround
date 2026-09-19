using System.Reflection;
using SolidGround.Cli.Commands;
using SolidGround.Cli.Options;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Transformations;

namespace SolidGround.Cli;

/// <summary>
/// The CLI's single public in-process entry point. See docs/architecture/cli-workflow.md's "Exit codes and
/// error classes" section for the full exception-to-exit-code mapping <see cref="RunAsync"/>'s outermost
/// catch chain implements below, in the documented order, and its "Testing strategy" section for why every
/// test calls this method directly instead of shelling out to a built executable.
/// </summary>
public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(host);

        try
        {
            int? globalResult = TryHandleGlobalRequest(args, host);
            if (globalResult is int exitCode)
            {
                return exitCode;
            }

            ParsedInvocation invocation = ParsedInvocation.Parse(args);
            return await DispatchAsync(invocation, host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Never ex.Message: a caller-attached handler could embed request text there. See
            // docs/architecture/cli-workflow.md's "Exit codes and error classes" section.
            host.StandardError.WriteLine("error (cancelled): the operation was cancelled.");
            return CliExitCodes.Cancelled;
        }
        catch (CliUsageException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (OpenTopographyAuthorizationException ex)
        {
            host.StandardError.WriteLine($"error (authorization): {ex.Message}");
            return CliExitCodes.Authorization;
        }
        catch (OpenTopographyRequestValidationException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (OpenTopographyException ex)
        {
            host.StandardError.WriteLine($"error (source-quality): {ex.Message}");
            return CliExitCodes.SourceQuality;
        }
        catch (ParcelGeometryException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (HorizontalCoordinateTransformException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (AoiNormalizationException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (GridClipException ex)
        {
            host.StandardError.WriteLine($"error (source-quality): {ex.Message}");
            return CliExitCodes.SourceQuality;
        }
        catch (TerrainSimplificationException ex)
        {
            host.StandardError.WriteLine($"error (processing): {ex.Message}");
            return CliExitCodes.Processing;
        }
        catch (TerrainProvenanceException ex)
        {
            host.StandardError.WriteLine($"error (source-quality): {ex.Message}");
            return CliExitCodes.SourceQuality;
        }
        catch (TerrainExportException ex)
        {
            host.StandardError.WriteLine($"error (processing): {ex.Message}");
            return CliExitCodes.Processing;
        }
        catch (CliProcessingException ex)
        {
            host.StandardError.WriteLine($"error (processing): {ex.Message}");
            return CliExitCodes.Processing;
        }
        catch (FormatException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (ArgumentException ex)
        {
            // Defensive: ArgumentOutOfRangeException and ArgumentNullException both derive from
            // ArgumentException, and every option is already validated as a CliUsageException before this
            // should ever fire.
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return CliExitCodes.Usage;
        }
        catch (Exception ex)
        {
            host.StandardError.WriteLine($"error (unexpected): {ex.Message}");
            if (WasVerboseRequested(args))
            {
                host.StandardError.WriteLine(ex.ToString());
            }

            return CliExitCodes.UnexpectedError;
        }
    }

    /// <summary>
    /// Raw-scans <paramref name="args"/> for <c>--version</c>, <c>--help</c>/<c>-h</c>, and the <c>help</c>
    /// pseudo-command before <see cref="ParsedInvocation.Parse"/> ever runs, so a help or version request
    /// always succeeds even when another option elsewhere in the same invocation is unknown or malformed.
    /// See docs/architecture/cli-workflow.md's "Options and defaults" section.
    /// </summary>
    private static int? TryHandleGlobalRequest(string[] args, CliHost host)
    {
        if (args.Length == 0)
        {
            host.StandardError.WriteLine("error (usage): no command was given.");
            host.StandardOutput.Write(HelpText.RenderGlobal());
            return CliExitCodes.Usage;
        }

        if (Array.IndexOf(args, "--version") >= 0)
        {
            if (args.Length != 1)
            {
                throw new CliUsageException("--version must be the only argument.");
            }

            host.StandardOutput.WriteLine(VersionText());
            return CliExitCodes.Success;
        }

        if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
        {
            // The candidate verb is whichever token is NOT the --help/-h flag itself: args[1] when the flag
            // leads (`--help bogus`), otherwise args[0] (`bogus --help`). An explicit, unknown candidate is a
            // usage error naming it, exactly like `help <verb>` below and unlike a bare `--help`/`-h`, which
            // has no candidate at all and always renders global help. See docs/architecture/cli-workflow.md's
            // "Options and defaults" section.
            bool firstArgIsHelpFlag = string.Equals(args[0], "--help", StringComparison.Ordinal) || string.Equals(args[0], "-h", StringComparison.Ordinal);
            string? candidateVerb = firstArgIsHelpFlag ? (args.Length > 1 ? args[1] : null) : args[0];
            if (candidateVerb is null)
            {
                host.StandardOutput.Write(HelpText.RenderGlobal());
                return CliExitCodes.Success;
            }

            if (OptionTable.TryGetVerb(candidateVerb) is null)
            {
                // Never echo the supplied positional token here -- see docs/architecture/cli-workflow.md's
                // "Diagnostics and redaction" section.
                throw new CliUsageException("unknown command; run 'help' to list the commands.");
            }

            host.StandardOutput.Write(HelpText.RenderCommand(candidateVerb));
            return CliExitCodes.Success;
        }

        if (string.Equals(args[0], "help", StringComparison.Ordinal))
        {
            if (args.Length == 1)
            {
                host.StandardOutput.Write(HelpText.RenderGlobal());
                return CliExitCodes.Success;
            }

            if (args.Length == 2)
            {
                host.StandardOutput.Write(HelpText.RenderCommand(args[1]));
                return CliExitCodes.Success;
            }

            throw new CliUsageException("help accepts at most one command name.");
        }

        return null;
    }

    private static Task<int> DispatchAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken) => invocation.Verb switch
    {
        OptionTable.Process => ProcessCommand.RunAsync(invocation, host, cancellationToken),
        OptionTable.Fetch => FetchCommand.RunAsync(invocation, host, cancellationToken),
        OptionTable.Run => RunCommand.RunAsync(invocation, host, cancellationToken),
        OptionTable.Verify => VerifyCommand.RunAsync(invocation, host, cancellationToken),
        _ => throw new InvalidOperationException($"Unhandled verb '{invocation.Verb}'."),
    };

    private static string VersionText()
    {
        string? informationalVersion = typeof(CliApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informationalVersion ?? typeof(CliApplication).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        return $"SolidGround CLI {version}";
    }

    private static bool WasVerboseRequested(string[] args) => Array.IndexOf(args, "--verbose") >= 0;
}
