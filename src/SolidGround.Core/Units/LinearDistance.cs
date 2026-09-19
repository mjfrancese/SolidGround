namespace SolidGround.Core.Units;

/// <summary>A finite, non-negative linear distance carrying its own unit.</summary>
public readonly record struct LinearDistance
{
    public LinearDistance(double value, LengthUnit unit)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Distance must be finite and non-negative.");
        }

        _ = LengthConverter.MetersPerUnit(unit);

        Value = value;
        Unit = unit;
    }

    public double Value { get; }
    public LengthUnit Unit { get; }

    /// <summary>A zero-length distance expressed in meters.</summary>
    public static LinearDistance Zero { get; } = new(0d, LengthUnit.Meter);

    /// <summary>Creates a distance expressed in meters.</summary>
    public static LinearDistance Meters(double value) => new(value, LengthUnit.Meter);

    /// <summary>Converts this distance to meters using <see cref="LengthConverter"/>.</summary>
    public double ToMeters() => LengthConverter.Convert(Value, Unit, LengthUnit.Meter);

    /// <summary>Converts this distance to <paramref name="unit"/> using <see cref="LengthConverter"/>.</summary>
    public double In(LengthUnit unit) => LengthConverter.Convert(Value, Unit, unit);
}
