using System.Globalization;
using System.Text.Json;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="PlacementRecordRenderer.Render"/>: determinism, the fixed property order and
/// null-handling <c>SolidGround.Revit.Provenance.PlacementRecordWriter</c> relies on staying stable,
/// and the shared byte-level conventions (<c>SolidGround.Core.Exports.TerrainExportBundleRenderer</c>'s own
/// no-CR/no-BOM/single-trailing-LF rule). Moved into <c>SolidGround.Core</c> for SolidGround Issue #15's
/// review fix so this determinism is directly, Revit-free offline testable.
/// </summary>
public sealed class PlacementRecordRendererTests
{
    [Fact]
    public void RenderingTheSameRecordTwiceGivesIdenticalBytes()
    {
        PlacementRecord record = CreateRecord();

        byte[] first = PlacementRecordRenderer.Render(record);
        byte[] second = PlacementRecordRenderer.Render(record);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RenderingAFreshlyConstructedValueEqualRecordGivesIdenticalBytes()
    {
        byte[] first = PlacementRecordRenderer.Render(CreateRecord());
        byte[] second = PlacementRecordRenderer.Render(CreateRecord());

        Assert.Equal(first, second);
    }

    [Fact]
    public void RenderingUnderTheDeDeCultureGivesBytesIdenticalToInvariantCulture()
    {
        PlacementRecord record = CreateRecord();
        byte[] invariantBytes = PlacementRecordRenderer.Render(record);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            byte[] deDeBytes = PlacementRecordRenderer.Render(record);

            Assert.Equal(invariantBytes, deDeBytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RenderedBytesContainNoCarriageReturnNoByteOrderMarkAndEndWithExactlyOneLineFeed()
    {
        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());

        Assert.DoesNotContain((byte)0x0D, bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Rendered bytes must not begin with a UTF-8 byte-order mark.");
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);
    }

    [Fact]
    public void DocumentPropertyOrderMatchesTheManifestAtEveryNestingLevel()
    {
        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;

        Assert.Equal(
            [
                "schema", "schemaVersion", "createdUtc", "exportDocument", "exportPoints", "toposolid", "unitConversion",
                "localOrigin", "boundaryPlaneElevation", "revitCoordinates", "sharedCoordinatesStatement", "pointCounts",
                "extensibleStorage", "propertyLine", "sharedCoordinatesWrite", "floorReference",
            ],
            PropertyNames(root));

        Assert.Equal(
            ["elementId", "levelName", "levelId", "toposolidTypeName", "toposolidTypeId", "creationStrategy"],
            PropertyNames(root.GetProperty("toposolid")));

        Assert.Equal(
            ["outputUnit", "forgeTypeId", "metersPerOutputUnit", "roundTripDelta"],
            PropertyNames(root.GetProperty("unitConversion")));

        JsonElement localOrigin = root.GetProperty("localOrigin");
        Assert.Equal(["sourceX", "sourceY", "sourceElevation", "horizontalReference", "verticalReference"], PropertyNames(localOrigin));
        Assert.Equal(["datum", "unit", "geoidModel"], PropertyNames(localOrigin.GetProperty("verticalReference")));

        Assert.Equal(
            ["constantZInternal", "source", "levelElevationInternal", "note"],
            PropertyNames(root.GetProperty("boundaryPlaneElevation")));

        JsonElement revitCoordinates = root.GetProperty("revitCoordinates");
        Assert.Equal(
            [
                "internalOriginIsZero", "basePointPosition", "basePointSharedPosition", "surveyPointPosition",
                "surveyPointSharedPosition", "activeProjectLocationName",
            ],
            PropertyNames(revitCoordinates));
        Assert.Equal(["x", "y", "z"], PropertyNames(revitCoordinates.GetProperty("basePointPosition")));

        Assert.Equal(["original", "retained", "budget"], PropertyNames(root.GetProperty("pointCounts")));

        Assert.Equal(["schemaGuid", "schemaVersion"], PropertyNames(root.GetProperty("extensibleStorage")));

        Assert.Equal(["created", "elementId", "areaInternal"], PropertyNames(root.GetProperty("propertyLine")));

        Assert.Equal(
            [
                "attempted", "eastWest", "northSouth", "elevation", "angleInternal", "horizontalUnit", "verticalUnit", "verified",
            ],
            PropertyNames(root.GetProperty("sharedCoordinatesWrite")));
    }

    [Fact]
    public void WritesTheExtensibleStorageCrossReferenceAfterPointCounts()
    {
        PlacementRecord record = CreateRecord();

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        List<string> topLevelNames = PropertyNames(root);

        Assert.Equal(topLevelNames.IndexOf("pointCounts") + 1, topLevelNames.IndexOf("extensibleStorage"));

        JsonElement extensibleStorage = root.GetProperty("extensibleStorage");
        Assert.Equal(JsonValueKind.String, extensibleStorage.GetProperty("schemaGuid").ValueKind);
        Assert.Equal(record.ExtensibleStorage.SchemaGuid, extensibleStorage.GetProperty("schemaGuid").GetString());
        Assert.Equal(JsonValueKind.Number, extensibleStorage.GetProperty("schemaVersion").ValueKind);
        Assert.Equal(record.ExtensibleStorage.SchemaVersion, extensibleStorage.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void SchemaVersionIsNowFour()
    {
        Assert.Equal(4, PlacementRecordDraft.SchemaVersion);

        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal(4, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void PropertyLineIsWrittenAsNotCreatedWithNullDetailFieldsByDefault()
    {
        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement propertyLine = document.RootElement.GetProperty("propertyLine");

        Assert.False(propertyLine.GetProperty("created").GetBoolean());
        Assert.Equal(JsonValueKind.Null, propertyLine.GetProperty("elementId").ValueKind);
        Assert.Equal(JsonValueKind.Null, propertyLine.GetProperty("areaInternal").ValueKind);
    }

    [Fact]
    public void PropertyLineIsWrittenWithItsElementIdAndAreaWhenCreated()
    {
        PlacementRecord record = CreateRecord(
            propertyLine: new PlacementPropertyLineRecord(true, ElementId: 123457L, AreaInternal: 1652.34));

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement propertyLine = document.RootElement.GetProperty("propertyLine");

        Assert.True(propertyLine.GetProperty("created").GetBoolean());
        Assert.Equal(123457L, propertyLine.GetProperty("elementId").GetInt64());
        Assert.Equal(1652.34, propertyLine.GetProperty("areaInternal").GetDouble());
    }

    [Fact]
    public void SharedCoordinatesWriteIsWrittenAsNotAttemptedWithNullDetailFieldsByDefault()
    {
        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement sharedCoordinatesWrite = document.RootElement.GetProperty("sharedCoordinatesWrite");

        Assert.False(sharedCoordinatesWrite.GetProperty("attempted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("eastWest").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("northSouth").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("elevation").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("angleInternal").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("horizontalUnit").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("verticalUnit").ValueKind);
        Assert.Equal(JsonValueKind.Null, sharedCoordinatesWrite.GetProperty("verified").ValueKind);
    }

    [Fact]
    public void SharedCoordinatesWriteIsWrittenWithEveryFieldWhenAttempted()
    {
        // The example site's own recorded local origin (matching this note's own placement-record example in
        // docs/architecture/revit-property-line-and-shared-coordinates.md), at zero rotation (owner decision 2,
        // 2026-09-26): angleInternal is recorded Revit-internal (radians), never converted, unlike the other
        // three axes.
        PlacementRecord record = CreateRecord(
            sharedCoordinatesWrite: new PlacementSharedCoordinatesWriteRecord(
                Attempted: true,
                EastWest: 449674.0,
                NorthSouth: 4604563.0,
                Elevation: 183.10,
                AngleInternal: 0d,
                HorizontalUnit: "meter",
                VerticalUnit: "meter",
                Verified: true));

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement sharedCoordinatesWrite = document.RootElement.GetProperty("sharedCoordinatesWrite");

        Assert.True(sharedCoordinatesWrite.GetProperty("attempted").GetBoolean());
        Assert.Equal(449674.0, sharedCoordinatesWrite.GetProperty("eastWest").GetDouble());
        Assert.Equal(4604563.0, sharedCoordinatesWrite.GetProperty("northSouth").GetDouble());
        Assert.Equal(183.10, sharedCoordinatesWrite.GetProperty("elevation").GetDouble());
        Assert.Equal(0d, sharedCoordinatesWrite.GetProperty("angleInternal").GetDouble());
        Assert.Equal("meter", sharedCoordinatesWrite.GetProperty("horizontalUnit").GetString());
        Assert.Equal("meter", sharedCoordinatesWrite.GetProperty("verticalUnit").GetString());
        Assert.True(sharedCoordinatesWrite.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public void FloorReferenceIsWrittenAsAnExplicitNullWhenNoBuildingDatumWasSelected()
    {
        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());

        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("floorReference").ValueKind);
    }

    [Fact]
    public void FloorReferenceIsWrittenInItsCanonicalStructuredForm()
    {
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        BuildingFloorReference floorReference = BuildingFloorReference.KnownElevation(
            183.1d, vertical, new TargetProjectLevel(111L, "level-unique-id", "Level 1", 0d), "site survey");
        PlacementRecord record = CreateRecord(floorReference: floorReference);

        using JsonDocument document = JsonDocument.Parse(PlacementRecordRenderer.Render(record));
        JsonElement floor = document.RootElement.GetProperty("floorReference");

        Assert.Equal("KnownElevation", floor.GetProperty("mode").GetString());
        Assert.Equal("NAVD88", floor.GetProperty("sourceReference").GetProperty("datum").GetString());
        Assert.Equal(111L, floor.GetProperty("targetLevel").GetProperty("id").GetInt64());
        Assert.Equal(183.1d, floor.GetProperty("physicalFloorElevation").GetDouble());
        Assert.Equal(floorReference, BuildingFloorReferenceJson.Deserialize(floor.GetRawText()));
    }

    [Fact]
    public void CreatedUtcIsWrittenAsAnIso8601UtcStringWithSecondPrecision()
    {
        DateTime createdUtc = new(2026, 9, 21, 13, 5, 9, DateTimeKind.Utc);
        PlacementRecord record = CreateRecord(createdUtc: createdUtc);

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal("2026-09-21T13:05:09Z", document.RootElement.GetProperty("createdUtc").GetString());
    }

    [Fact]
    public void ANullGeoidModelIsWrittenAsAnExplicitNull()
    {
        PlacementRecord record = CreateRecord(geoidModel: null);

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement geoidModel = document.RootElement.GetProperty("localOrigin").GetProperty("verticalReference").GetProperty("geoidModel");
        Assert.Equal(JsonValueKind.Null, geoidModel.ValueKind);
    }

    [Fact]
    public void ANonNullGeoidModelIsWrittenAsAString()
    {
        PlacementRecord record = CreateRecord(geoidModel: "Geoid12B");

        byte[] bytes = PlacementRecordRenderer.Render(record);

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement geoidModel = document.RootElement.GetProperty("localOrigin").GetProperty("verticalReference").GetProperty("geoidModel");
        Assert.Equal(JsonValueKind.String, geoidModel.ValueKind);
        Assert.Equal("Geoid12B", geoidModel.GetString());
    }

    [Fact]
    public void RenderRejectsANullRecordWithArgumentNullException() =>
        Assert.Throws<ArgumentNullException>(() => PlacementRecordRenderer.Render(null!));

    private static PlacementRecord CreateRecord(
        DateTime? createdUtc = null,
        string? geoidModel = "Geoid12B",
        PlacementPropertyLineRecord? propertyLine = null,
        PlacementSharedCoordinatesWriteRecord? sharedCoordinatesWrite = null,
        BuildingFloorReference? floorReference = null) => new(
        Schema: PlacementRecordDraft.Schema,
        SchemaVersion: PlacementRecordDraft.SchemaVersion,
        CreatedUtc: createdUtc ?? new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        ExportDocument: "terrain.solidground.json",
        ExportPoints: "terrain.points.csv",
        Toposolid: new PlacementToposolidRecord(
            ElementId: 123456L,
            LevelName: "Level 1",
            LevelId: 111L,
            ToposolidTypeName: "Default",
            ToposolidTypeId: 222L,
            CreationStrategy: "Points"),
        UnitConversion: new PlacementUnitConversionRecord(
            OutputUnit: "UsSurveyFoot",
            ForgeTypeId: "autodesk.unit.unit:feetUSSurvey-1.0.1",
            MetersPerOutputUnit: 1200d / 3937d,
            RoundTripDelta: 0d),
        LocalOrigin: new PlacementLocalOriginRecord(
            SourceX: 745_123.5,
            SourceY: 4_285_987.25,
            SourceElevation: 152.125,
            HorizontalReference: "EPSG:26915",
            VerticalReference: new PlacementVerticalReferenceRecord(Datum: "NAVD88", Unit: "UsSurveyFoot", GeoidModel: geoidModel)),
        BoundaryPlaneElevation: new PlacementBoundaryPlaneElevationRecord(
            ConstantZInternal: 10.5,
            Source: "LowestRetainedSample",
            LevelElevationInternal: 0d,
            Note: "Applied uniformly to every boundary-ring vertex."),
        RevitCoordinates: new PlacementRevitCoordinatesRecord(
            InternalOriginIsZero: true,
            BasePointPosition: new PlacementPointRecord(0d, 0d, 0d),
            BasePointSharedPosition: new PlacementPointRecord(0d, 0d, 0d),
            SurveyPointPosition: new PlacementPointRecord(1d, 2d, 0d),
            SurveyPointSharedPosition: new PlacementPointRecord(1d, 2d, 0d),
            ActiveProjectLocationName: "Internal"),
        SharedCoordinatesStatement: "SolidGround did not move or modify the shared coordinate system.",
        PointCounts: new PlacementPointCountsRecord(Original: 9, Retained: 5, Budget: 15000),
        ExtensibleStorage: new PlacementExtensibleStorageRecord(
            SchemaGuid: ExtensibleStorageProvenanceSchema.SchemaGuidText,
            SchemaVersion: ExtensibleStorageProvenanceSchema.CurrentVersion),
        PropertyLine: propertyLine ?? new PlacementPropertyLineRecord(false, null, null),
        SharedCoordinatesWrite: sharedCoordinatesWrite ?? new PlacementSharedCoordinatesWriteRecord(false, null, null, null, null, null, null, null),
        FloorReference: floorReference);

    private static List<string> PropertyNames(JsonElement obj) => [.. obj.EnumerateObject().Select(property => property.Name)];
}
