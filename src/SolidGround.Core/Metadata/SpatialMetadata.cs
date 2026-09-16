using SolidGround.Core.Units;

namespace SolidGround.Core.Metadata;

/// <summary>Declares the horizontal reference independently of elevation metadata.</summary>
public enum HorizontalReferenceKind
{
    Geographic,
    Projected,
}

/// <summary>Declares the order in which X/Y ordinates are supplied to a reference.</summary>
public enum HorizontalAxisOrder
{
    LongitudeLatitude,
    LatitudeLongitude,
    EastingNorthing,
    NorthingEasting,
}

/// <summary>Declares whether horizontal ordinates are decimal degrees or a linear unit.</summary>
public sealed record HorizontalUnit
{
    private HorizontalUnit(HorizontalReferenceKind referenceKind, LengthUnit? linearUnit)
    {
        ReferenceKind = referenceKind;
        LinearUnit = linearUnit;
    }

    public HorizontalReferenceKind ReferenceKind { get; }
    public LengthUnit? LinearUnit { get; }
    public static HorizontalUnit DecimalDegrees { get; } = new(HorizontalReferenceKind.Geographic, null);

    public static HorizontalUnit Linear(LengthUnit unit)
    {
        _ = LengthConverter.MetersPerUnit(unit);
        return new HorizontalUnit(HorizontalReferenceKind.Projected, unit);
    }
}

public sealed record HorizontalReference
{
    public HorizontalReference(
        string coordinateReferenceSystem,
        string datum,
        HorizontalReferenceKind kind,
        HorizontalUnit unit,
        HorizontalAxisOrder axisOrder)
    {
        if (string.IsNullOrWhiteSpace(coordinateReferenceSystem))
        {
            throw new ArgumentException("A horizontal coordinate reference system is required.", nameof(coordinateReferenceSystem));
        }

        if (string.IsNullOrWhiteSpace(datum))
        {
            throw new ArgumentException("A horizontal datum is required.", nameof(datum));
        }

        ArgumentNullException.ThrowIfNull(unit);
        if (!Enum.IsDefined(kind) || unit.ReferenceKind != kind)
        {
            throw new ArgumentException("Horizontal reference kind and unit must agree.", nameof(unit));
        }

        bool validAxisOrder = kind == HorizontalReferenceKind.Geographic
            ? axisOrder is HorizontalAxisOrder.LongitudeLatitude or HorizontalAxisOrder.LatitudeLongitude
            : axisOrder is HorizontalAxisOrder.EastingNorthing or HorizontalAxisOrder.NorthingEasting;
        if (!validAxisOrder)
        {
            throw new ArgumentException("Axis order must match the horizontal reference kind.", nameof(axisOrder));
        }

        CoordinateReferenceSystem = coordinateReferenceSystem;
        Datum = datum;
        Kind = kind;
        Unit = unit;
        AxisOrder = axisOrder;
    }

    public string CoordinateReferenceSystem { get; }
    public string Datum { get; }
    public HorizontalReferenceKind Kind { get; }
    public HorizontalUnit Unit { get; }
    public HorizontalAxisOrder AxisOrder { get; }
}

/// <summary>Declares vertical datum information without implying a horizontal transformation.</summary>
public sealed record VerticalReference
{
    public VerticalReference(string datum, LengthUnit unit, string? geoidModel = null)
    {
        if (string.IsNullOrWhiteSpace(datum))
        {
            throw new ArgumentException("A vertical datum is required.", nameof(datum));
        }

        _ = LengthConverter.MetersPerUnit(unit);
        Datum = datum;
        Unit = unit;
        GeoidModel = geoidModel;
    }

    public string Datum { get; }
    public LengthUnit Unit { get; }
    public string? GeoidModel { get; }
}
