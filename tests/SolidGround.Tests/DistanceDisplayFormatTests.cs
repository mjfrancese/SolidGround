namespace SolidGround.Tests;

public sealed class DistanceDisplayFormatTests
{
    [Theory]
    [InlineData("usSurveyFeet")]
    [InlineData("internationalFeet")]
    [InlineData("internationalInches")]
    [InlineData("feetAndInches")]
    [InlineData("metres")]
    public void EachSupportedFormatHasAStableSettingsToken(string token)
    {
        Type tokens = Type.GetType("SolidGround.Core.Units.DistanceDisplayFormatTokens, SolidGround.Core")
            ?? throw new InvalidOperationException("DistanceDisplayFormatTokens has not been added.");
        object format = tokens.GetMethod("ParseSettingsToken")!.Invoke(null, [token])!;

        Assert.Equal(token, tokens.GetMethod("ToSettingsToken")!.Invoke(null, [format]));
    }
}
