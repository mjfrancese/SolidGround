namespace SolidGround.Core.Units;

using System.Globalization;

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

/// <summary>
/// Converts the Settings extension editor's text without coupling its presentation format to terrain output
/// units. The persisted value is always metres and is formatted with round-trip precision.
/// </summary>
public static class DistanceDisplayConverter
{
    public static string FormatMeters(double metres, DistanceDisplayFormat format)
    {
        if (!double.IsFinite(metres) || metres < 0d) throw new ArgumentOutOfRangeException(nameof(metres));
        double displayed = metres / MetresPerDisplayUnit(format);
        return format == DistanceDisplayFormat.FeetAndInches
            ? $"{Math.Floor(displayed).ToString(CultureInfo.InvariantCulture)}' {(displayed - Math.Floor(displayed)) * 12d:R}\""
            : displayed.ToString("R", CultureInfo.InvariantCulture);
    }

    public static double ParseMeters(string text, DistanceDisplayFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        double displayed;
        if (format == DistanceDisplayFormat.FeetAndInches && text.Contains('\'', StringComparison.Ordinal))
        {
            string[] pieces = text.Replace("\"", string.Empty, StringComparison.Ordinal).Split('\'', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double feet) || !double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double inches)) throw new FormatException("Feet and inches must be written as feet' inches\".");
            displayed = feet + inches / 12d;
        }
        else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out displayed))
        {
            throw new FormatException("Distance must be a finite nonnegative number.");
        }

        double metres = displayed * MetresPerDisplayUnit(format);
        if (!double.IsFinite(metres) || metres < 0d) throw new FormatException("Distance must be finite and nonnegative.");
        return metres;
    }

    private static double MetresPerDisplayUnit(DistanceDisplayFormat format) => format switch
    {
        DistanceDisplayFormat.UsSurveyFeet => LengthConverter.MetersPerUnit(LengthUnit.UsSurveyFoot),
        DistanceDisplayFormat.InternationalFeet or DistanceDisplayFormat.FeetAndInches => LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot),
        DistanceDisplayFormat.InternationalInches => LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot) / 12d,
        DistanceDisplayFormat.Metres => 1d,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
