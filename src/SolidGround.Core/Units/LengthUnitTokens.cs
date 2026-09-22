using System.Text.Json;
using SolidGround.Core.Processing;

namespace SolidGround.Core.Units;

/// <summary>
/// The CLI's single source of truth for the three <c>--unit</c>/<c>--vertical-unit</c> tokens and their
/// mapping to <see cref="LengthUnit"/>, so the option parser, the CLI's own default fallback, and
/// <c>OptionTable</c>'s help text can never independently restate -- and drift from -- one another. See
/// docs/architecture/cli-workflow.md's "Units" section. Lifted into <c>SolidGround.Core</c> for SolidGround
/// Issue #15 alongside <see cref="Sources.RasterSourceSidecarIo"/> for the identical structural reason (§0.2
/// of the design record): <see cref="Parse"/> now throws <see cref="FormatException"/> instead of the
/// CLI-only <c>CliUsageException</c>, with its message text unchanged. The CLI's own <c>--unit</c>/
/// <c>--vertical-unit</c> flags remain this type's only caller today, wrapping the new
/// <see cref="FormatException"/> back into <c>CliUsageException</c> at their one call site so CLI-facing
/// output text does not change.
/// </summary>
public static class LengthUnitTokens
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
    public static string Syntax { get; } = string.Join('|', Tokens);

    /// <summary>The token for <see cref="LengthConverter.DefaultOutputUnit"/>, the CLI's own default when <c>--unit</c> is omitted.</summary>
    public static string DefaultToken => TokenOf(LengthConverter.DefaultOutputUnit);

    /// <summary>
    /// Parses one of the three accepted tokens into a <see cref="LengthUnit"/>. Moved here, unchanged in
    /// behavior, from the CLI commands' own former <c>ParseLengthUnitValue</c> helper so <c>process</c>'s
    /// and <c>run</c>'s <c>--unit</c> and <c>process</c>'s <c>--vertical-unit</c> all share one parser. The
    /// usage message never echoes <paramref name="text"/> -- see docs/architecture/cli-workflow.md's
    /// "Diagnostics and redaction" section -- and is built from the same token list <see cref="Syntax"/>
    /// uses, so the two can never disagree.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="text"/> is not one of the three accepted tokens.</exception>
    public static LengthUnit Parse(string optionName, string text)
    {
        for (int index = 0; index < Tokens.Length; index++)
        {
            if (string.Equals(Tokens[index], text, StringComparison.Ordinal))
            {
                return OrderedUnits[index];
            }
        }

        throw new FormatException($"--{optionName} must be one of: {string.Join(", ", Tokens)}.");
    }

    /// <summary>Returns <paramref name="unit"/>'s own CLI token.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined <see cref="LengthUnit"/> member.</exception>
    public static string TokenOf(LengthUnit unit) => unit switch
    {
        LengthUnit.UsSurveyFoot => "us-survey-foot",
        LengthUnit.InternationalFoot => "international-foot",
        LengthUnit.Meter => "meter",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit."),
    };

    /// <summary>
    /// The exact camelCase token <see cref="TerrainRequestSettings.JsonOptions"/>'s <c>JsonStringEnumConverter</c>
    /// produces for <paramref name="unit"/> (for example <c>"usSurveyFoot"</c>) -- deliberately different from
    /// <see cref="TokenOf"/>'s kebab-case CLI flag tokens. SolidGround Issue #16's design record §2 lifted
    /// this out of what was previously <c>CreateToposolidCommand.LengthUnitToken</c>, a private Revit-side
    /// helper with the identical one-line body, so the placement record's <c>unitConversion.outputUnit</c>
    /// field and the Extensible Storage entity's <c>outputUnitToken</c> field can share one call site and
    /// never drift from each other or from <see cref="TerrainRequestSettings.JsonOptions"/>'s own decode
    /// convention.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined <see cref="LengthUnit"/> member.</exception>
    public static string SettingsToken(LengthUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit.");
        }

        return JsonSerializer.Serialize(unit, TerrainRequestSettings.JsonOptions).Trim('"');
    }
}
