namespace SolidGround.Core.Geometry;

/// <summary>A finite two-dimensional coordinate. Its unit is carried by its containing contract.</summary>
public readonly record struct Coordinate2D
{
    public Coordinate2D(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite.");
        }

        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}

/// <summary>A finite three-dimensional coordinate. Its unit is carried by its containing contract.</summary>
public readonly record struct Coordinate3D
{
    public Coordinate3D(double x, double y, double elevation)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(elevation))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite.");
        }

        X = x;
        Y = y;
        Elevation = elevation;
    }

    public double X { get; }
    public double Y { get; }
    public double Elevation { get; }
}

/// <summary>A coordinate expressed relative to a local coordinate frame.</summary>
public readonly record struct LocalCoordinate
{
    public LocalCoordinate(double x, double y, double elevation)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(elevation))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be finite.");
        }

        X = x;
        Y = y;
        Elevation = elevation;
    }

    public double X { get; }
    public double Y { get; }
    public double Elevation { get; }
}
