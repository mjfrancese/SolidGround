using SolidGround.Core.Provenance;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ExtensibleStorageProvenanceV3Tests
{
    [Fact]
    public void V3IsSelfContainedAndLeavesTheV1AndV2ContractsUntouched()
    {
        Assert.Equal(36, ExtensibleStorageProvenanceSchema.Fields.Count);
        Assert.Equal(2, ExtensibleStorageProvenanceSchemaV2.CurrentVersion);
        Assert.Equal(64, ExtensibleStorageProvenanceSchemaV2.Fields.Count);

        Assert.Equal(3, ExtensibleStorageProvenanceSchemaV3.CurrentVersion);
        Assert.NotEqual(ExtensibleStorageProvenanceSchema.SchemaGuid, ExtensibleStorageProvenanceSchemaV3.SchemaGuid);
        Assert.NotEqual(ExtensibleStorageProvenanceSchemaV2.SchemaGuid, ExtensibleStorageProvenanceSchemaV3.SchemaGuid);
        Assert.Equal(66, ExtensibleStorageProvenanceSchemaV3.Fields.Count);
        Assert.True(ExtensibleStorageProvenanceSchemaV3.Fields.Count < 256);
        Assert.Equal(ExtensibleStorageProvenanceSchemaV2.Fields, ExtensibleStorageProvenanceSchemaV3.Fields.Take(64));
    }

    [Fact]
    public void V3AddsCanonicalFloorAndBuildingOutlineContextStrings()
    {
        ProvenanceFieldDefinition[] fields = ExtensibleStorageProvenanceSchemaV3.Fields.Skip(64).ToArray();

        Assert.Collection(fields,
            floor => AssertField(floor, "floorReferenceJson"),
            outline => AssertField(outline, "buildingOutlineJson"));
    }

    [Fact]
    public void V3ValuesStoreAValidatedCanonicalFloorReferenceWhoseTargetLevelExplainsTheFrameOrigin()
    {
        BuildingOutlineProvenance outline = Outline();
        BuildingFloorReference floor = BuildingFloorReference.KnownElevation(
            100d, Vertical, new TargetProjectLevel(17L, "level-17", "First Floor", 10d), "survey record", outline);
        TerrainExportPayload payload = Payload(floor, outline, frameElevation: 96.952d);

        ExtensibleStorageProvenanceValuesV3 values = ExtensibleStorageProvenanceValuesV3.From(
            payload, Identity, 0.2d, "test", "mvid", "sha");

        Assert.Equal(BuildingFloorReferenceJson.Serialize(floor), values.FloorReferenceJson);
        Assert.Equal(BuildingOutlineProvenanceJson.Serialize(outline), values.BuildingOutlineJson);
        BuildingFloorReference parsed = Assert.IsType<BuildingFloorReference>(BuildingFloorReferenceJson.Deserialize(values.FloorReferenceJson));
        Assert.Equal(BuildingFloorReferenceJson.Serialize(floor), BuildingFloorReferenceJson.Serialize(parsed));
        Assert.Equal(floor.SourceReference, parsed.SourceReference);
        Assert.Equal(floor.TargetLevel, parsed.TargetLevel);
        Assert.Equal(floor.PhysicalFloorElevation, parsed.PhysicalFloorElevation);
    }

    [Fact]
    public void V3ValuesRejectAFloorReferenceThatDoesNotReproduceThePayloadFrameOrigin()
    {
        BuildingFloorReference floor = BuildingFloorReference.KnownElevation(
            100d, Vertical, new TargetProjectLevel(17L, "level-17", "First Floor", 10d), "survey record");

        Assert.Throws<ProvenanceFieldValueException>(() => ExtensibleStorageProvenanceValuesV3.From(
            Payload(floor, null, frameElevation: 100d), Identity, 0.2d, "test", "mvid", "sha"));
    }

    [Fact]
    public void V3ValuesAcceptStandaloneOutlineContextWithoutApplyingAFloorFrameShift()
    {
        BuildingOutlineProvenance outline = Outline();
        TerrainExportPayload payload = Payload(null, outline, frameElevation: 100d);

        ExtensibleStorageProvenanceValuesV3 values = ExtensibleStorageProvenanceValuesV3.From(
            payload, Identity, 0.2d, "test", "mvid", "sha");

        Assert.Equal("null", values.FloorReferenceJson);
        Assert.Equal(BuildingOutlineProvenanceJson.Serialize(outline), values.BuildingOutlineJson);
        BuildingOutlineProvenance roundTripped = Assert.IsType<BuildingOutlineProvenance>(BuildingOutlineProvenanceJson.Deserialize(values.BuildingOutlineJson));
        Assert.Equal(BuildingOutlineProvenanceJson.Serialize(outline), BuildingOutlineProvenanceJson.Serialize(roundTripped));
        Assert.Equal(outline.Provider, roundTripped.Provider);
        Assert.Equal(outline.Release, roundTripped.Release);
        Assert.Equal(outline.LicenseIdentifier, roundTripped.LicenseIdentifier);
        Assert.Equal(outline.LicenseUrl, roundTripped.LicenseUrl);
        Assert.Equal(outline.LicenseText, roundTripped.LicenseText);
        Assert.Equal(outline.Attribution, roundTripped.Attribution);
        Assert.Equal(outline.ManifestSha256, roundTripped.ManifestSha256);
        Assert.Equal(outline.TileKeys.ToArray(), roundTripped.TileKeys.ToArray());
        Assert.Equal(outline.DateOnlyRetrievedDate, roundTripped.DateOnlyRetrievedDate);
    }

    [Fact]
    public void V3ValuesRejectAbsentFloorAndBuildingOutlineContext()
    {
        Assert.Throws<ProvenanceFieldValueException>(() => ExtensibleStorageProvenanceValuesV3.From(
            Payload(null, null, frameElevation: 100d), Identity, 0.2d, "test", "mvid", "sha"));
    }

    private static readonly VerticalReference Vertical = new("NAVD88", LengthUnit.Meter);
    private static readonly TerrainIdentity Identity = new("1", TerrainIdentityKind.Polygon, "stem", "sha256", "content", "frame");

    private static TerrainExportPayload Payload(BuildingFloorReference? floor, BuildingOutlineProvenance? outline, double frameElevation)
    {
        HorizontalReference geographic = new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        HorizontalTransformationDefinition transform = new(geographic, projected, new("WKT1", "forward"), new("WKT1", "inverse"), "test", "1");
        LocalCoordinateFrame frame = new(new Coordinate3D(1d, 2d, frameElevation), projected, Vertical, LengthUnit.Meter);
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            new ElevationSourceMetadata("Synthetic", "synthetic"),
            transform,
            Vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            frame,
            new SimplificationRequest(1, SimplificationMethod.CurvatureAware, coverageFloorFraction: 0.2d),
            1,
            1,
            new ElevationRange(100d, 100d, LengthUnit.Meter),
            floorReference: floor,
            buildingOutline: outline);
        return new([new LocalTerrainSample(new LocalCoordinate(0d, 0d, 3.048d))], provenance);
    }

    private static BuildingOutlineProvenance Outline() => new(
        "Synthetic", "2026-10", "MIT", new Uri("https://example.test/license"), "License text", "Attribution", new string('A', 64), ["000000000"]);

    private static void AssertField(ProvenanceFieldDefinition field, string expectedName)
    {
        Assert.Equal(expectedName, field.Name);
        Assert.Equal(typeof(string), field.ClrType);
        Assert.Equal(ProvenanceFieldSpec.None, field.Spec);
        Assert.DoesNotContain(ExtensibleStorageProvenanceSchemaV2.Fields, candidate => candidate.Name == field.Name);
    }
}
