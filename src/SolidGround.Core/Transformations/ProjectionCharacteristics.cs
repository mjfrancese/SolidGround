using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Transformations;

/// <summary>Grid-orientation and local scale information measured at a projected local origin.</summary>
public sealed record ProjectionCharacteristicsMeasurement(
    Coordinate2D Wgs84Point,
    /// <summary>The clockwise rotation from true north to grid north, in radians.</summary>
    double GridConvergenceRadians,
    double PointScaleFactor);

/// <summary>
/// Measures the relationship between true north on WGS 84 and the projected grid at a local origin.
/// </summary>
public static class ProjectionCharacteristics
{
    // A symmetric two-metre stencil is large enough to avoid coordinate-transform round-off dominating the
    // derivative, while still representing a point characteristic for the site-scale local frame.
    private const double DifferentiationHalfStepMeters = 1d;
    private const double MaximumRelativeScaleDisagreement = 1e-5;
    private const double MaximumRelativeStencilChange = 1e-5;

    /// <summary>
    /// Measures grid convergence and point scale at <paramref name="localFrame"/>'s projected origin.
    /// </summary>
    public static ProjectionCharacteristicsMeasurement Measure(
        IHorizontalCoordinateTransform transform,
        LocalCoordinateFrame localFrame)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(localFrame);
        ValidateContract(transform.Definition, localFrame);

        Coordinate2D wgs84Point = transform.Inverse(new Coordinate2D(localFrame.Origin.X, localFrame.Origin.Y));
        Wgs84BoundingBoxAoi.ValidateLongitude(wgs84Point.X, nameof(localFrame));
        Wgs84BoundingBoxAoi.ValidateLatitude(wgs84Point.Y, nameof(localFrame));

        LengthUnit projectedUnit = transform.Definition.TargetReference.Unit.LinearUnit!.Value;
        Derivative derivative = Differentiate(transform, wgs84Point, projectedUnit, DifferentiationHalfStepMeters);
        Derivative halfStepDerivative = Differentiate(transform, wgs84Point, projectedUnit, DifferentiationHalfStepMeters / 2d);
        EnsureStableAcrossStencils(transform, derivative, halfStepDerivative);
        double eastScale = VectorLength(derivative.EastingPerEastMeter, derivative.NorthingPerEastMeter);
        double northScale = VectorLength(derivative.EastingPerNorthMeter, derivative.NorthingPerNorthMeter);
        EnsureFinitePositive(eastScale, "east-west point scale");
        EnsureFinitePositive(northScale, "north-south point scale");

        double relativeDisagreement = Math.Abs(eastScale - northScale) / Math.Max(eastScale, northScale);
        if (relativeDisagreement > MaximumRelativeScaleDisagreement)
        {
            throw new ArgumentException(
                "The transform's local east-west and north-south scales disagree, so it does not provide one conformal point scale factor.",
                nameof(transform));
        }

        // The north derivative is the direction of true north in grid coordinates. Its atan2 angle is
        // counter-clockwise from grid north to true north, so negate it to publish the conventional
        // clockwise true-north-to-grid-north convergence.
        double gridConvergenceRadians = -Math.Atan2(derivative.EastingPerNorthMeter, derivative.NorthingPerNorthMeter);
        if (!double.IsFinite(gridConvergenceRadians))
        {
            throw new ArgumentException("The transform produced an invalid grid convergence derivative.", nameof(transform));
        }

