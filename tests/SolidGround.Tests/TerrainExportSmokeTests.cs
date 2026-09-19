using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// One end-to-end smoke test proving the export document manifest is complete: an omitted manifest property
/// would fail this round trip's record equality assertion before any dedicated renderer, reader, or
/// golden-file test runs. See docs/architecture/provenance-and-deterministic-exports.md's "Export document
/// manifest, schema version 1" section for the full contract those dedicated suites verify byte-for-byte.
/// </summary>
public sealed class TerrainExportSmokeTests
{
    [Fact]
    public void RenderingAndReadingAPayloadRoundTripsAnEqualProvenanceRecordAndSampleSequence()
    {
        TerrainProvenance provenance = CreateProvenance(retainedPointCount: 2);
        TerrainExportPayload payload = new(
            [
                new LocalTerrainSample(new LocalCoordinate(1d, 2d, 3d)),
                new LocalTerrainSample(new LocalCoordinate(4d, 5d, 6d)),
            ],
            provenance);

        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "smoke-test");
        Assert.Equal("smoke-test.solidground.json", bundle.DocumentFileName);
        Assert.Equal("smoke-test.points.csv", bundle.PointsFileName);

        TerrainExportPayload roundTripped = TerrainExportBundleReader.Read(bundle.DocumentBytes.Span, bundle.PointsBytes.Span);

        Assert.Equal(provenance, roundTripped.Provenance);
        Assert.Equal(payload.Samples, roundTripped.Samples);
    }

    private static TerrainProvenance CreateProvenance(int retainedPointCount)
    {
        VerticalReference vertical = VerticalReference();
        return new TerrainProvenance(
            TerrainProvenance.CurrentSchemaVersion,
            new ElevationSourceMetadata("OpenTopography", "USGS1m", new CollectionPeriod(new DateOnly(2017, 2, 17), new DateOnly(2017, 2, 27)), "QL2"),
            Transformation(),
            vertical,
            new LocalCoordinateFrame(new Coordinate3D(10d, 20d, 30d), ProjectedReference(), vertical, LengthUnit.UsSurveyFoot),
            new SimplificationRequest(15000, SimplificationMethod.CurvatureAware),
            5,
            retainedPointCount,
            new ElevationRange(1d, 3d, LengthUnit.InternationalFoot));
    }

    private static HorizontalReference GeographicReference() =>
        new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference(LengthUnit unit = LengthUnit.Meter) =>
        new("EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(unit), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.InternationalFoot) => new("NAVD88", unit, "Geoid12B");

    private static HorizontalTransformationDefinition Transformation(HorizontalReference? target = null) => new(
        GeographicReference(),
        target ?? ProjectedReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "candidate-engine",
        "1.0");
}
