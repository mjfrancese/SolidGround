using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class TerrainPlacementReframerTests
{
    [Fact]
    public void KnownFloorMapsItsPhysicalHeightToTheChosenProjectPlaneAndKeepsSourceReversible()
    {
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        LocalCoordinateFrame initial = new(new Coordinate3D(500d, 600d, 0d), projected, vertical, LengthUnit.Meter);
        TerrainProvenance provenance = new(TerrainProvenance.CurrentSchemaVersion,
            new ElevationSourceMetadata("synthetic", "grid"), Transformation(projected), vertical,
            ReferenceOrigin.Operator, ReferenceOrigin.Operator, initial, new SimplificationRequest(2), 2, 2,
            new ElevationRange(99d, 101d, LengthUnit.Meter));
        TerrainExportPayload payload = new(
            [new LocalTerrainSample(new LocalCoordinate(0d, 0d, 100d)), new LocalTerrainSample(new LocalCoordinate(1d, 1d, 101d))], provenance);
        TerrainProcessingOutcome outcome = new(payload, null, null);
        ElevationGrid raw = new(projected, vertical, new Coordinate2D(500d, 600d), 1d, 1d,
            GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth, new double?[,] { { 99d, 100d }, { 101d, null } });
        TargetProjectLevel level = new(1, "level-1", "Level 1", 10d);
        BuildingFloorReference floor = BuildingFloorReference.KnownElevation(100d, vertical, level, "survey note");

        TerrainProcessingOutcome reframed = TerrainPlacementReframer.Reframe(outcome, raw, floor);

        Assert.Equal(100d - 3.048d, reframed.Payload.Provenance.LocalFrame.Origin.Elevation, 12);
        Assert.Equal(3.048d, reframed.Payload.Samples[0].Position.Elevation, 12);
        Coordinate3D source = reframed.Payload.Provenance.LocalFrame.ToSource(reframed.Payload.Samples[0].Position);
        Assert.Equal(500d, source.X, 12);
        Assert.Equal(600d, source.Y, 12);
        Assert.Equal(100d, source.Elevation, 12);
        Assert.Same(floor, reframed.Payload.Provenance.FloorReference);
    }

    [Fact]
    public void ProvisionalReferenceUsesOnlyRawValidCellsAndAvoidsEvenValueOverflow()
    {
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        ElevationGrid raw = new(projected, vertical, new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth,
            new double?[,] { { double.MaxValue, null }, { double.MaxValue, null } });
        TerrainProcessingOutcome outcome = new(Payload(projected, vertical), null, null);

        ProvisionalGroundReference reference = TerrainPlacementReframer.ResolveProvisionalGroundReference(outcome, raw);

        Assert.Equal(double.MaxValue, reference.Elevation);
        Assert.Equal("aoi-valid-cell-median-v1", reference.Policy);
    }

    private static TerrainExportPayload Payload(HorizontalReference projected, VerticalReference vertical)
    {
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), projected, vertical, LengthUnit.Meter);
        TerrainProvenance provenance = new(TerrainProvenance.CurrentSchemaVersion, new ElevationSourceMetadata("synthetic", "grid"), Transformation(projected), vertical, ReferenceOrigin.Operator, ReferenceOrigin.Operator, frame, new SimplificationRequest(1), 1, 1, new ElevationRange(1d, 1d, LengthUnit.Meter));
        return new TerrainExportPayload([new LocalTerrainSample(new LocalCoordinate(0d, 0d, 1d))], provenance);
    }

    private static HorizontalTransformationDefinition Transformation(HorizontalReference projected) => new(
        new HorizontalReference("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude), projected,
        new CoordinateOperationDefinition("synthetic", "forward"), new CoordinateOperationDefinition("synthetic", "inverse"), "synthetic", "1");
}