        return new ProjectionCharacteristicsMeasurement(
            wgs84Point,
            gridConvergenceRadians,
            (eastScale + northScale) / 2d);
    }

    /// <summary>
    /// Attempts an informational measurement without turning a valid terrain-processing result into a failure.
    /// Only the documented validation failures of <see cref="Measure"/> are absorbed; callers receive the
    /// reason for logs/UI and must not publish numeric characteristics when this returns false.
    /// </summary>
    public static bool TryMeasure(
        IHorizontalCoordinateTransform transform,
        LocalCoordinateFrame localFrame,
        out ProjectionCharacteristicsMeasurement? measurement,
        out string? unavailableReason)
    {
        try
        {
            measurement = Measure(transform, localFrame);
            unavailableReason = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            measurement = null;
            unavailableReason = exception.Message;
            return false;
        }
    }

    private static Derivative Differentiate(
        IHorizontalCoordinateTransform transform, Coordinate2D point, LengthUnit projectedUnit, double halfStepMeters)
    {
        double latitudeStepDegrees = halfStepMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(point.Y);
        double longitudeMetersPerDegree = Wgs84Ellipsoid.MetersPerDegreeLongitude(point.Y);
        if (!double.IsFinite(longitudeMetersPerDegree) || longitudeMetersPerDegree <= 0d)
        {
            throw new ArgumentException("Grid convergence is undefined at this latitude because longitude has no usable local distance.", nameof(point));
        }

        double longitudeStepDegrees = halfStepMeters / longitudeMetersPerDegree;
        Coordinate2D east = transform.Forward(new Coordinate2D(point.X + longitudeStepDegrees, point.Y));
        Coordinate2D west = transform.Forward(new Coordinate2D(point.X - longitudeStepDegrees, point.Y));
        Coordinate2D north = transform.Forward(new Coordinate2D(point.X, point.Y + latitudeStepDegrees));
        Coordinate2D south = transform.Forward(new Coordinate2D(point.X, point.Y - latitudeStepDegrees));

        return new Derivative(
            DifferenceInMeters(east.X, west.X, projectedUnit) / (2d * halfStepMeters),
            DifferenceInMeters(east.Y, west.Y, projectedUnit) / (2d * halfStepMeters),
            DifferenceInMeters(north.X, south.X, projectedUnit) / (2d * halfStepMeters),
            DifferenceInMeters(north.Y, south.Y, projectedUnit) / (2d * halfStepMeters));
    }

    private static double DifferenceInMeters(double positive, double negative, LengthUnit projectedUnit) =>
        LengthConverter.Convert(positive - negative, projectedUnit, LengthUnit.Meter);

    private static double VectorLength(double x, double y) => Math.Sqrt((x * x) + (y * y));

    private static void EnsureStableAcrossStencils(IHorizontalCoordinateTransform transform, Derivative oneMeter, Derivative halfMeter)
    {
        if (!AreStable(oneMeter.EastingPerEastMeter, halfMeter.EastingPerEastMeter)
            || !AreStable(oneMeter.NorthingPerEastMeter, halfMeter.NorthingPerEastMeter)
            || !AreStable(oneMeter.EastingPerNorthMeter, halfMeter.EastingPerNorthMeter)
            || !AreStable(oneMeter.NorthingPerNorthMeter, halfMeter.NorthingPerNorthMeter))
        {
            throw new ArgumentException(
                "The transform's local derivative changes materially when the centered differentiation stencil is halved.",
                nameof(transform));
        }
    }

    private static bool AreStable(double first, double second) =>
        double.IsFinite(first)
        && double.IsFinite(second)
        && Math.Abs(first - second) <= MaximumRelativeStencilChange * Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second)));

    private static void ValidateContract(HorizontalTransformationDefinition definition, LocalCoordinateFrame localFrame)
    {
        if (definition.SourceReference.Kind != HorizontalReferenceKind.Geographic
            || definition.SourceReference.AxisOrder != HorizontalAxisOrder.LongitudeLatitude
            || !string.Equals(definition.SourceReference.CoordinateReferenceSystem, "EPSG:4326", StringComparison.Ordinal))
        {
            throw new ArgumentException("Projection characteristics require a WGS 84 (EPSG:4326) longitude-latitude transform source.", nameof(definition));
        }

        if (definition.TargetReference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new ArgumentException("Projection characteristics require a projected transform target.", nameof(definition));
        }

        if (definition.TargetReference != localFrame.ProjectedHorizontalReference)
        {
            throw new ArgumentException("The local frame's projected reference must match the transform target reference.", nameof(localFrame));
        }
    }

    private static void EnsureFinitePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentException($"The transform produced an invalid {name}.", nameof(value));
        }
    }

    private sealed record Derivative(
        double EastingPerEastMeter,
        double NorthingPerEastMeter,
        double EastingPerNorthMeter,
        double NorthingPerNorthMeter);
}
