using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ProjectionCharacteristicsTests
{
    [Fact]
    public void UtmCentralMeridianMeasuresItsPublishedCentralScaleAndZeroConvergence()
    {
        IHorizontalCoordinateTransform transform = CreateTransform("example-site-synthetic.prj");
        Coordinate2D projectedOrigin = transform.Forward(new Coordinate2D(-93d, 41.59d));
        LocalCoordinateFrame localFrame = new(
            new Coordinate3D(projectedOrigin.X, projectedOrigin.Y, 0d),
            transform.Definition.TargetReference,
            new VerticalReference("NAVD88", LengthUnit.Meter),
            LengthUnit.Meter);

        ProjectionCharacteristicsMeasurement measured = ProjectionCharacteristics.Measure(transform, localFrame);

        // This point is recovered through the same ProjNET inverse path whose documented geographic
        // round-trip limit is 2e-7 degrees; it is not an exact identity operation.
        Assert.InRange(Math.Abs(measured.Wgs84Point.X - -93d), 0d, ProjNetHorizontalCoordinateTransformFactory.GeographicRoundTripToleranceDegrees);
        Assert.InRange(Math.Abs(measured.Wgs84Point.Y - 41.59d), 0d, ProjNetHorizontalCoordinateTransformFactory.GeographicRoundTripToleranceDegrees);
        Assert.Equal(0d, measured.GridConvergenceRadians, 8);
        Assert.Equal(0.9996d, measured.PointScaleFactor, 7);
    }

    [Fact]
    public void UtmOffCentralMeridianMeasuresTheClosedFormConvergence()
    {
        const double latitudeDegrees = 41.59d;
        const double longitudeDegrees = -92d;
        IHorizontalCoordinateTransform transform = CreateTransform("example-site-synthetic.prj");
        ProjectionCharacteristicsMeasurement measured = MeasureAt(transform, longitudeDegrees, latitudeDegrees);

        double expectedConvergence = Math.Atan(Math.Tan(DegreesToRadians(longitudeDegrees - -93d)) * Math.Sin(DegreesToRadians(latitudeDegrees)));

        Assert.Equal(expectedConvergence, measured.GridConvergenceRadians, 5);
        Assert.InRange(measured.PointScaleFactor, 0.9996d, 1.001d);
    }

    [Fact]
    public void StatePlaneLccMeasuresTheAnalyticConvergenceAndUnitScaleAtItsStandardParallel()
    {
        const double latitudeDegrees = 28d + (23d / 60d);
        const double longitudeDegrees = -98.5d;
        IHorizontalCoordinateTransform transform = CreateTransform("nad83-texas-south-central-synthetic.prj");
        ProjectionCharacteristicsMeasurement measured = MeasureAt(transform, longitudeDegrees, latitudeDegrees);

        double lccN = LambertConformalConicN(28d + (23d / 60d), 30d + (17d / 60d));
        double expectedConvergence = lccN * DegreesToRadians(longitudeDegrees - -99d);

        Assert.Equal(expectedConvergence, measured.GridConvergenceRadians, 5);
        Assert.Equal(1d, measured.PointScaleFactor, 4);
    }

    [Fact]
    public void UnstableFiniteDifferenceDerivativeIsRejected()
    {
        IHorizontalCoordinateTransform transform = new StepUnstableTransform();
        LocalCoordinateFrame localFrame = new(
            new Coordinate3D(0d, 0d, 0d),
            transform.Definition.TargetReference,
            new VerticalReference("NAVD88", LengthUnit.Meter),
            LengthUnit.Meter);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ProjectionCharacteristics.Measure(transform, localFrame));

        Assert.Contains("stencil", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IHorizontalCoordinateTransform CreateTransform(string fixtureName) =>
        ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName)));

    private static ProjectionCharacteristicsMeasurement MeasureAt(
        IHorizontalCoordinateTransform transform, double longitudeDegrees, double latitudeDegrees)
    {
        Coordinate2D projected = transform.Forward(new Coordinate2D(longitudeDegrees, latitudeDegrees));
        LocalCoordinateFrame localFrame = new(
            new Coordinate3D(projected.X, projected.Y, 0d),
            transform.Definition.TargetReference,
            new VerticalReference("NAVD88", LengthUnit.Meter),
            LengthUnit.Meter);
        return ProjectionCharacteristics.Measure(transform, localFrame);
    }

    private static double LambertConformalConicN(double firstStandardParallelDegrees, double secondStandardParallelDegrees)
    {
        const double inverseFlattening = 298.257222101d; // GRS 1980, the cited fixture ellipsoid.
        double flattening = 1d / inverseFlattening;
        double eccentricity = Math.Sqrt(flattening * (2d - flattening));
        double first = DegreesToRadians(firstStandardParallelDegrees);
        double second = DegreesToRadians(secondStandardParallelDegrees);
        double m1 = Math.Cos(first) / Math.Sqrt(1d - (eccentricity * eccentricity * Math.Sin(first) * Math.Sin(first)));
        double m2 = Math.Cos(second) / Math.Sqrt(1d - (eccentricity * eccentricity * Math.Sin(second) * Math.Sin(second)));
        double t1 = T(first, eccentricity);
        double t2 = T(second, eccentricity);
        return Math.Log(m1 / m2) / Math.Log(t1 / t2);
    }

    private static double T(double latitudeRadians, double eccentricity) =>
        Math.Tan((Math.PI / 4d) - (latitudeRadians / 2d)) /
        Math.Pow((1d - (eccentricity * Math.Sin(latitudeRadians))) / (1d + (eccentricity * Math.Sin(latitudeRadians))), eccentricity / 2d);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private sealed class StepUnstableTransform : IHorizontalCoordinateTransform
    {
        private const double MetersPerDegreeAtEquator = 111319.49079327357d;
        private const double CubicCoefficient = 100_000_000_000d;

        public StepUnstableTransform()
        {
            HorizontalReference source = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
            HorizontalReference target = new(
                "Synthetic projected metres", "Synthetic", HorizontalReferenceKind.Projected,
                HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
            Definition = new HorizontalTransformationDefinition(
                source,
                target,
                new CoordinateOperationDefinition("test", "forward"),
                new CoordinateOperationDefinition("test", "inverse"),
                "test", "1");
        }

        public HorizontalTransformationDefinition Definition { get; }

        public Coordinate2D Forward(Coordinate2D source) => new(
            (source.X * MetersPerDegreeAtEquator) + (CubicCoefficient * source.X * source.X * source.X),
            source.Y * MetersPerDegreeAtEquator);

        public Coordinate2D Inverse(Coordinate2D target) => target;
    }
}
