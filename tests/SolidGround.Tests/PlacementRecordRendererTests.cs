using System.Globalization;
using System.Text.Json;
using SolidGround.Core.Provenance;

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
                "extensibleStorage",
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
    public void SchemaVersionIsNowTwo()
    {
        // SolidGround Issue #16 design record §5: the placement record's own required shape changed with the
        // addition of PlacementExtensibleStorageRecord, so PlacementRecordDraft.SchemaVersion (and therefore
        // every rendered record's own "schemaVersion" property) bumps 1 -> 2.
        Assert.Equal(2, PlacementRecordDraft.SchemaVersion);

        byte[] bytes = PlacementRecordRenderer.Render(CreateRecord());
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
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

    private static PlacementRecord CreateRecord(DateTime? createdUtc = null, string? geoidModel = "Geoid12B") => new(
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
            SchemaVersion: ExtensibleStorageProvenanceSchema.CurrentVersion));

    private static List<string> PropertyNames(JsonElement obj) => [.. obj.EnumerateObject().Select(property => property.Name)];
}
