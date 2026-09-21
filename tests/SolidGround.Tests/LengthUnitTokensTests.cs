using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="LengthUnitTokens"/>: that every <see cref="LengthUnit"/> member round-trips through
/// <see cref="LengthUnitTokens.TokenOf"/> and <see cref="LengthUnitTokens.Parse"/>, that
/// <see cref="LengthUnitTokens.DefaultToken"/> matches <see cref="LengthConverter.DefaultOutputUnit"/>, and
/// that an unknown token is rejected without being echoed. See docs/architecture/cli-workflow.md's "Units"
/// section and docs/architecture/coordinate-transformation-and-units.md's "`LengthConverter.DefaultOutputUnit`"
/// section. Moved from `SolidGround.Cli.Options` to `SolidGround.Core.Units` for SolidGround Issue #15 (the
/// Core lift, §0.2/§0.4 item 4 of the design record); the round-trip/default-token assertions carry over
/// unchanged, but the rejection test's exception type changed from the CLI-only <c>CliUsageException</c> to
/// <see cref="FormatException"/>, since <see cref="LengthUnitTokens.Parse"/> itself changed to throw that
/// type once it left <c>SolidGround.Cli</c>.
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
    public void ParseRejectsAnUnknownTokenWithAFormatExceptionThatNeverEchoesIt()
    {
        const string unknownToken = "furlong";

        FormatException exception = Assert.Throws<FormatException>(() => LengthUnitTokens.Parse("unit", unknownToken));

        Assert.DoesNotContain(unknownToken, exception.Message, StringComparison.Ordinal);
    }
}
