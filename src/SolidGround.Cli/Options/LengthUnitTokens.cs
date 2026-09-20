using SolidGround.Core.Units;

namespace SolidGround.Cli.Options;

/// <summary>
/// The CLI's single source of truth for the three <c>--unit</c>/<c>--vertical-unit</c> tokens and their
/// mapping to <see cref="LengthUnit"/>, so the option parser, the CLI's own default fallback, and
/// <see cref="OptionTable"/>'s help text can never independently restate -- and drift from -- one another.
/// See docs/architecture/cli-workflow.md's "Units" section.
/// </summary>
internal static class LengthUnitTokens
{
    // The one place a LengthUnit member maps to its own CLI token; every other member below is derived
    // from this fixed order rather than restating the three strings a second time.
    private static readonly LengthUnit[] OrderedUnits =
    [
        LengthUnit.UsSurveyFoot,
        LengthUnit.InternationalFoot,
        LengthUnit.Meter,
    ];

    private static readonly string[] Tokens = Array.ConvertAll(OrderedUnits, TokenOf);

    /// <summary>The three accepted tokens joined with <c>'|'</c>, in the fixed order us-survey-foot, international-foot, meter.</summary>
    internal static string Syntax { get; } = string.Join('|', Tokens);

    /// <summary>The token for <see cref="LengthConverter.DefaultOutputUnit"/>, the CLI's own default when <c>--unit</c> is omitted.</summary>
    internal static string DefaultToken => TokenOf(LengthConverter.DefaultOutputUnit);

    /// <summary>
    /// Parses one of the three accepted tokens into a <see cref="LengthUnit"/>. Moved here, unchanged in
    /// behavior, from the CLI commands' own former <c>ParseLengthUnitValue</c> helper so <c>process</c>'s
    /// and <c>run</c>'s <c>--unit</c> and <c>process</c>'s <c>--vertical-unit</c> all share one parser. The
    /// usage message never echoes <paramref name="text"/> -- see docs/architecture/cli-workflow.md's
    /// "Diagnostics and redaction" section -- and is built from the same token list <see cref="Syntax"/>
    /// uses, so the two can never disagree.
    /// </summary>
    /// <exception cref="CliUsageException"><paramref name="text"/> is not one of the three accepted tokens.</exception>
    internal static LengthUnit Parse(string optionName, string text)
    {
        for (int index = 0; index < Tokens.Length; index++)
        {
            if (string.Equals(Tokens[index], text, StringComparison.Ordinal))
            {
                return OrderedUnits[index];
            }
        }

        throw new CliUsageException($"--{optionName} must be one of: {string.Join(", ", Tokens)}.");
    }

    /// <summary>Returns <paramref name="unit"/>'s own CLI token.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined <see cref="LengthUnit"/> member.</exception>
    internal static string TokenOf(LengthUnit unit) => unit switch
    {
        LengthUnit.UsSurveyFoot => "us-survey-foot",
        LengthUnit.InternationalFoot => "international-foot",
        LengthUnit.Meter => "meter",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit."),
    };
}
