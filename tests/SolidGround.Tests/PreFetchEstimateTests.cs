using SolidGround.Core.Processing;
using SolidGround.Core.Workflow;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class PreFetchEstimateTests
{
    [Fact]
    public void RadiusEstimateUsesTheActualNormalizedRectangularFetchEnvelope()
    {
        PreFetchEstimate estimate = PreFetchEstimator.FromRadius(new RadiusAoiSettings { CenterLatitude = 41, CenterLongitude = -93, RadiusMeters = 10 });
        Assert.Equal(2, estimate.OpenTopographyRequestCount);
        Assert.NotNull(estimate.FetchEnvelope);
        Assert.NotNull(estimate.MinimumSideExpansion);
        Assert.True(estimate.MinimumSideExpansion!.Applied);
        Assert.True(estimate.EnvelopeSquareMeters >= 12_000d);
        Assert.Equal((long)Math.Ceiling(estimate.EnvelopeSquareMeters), estimate.ApproximateOneMeterSamples);
        Assert.Contains("not an entitlement", estimate.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundingBoxAndProcessEstimatesUseTheSameMinimumEnvelopeWithoutAHttpQuote()
    {
        Wgs84BoundingBoxAoi tiny = new(-0.00001d, -0.00001d, 0.00001d, 0.00001d);

        PreFetchEstimate fetch = PreFetchEstimator.FromAreaOfInterest(tiny);
        PreFetchEstimate process = PreFetchEstimator.FromProcessAreaOfInterest(tiny);

        Assert.Equal(fetch.FetchEnvelope, process.FetchEnvelope);
        Assert.Equal(fetch.EnvelopeSquareMeters, process.EnvelopeSquareMeters);
        Assert.Equal(2, fetch.OpenTopographyRequestCount);
        Assert.Equal(0, process.OpenTopographyRequestCount);
        Assert.DoesNotContain("OpenTopography", process.Label, StringComparison.Ordinal);
        Assert.Contains("no HTTP request", process.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void LegalParcelEstimateUsesTheSameProjectionAwareRequestEnvelopeAsAcquisition()
    {
        HorizontalReference wgs84 = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        double longitude = -80d;
        double latitude = 30d;
        string geometry = $"POLYGON (({longitude - 0.001d:R} {latitude - 0.001d:R}, {longitude + 0.001d:R} {latitude - 0.001d:R}, {longitude + 0.001d:R} {latitude + 0.001d:R}, {longitude - 0.001d:R} {latitude + 0.001d:R}, {longitude - 0.001d:R} {latitude - 0.001d:R}))";
        ParcelGeometryAoi legal = new(ParcelGeometryFormat.Wkt, geometry, wgs84, LinearDistance.Zero);
        LinearDistance margin = LinearDistance.Meters(25d);

        PreFetchEstimate estimate = PreFetchEstimator.FromAreaOfInterest(legal, margin);

        Assert.Equal(ParcelFetchEnvelopePlanner.Build(legal, margin), estimate.FetchEnvelope);
        Assert.Null(estimate.MinimumSideExpansion);
    }
}
