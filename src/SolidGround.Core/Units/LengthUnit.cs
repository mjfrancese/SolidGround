namespace SolidGround.Core.Units;

/// <summary>
/// Length definitions supported by SolidGround contract boundaries.
/// </summary>
public enum LengthUnit
{
    Meter,
    UsSurveyFoot,
    InternationalFoot,
}

/// <summary>
/// Converts explicit physical length units.
/// </summary>
public static class LengthConverter
{
    public static double Convert(double value, LengthUnit from, LengthUnit to)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Length must be finite.");
        }

        return value * MetersPerUnit(from) / MetersPerUnit(to);
    }

    /// <summary>
    /// Returns the exact number of meters represented by one unit.
    /// </summary>
    public static double MetersPerUnit(LengthUnit unit) => unit switch
    {
        LengthUnit.Meter => 1d,
        LengthUnit.UsSurveyFoot => 1200d / 3937d,
        LengthUnit.InternationalFoot => 0.3048d,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit."),
    };
}
