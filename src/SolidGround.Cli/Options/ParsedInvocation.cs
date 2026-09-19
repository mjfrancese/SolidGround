namespace SolidGround.Cli.Options;

/// <summary>
/// One fully table-checked command invocation: a known verb plus its option values, ready for that
/// command's own strongly-typed option binder to interpret. "Table-checked" means every option name is
/// legal for this verb, no option was given more than once, every flag was given no value and every value
/// option was given exactly one, and every unconditionally required option is present -- all mechanical,
/// name-and-arity-only checks driven from <see cref="OptionTable"/> that need no knowledge of what an
/// option's value means. See docs/architecture/cli-workflow.md's "Options and defaults" section for that
/// table and for why the remaining, meaning-specific validation (area-of-interest mutual exclusion, numeric
/// ranges, enum values, and so on) is left to each command instead of living here.
/// </summary>
internal sealed class ParsedInvocation
{
    private readonly IReadOnlyDictionary<string, string?> values;

    private ParsedInvocation(string verb, IReadOnlyDictionary<string, string?> values)
    {
        Verb = verb;
        this.values = values;
    }

    /// <summary>The invocation's verb, one of <see cref="OptionTable"/>'s <see cref="VerbSpec.Name"/> values.</summary>
    internal string Verb { get; }

    /// <summary>True when <paramref name="name"/> (without its leading <c>--</c>) was given.</summary>
    internal bool HasOption(string name) => values.ContainsKey(name);

    /// <summary>The raw value given for <paramref name="name"/>, or null when the option is a flag or was not given.</summary>
    internal string? GetValue(string name) => values.TryGetValue(name, out string? value) ? value : null;

    /// <exception cref="CliUsageException">
    /// <paramref name="args"/> is empty; its first element is not a known verb; an option is unknown for
    /// that verb, given more than once, given a value when it is a flag, given no value when it takes one;
    /// or a required option is missing.
    /// </exception>
    internal static ParsedInvocation Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            throw new CliUsageException("no command was given.");
        }

        string verb = args[0];
        VerbSpec? spec = OptionTable.TryGetVerb(verb);
        if (spec is null)
        {
            // Never echo the supplied positional token here -- see docs/architecture/cli-workflow.md's
            // "Diagnostics and redaction" section.
            throw new CliUsageException("unknown command; run 'help' to list the commands.");
        }

        Dictionary<string, OptionSpec> specsByName = new(StringComparer.Ordinal);
        foreach (OptionSpec option in spec.Options)
        {
            specsByName[option.Name] = option;
        }

        Dictionary<string, string?> values = new(StringComparer.Ordinal);
        IReadOnlyList<string> remainder = [.. args.Skip(1)];
        foreach (RawOption raw in ArgumentTokenizer.Tokenize(remainder))
        {
            if (!specsByName.TryGetValue(raw.Name, out OptionSpec? option))
            {
                // Unlike a genuinely unbounded positional token (see ArgumentTokenizer), this always starts
                // with the user-typed '--' prefix, so echoing it back is allowed, but only up to 64
                // characters -- see docs/architecture/cli-workflow.md's "Diagnostics and redaction" section.
                string token = "--" + raw.Name;
                string displayedToken = token.Length > 64 ? token[..64] : token;
                throw new CliUsageException($"unknown option '{displayedToken}' for command '{verb}'.");
            }

            if (!values.TryAdd(raw.Name, raw.Value))
            {
                throw new CliUsageException($"--{raw.Name} was specified more than once.");
            }

            if (option.Kind == OptionKind.Flag && raw.Value is not null)
            {
                throw new CliUsageException($"--{raw.Name} does not take a value.");
            }

            if (option.Kind == OptionKind.Value && raw.Value is null)
            {
                throw new CliUsageException($"--{raw.Name} requires a value.");
            }
        }

        foreach (OptionSpec option in spec.Options)
        {
            if (option.Required && !values.ContainsKey(option.Name))
            {
                throw new CliUsageException($"--{option.Name} is required for command '{verb}'.");
            }
        }

        return new ParsedInvocation(verb, values);
    }
}
