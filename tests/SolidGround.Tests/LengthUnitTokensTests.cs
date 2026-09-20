using SolidGround.Cli;
using SolidGround.Cli.Options;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="LengthUnitTokens"/>: that every <see cref="LengthUnit"/> member round-trips through
/// <see cref="LengthUnitTokens.TokenOf"/> and <see cref="LengthUnitTokens.Parse"/>, that
/// <see cref="LengthUnitTokens.DefaultToken"/> matches <see cref="LengthConverter.DefaultOutputUnit"/>, and
/// that an unknown token is rejected without being echoed. See docs/architecture/cli-workflow.md's "Units"
/// section and docs/architecture/coordinate-transformation-and-units.md's "`LengthConverter.DefaultOutputUnit`"
/// section.
/// </summary>
public sealed class LengthUnitTokensTests
{
    [Fact]
    public void EveryLengthUnitMemberRoundTripsThroughTokenOfAndParse()
    {
        foreach (LengthUnit unit in Enum.GetValues<LengthUnit>())
        {
            string token = LengthUnitTokens.TokenOf(unit);
            Assert.Equal(unit, LengthUnitTokens.Parse("unit", token));
        }
    }

    [Fact]
    public void DefaultTokenEqualsTokenOfLengthConverterDefaultOutputUnit()
    {
        Assert.Equal(LengthUnitTokens.TokenOf(LengthConverter.DefaultOutputUnit), LengthUnitTokens.DefaultToken);
    }

    [Fact]
    public void ParseRejectsAnUnknownTokenWithACliUsageExceptionThatNeverEchoesIt()
    {
        const string unknownToken = "furlong";

        CliUsageException exception = Assert.Throws<CliUsageException>(() => LengthUnitTokens.Parse("unit", unknownToken));

        Assert.DoesNotContain(unknownToken, exception.Message, StringComparison.Ordinal);
    }
}
