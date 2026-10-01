using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ParcelFetchEnvelopePlannerTests
{
    private static readonly HorizontalReference Wgs84Reference =
        WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

    private static readonly VerticalReference PlanningVerticalReference = new("Planning test", LengthUnit.Meter);

    [Fact]
    public void BuildIncludesATwoHundredMeterTerrainMarginBeyondTheZeroMarginLegalRequest()
    {
        ParcelGeometryAoi legalParcel = ExampleSiteLegalParcel();

        Wgs84BoundingBoxAoi zeroMargin = ParcelFetchEnvelopePlanner.Build(legalParcel, LinearDistance.Zero);
        Wgs84BoundingBoxAoi largeMargin = ParcelFetchEnvelopePlanner.Build(legalParcel, LinearDistance.Meters(200d));

        Assert.True(largeMargin.WestLongitude < zeroMargin.WestLongitude);
        Assert.True(largeMargin.EastLongitude > zeroMargin.EastLongitude);
        Assert.True(largeMargin.SouthLatitude < zeroMargin.SouthLatitude);
        Assert.True(largeMargin.NorthLatitude > zeroMargin.NorthLatitude);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(200d)]
    public void BuildContainsEveryDenselySampledCandidateTerrainBoundaryAndEnvelope(double marginMeters)
    {
        foreach (ParcelGeometryAoi legalParcel in new[] { ExampleSiteLegalParcel(), SyntheticZoneEdgeLegalParcel() })
        {
            Wgs84BoundingBoxAoi request = ParcelFetchEnvelopePlanner.Build(legalParcel, LinearDistance.Meters(marginMeters));
            int candidatesVisited = 0;

            foreach (NorthAmericanUtmDefinition candidate in NorthAmericanUtmWellKnownText.Definitions)
            {
                foreach (Coordinate2D geographicPoint in InverseCandidateTerrainBoundarySamples(
                    legalParcel, candidate, LinearDistance.Meters(marginMeters), sampleSpacingMeters: 1d))
                {
                    Assert.True(
                        Contains(request, geographicPoint),
                        $"EPSG:{candidate.EpsgCode} terrain boundary point ({geographicPoint.X:R}, {geographicPoint.Y:R}) fell outside the planned request.");
                }

                candidatesVisited++;
            }

            Assert.Equal(NorthAmericanUtmWellKnownText.Definitions.Count, candidatesVisited);
        }
    }

    [Fact]
    public void BuildRejectsAPreBufferedLegalParcelRatherThanAddingAnUnverifiedGeographicMargin()
    {
        ParcelGeometryAoi alreadyBuffered = new(
            ParcelGeometryFormat.Wkt,
            ExampleSiteLegalParcel().Geometry,
            Wgs84Reference,
            LinearDistance.Meters(1d));

        ParcelFetchEnvelopePlanningException exception = Assert.Throws<ParcelFetchEnvelopePlanningException>(
            () => ParcelFetchEnvelopePlanner.Build(alreadyBuffered, LinearDistance.Zero));

        Assert.Contains("unbuffered legal parcel", exception.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<Coordinate2D> InverseCandidateTerrainBoundarySamples(
        ParcelGeometryAoi legalParcel,
        NorthAmericanUtmDefinition candidate,
        LinearDistance terrainMargin,
        double sampleSpacingMeters)
    {
        IHorizontalCoordinateTransform transform = CreateCandidateTransform(candidate);
        PolygonalRegion legal = ParcelGeometryParser.Parse(legalParcel);
        PolygonalRegion projectedLegal = PolygonalRegionReprojection.Reproject(legal, transform, HorizontalTransformDirection.Forward);
        PolygonalRegion terrain = GridClipper.ResolveEffectiveRegion(ClipRegion.FromRegion(projectedLegal, terrainMargin));

        foreach (PolygonRings polygon in terrain.Polygons)
        {
            foreach (Coordinate2D point in SampleRing(polygon.Shell, sampleSpacingMeters))
            {
                yield return transform.Inverse(point);
            }

            foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes)
            {
                foreach (Coordinate2D point in SampleRing(hole, sampleSpacingMeters))
                {
                    yield return transform.Inverse(point);
                }
            }
        }

        PlanarEnvelope envelope = terrain.Envelope;
        Coordinate2D[] envelopeRing =
        [
            new Coordinate2D(envelope.MinX, envelope.MinY), new Coordinate2D(envelope.MaxX, envelope.MinY),
            new Coordinate2D(envelope.MaxX, envelope.MaxY), new Coordinate2D(envelope.MinX, envelope.MaxY),
            new Coordinate2D(envelope.MinX, envelope.MinY),
        ];
        foreach (Coordinate2D point in SampleRing(envelopeRing, sampleSpacingMeters))
        {
            yield return transform.Inverse(point);
        }
    }

    private static IEnumerable<Coordinate2D> SampleRing(IReadOnlyList<Coordinate2D> ring, double sampleSpacingMeters)
    {
        for (int index = 1; index < ring.Count; index++)
        {
            Coordinate2D start = ring[index - 1];
            Coordinate2D end = ring[index];
            double length = Math.Sqrt(Math.Pow(end.X - start.X, 2d) + Math.Pow(end.Y - start.Y, 2d));
            int intervals = Math.Max(1, (int)Math.Ceiling(length / sampleSpacingMeters));
            for (int interval = 0; interval <= intervals; interval++)
            {
                double fraction = (double)interval / intervals;
                yield return new Coordinate2D(
                    start.X + (fraction * (end.X - start.X)),
                    start.Y + (fraction * (end.Y - start.Y)));
            }
        }
    }

    private static ParcelGeometryAoi ExampleSiteLegalParcel() => new(
        ParcelGeometryFormat.Wkt,
        "POLYGON ((-93.6040 41.5910, -93.6036 41.5910, -93.6036 41.5914, -93.6040 41.5914, -93.6040 41.5910))",
        Wgs84Reference);

    private static ParcelGeometryAoi SyntheticZoneEdgeLegalParcel()
    {
        IHorizontalCoordinateTransform transform = CreateCandidateTransform(
            NorthAmericanUtmWellKnownText.Definitions.Single(definition => definition.EpsgCode == 26915));
        Coordinate2D[] projectedCorners =
        [
            new Coordinate2D(833_000d, 4_600_000d), new Coordinate2D(833_200d, 4_600_000d),
            new Coordinate2D(833_200d, 4_600_200d), new Coordinate2D(833_000d, 4_600_200d),
            new Coordinate2D(833_000d, 4_600_000d),
        ];
        string ring = string.Join(", ", projectedCorners.Select(corner =>
        {
            Coordinate2D geographic = transform.Inverse(corner);
            return string.Create(CultureInfo.InvariantCulture, $"{geographic.X:R} {geographic.Y:R}");
        }));

        return new ParcelGeometryAoi(ParcelGeometryFormat.Wkt, $"POLYGON (({ring}))", Wgs84Reference);
    }

    private static IHorizontalCoordinateTransform CreateCandidateTransform(NorthAmericanUtmDefinition candidate) =>
        ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
            NorthAmericanUtmWellKnownText.Create(candidate.EpsgCode, PlanningVerticalReference));

    private static bool Contains(Wgs84BoundingBoxAoi envelope, Coordinate2D point) =>
        point.X >= envelope.WestLongitude && point.X <= envelope.EastLongitude &&
        point.Y >= envelope.SouthLatitude && point.Y <= envelope.NorthLatitude;
}
