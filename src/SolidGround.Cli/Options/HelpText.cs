using System.Text;

namespace SolidGround.Cli.Options;

/// <summary>
/// Renders deterministic help text from <see cref="OptionTable"/>, so a command's own usage text can never
/// drift from what it actually accepts. See docs/architecture/cli-workflow.md's "Commands" section for the
/// four verbs listed in <see cref="RenderGlobal"/>, and its "Options and defaults" section for the per-option
/// text -- including the <c>--unit</c> and <c>--origin</c> sentences, reproduced there verbatim -- that
/// <see cref="RenderCommand"/> prints for each option in <see cref="OptionTable"/>'s fixed, deterministic
/// order. See its "Determinism" section for why this type builds output only from that fixed order and never
/// enumerates a hash-based collection, so calling either method twice renders byte-identical text.
/// </summary>
internal static class HelpText
{
    private const string InvocationPrefix = "dotnet run --project src/SolidGround.Cli --configuration Release --";

    /// <summary>The top-level help text: every command's name and summary, plus the global options.</summary>
    internal static string RenderGlobal()
    {
        StringBuilder builder = new();
        builder.Append("SolidGround CLI\n\n");
        builder.Append("Commands:\n");
        foreach (VerbSpec verb in OptionTable.Verbs)
        {
            builder.Append("  ").Append(verb.Name).Append(" - ").Append(verb.Summary).Append('\n');
        }

        builder.Append("\nRun \"help <command>\" for one command's own options.\n\n");
        AppendGlobalOptions(builder);
        return builder.ToString();
    }

    /// <summary>One command's usage line, its own options, and the global options.</summary>
    /// <exception cref="CliUsageException"><paramref name="verb"/> names no command.</exception>
    internal static string RenderCommand(string verb)
    {
        VerbSpec? spec = OptionTable.TryGetVerb(verb);
        if (spec is null)
        {
            // Never echo the supplied positional token here -- see docs/architecture/cli-workflow.md's
            // "Diagnostics and redaction" section.
            throw new CliUsageException("unknown command; run 'help' to list the commands.");
        }

        StringBuilder builder = new();
        builder.Append(spec.Name).Append(" - ").Append(spec.Summary).Append('\n');
        builder.Append("\nUsage: ").Append(InvocationPrefix).Append(' ').Append(spec.Name).Append(" [options]\n\n");
        builder.Append("Options:\n");
        foreach (OptionSpec option in spec.Options)
        {
            AppendOption(builder, option);
        }

        builder.Append('\n');
        AppendGlobalOptions(builder);
        return builder.ToString();
    }

    private static void AppendOption(StringBuilder builder, OptionSpec option)
    {
        builder.Append("  --").Append(option.Name);
        if (option.Syntax.Length > 0)
        {
            builder.Append(' ').Append(option.Syntax);
        }

        if (option.Required)
        {
            builder.Append(" (required)");
        }

        builder.Append('\n').Append("      ").Append(option.HelpText).Append('\n');
    }

    private static void AppendGlobalOptions(StringBuilder builder)
    {
        builder.Append("Global options:\n");
        builder.Append("  --help, -h       Show help for the given command, or this text.\n");
        builder.Append("  --version        Print the SolidGround CLI version and exit. Must be the only argument.\n");
        builder.Append("  help [command]   Show this help, or help for one command.\n");
    }
}
