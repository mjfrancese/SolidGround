using System.Globalization;

namespace SolidGround.Cli.Options;

/// <summary>One <c>--name</c> token and its associated value, if any, in the order given on the command line.</summary>
internal readonly record struct RawOption(string Name, string? Value);

/// <summary>
/// Splits an argument list into <see cref="RawOption"/> tokens: long names only, both <c>--name value</c>
/// and <c>--name=value</c> forms. Purely lexical -- it knows nothing about which option names are legal for
/// a verb, which are required, or which are flags, so a bare option is recorded with a null value rather
/// than rejected here. See docs/architecture/cli-workflow.md's "Options and defaults" section for why that
/// table-driven validation happens separately, in <c>ParsedInvocation</c>, after tokenizing: a global
/// <c>--help</c>/<c>-h</c> request must short-circuit even when another option elsewhere in the same
/// invocation is unknown or malformed, which only works if nothing before that dispatch can throw for such
/// an option.
/// </summary>
internal static class ArgumentTokenizer
{
    /// <exception cref="CliUsageException">
    /// An element of <paramref name="args"/> does not begin with <c>--</c>, or is exactly <c>--</c> or
    /// begins with <c>--=</c>.
    /// </exception>
    internal static IReadOnlyList<RawOption> Tokenize(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        List<RawOption> options = new(args.Count);
        int index = 0;
        while (index < args.Count)
        {
            string token = args[index];
            if (!IsLongOptionToken(token))
            {
                // Never echo the offending token: it could be anything, including a secret pasted as a bare
                // positional argument. See docs/architecture/cli-workflow.md's "Diagnostics and redaction"
                // section.
                throw new CliUsageException($"argument {(index + 1).ToString(CultureInfo.InvariantCulture)} does not begin with '--'.");
            }

            string body = token[2..];
            int equalsIndex = body.IndexOf('=');
            if (equalsIndex == 0)
            {
                throw new CliUsageException($"argument {(index + 1).ToString(CultureInfo.InvariantCulture)} is missing an option name before '='.");
            }

            if (equalsIndex > 0)
            {
                options.Add(new RawOption(body[..equalsIndex], body[(equalsIndex + 1)..]));
                index++;
                continue;
            }

            if (body.Length == 0)
            {
                throw new CliUsageException("'--' is not a valid option.");
            }

            string? nextToken = index + 1 < args.Count ? args[index + 1] : null;
            if (nextToken is not null && !IsLongOptionToken(nextToken))
            {
                options.Add(new RawOption(body, nextToken));
                index += 2;
            }
            else
            {
                options.Add(new RawOption(body, null));
                index++;
            }
        }

        return options;
    }

    private static bool IsLongOptionToken(string token) => token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2;
}
