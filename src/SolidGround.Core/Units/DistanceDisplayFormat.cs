namespace SolidGround.Core.Units;

/// <summary>Presentation-only distance formats. Stored terrain-extension values remain canonical metres.</summary>
public enum DistanceDisplayFormat
{
    UsSurveyFeet,
    InternationalFeet,
    InternationalInches,
    FeetAndInches,
    Metres,
}

/// <summary>Stable JSON settings tokens for <see cref="DistanceDisplayFormat"/>.</summary>
public static class DistanceDisplayFormatTokens
{
    public static string ToSettingsToken(DistanceDisplayFormat format) => format switch
    {
        DistanceDisplayFormat.UsSurveyFeet => "usSurveyFeet",
        DistanceDisplayFormat.InternationalFeet => "internationalFeet",
        DistanceDisplayFormat.InternationalInches => "internationalInches",
        DistanceDisplayFormat.FeetAndInches => "feetAndInches",
        DistanceDisplayFormat.Metres => "metres",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported distance display format."),
    };

    public static DistanceDisplayFormat ParseSettingsToken(string token) => token switch
    {
        "usSurveyFeet" => DistanceDisplayFormat.UsSurveyFeet,
        "internationalFeet" => DistanceDisplayFormat.InternationalFeet,
        "internationalInches" => DistanceDisplayFormat.InternationalInches,
        "feetAndInches" => DistanceDisplayFormat.FeetAndInches,
        "metres" => DistanceDisplayFormat.Metres,
        _ => throw new FormatException($"distanceDisplayFormat '{token}' is not recognized."),
    };
}
