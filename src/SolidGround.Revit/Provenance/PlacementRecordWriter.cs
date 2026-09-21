using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SolidGround.Revit.Provenance;

/// <summary>
/// Renders and writes a <see cref="PlacementRecord"/> next to the export bundle, only after a confirmed
/// <c>Committed</c> transaction status (design record §6.6 step 1/§9). Mirrors
/// <c>TerrainExportBundleRenderer</c>'s own conventions: a hand-written, property-order-fixed document (never
/// a reflection-based serializer), indented UTF-8 with no BOM, <c>\n</c> newlines, camelCase property names.
/// </summary>
internal static class PlacementRecordWriter
{
    /// <summary>The file name suffix appended to a base name to form the placement record's file name.</summary>
    internal const string FileSuffix = ".revit-placement.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Renders <paramref name="record"/> and writes it to <c>&lt;outputDirectory&gt;\&lt;baseName&gt;.revit-placement.json</c>, returning the full path written.</summary>
    internal static string Write(string outputDirectory, string baseName, PlacementRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, baseName + FileSuffix);
        File.WriteAllBytes(path, Render(record));
        return path;
    }

    private static byte[] Render(PlacementRecord record)
    {
        JsonWriterOptions options = new()
        {
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
        };

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", record.Schema);
            writer.WriteNumber("schemaVersion", record.SchemaVersion);
            writer.WriteString("createdUtc", record.CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            writer.WriteString("exportDocument", record.ExportDocument);
            writer.WriteString("exportPoints", record.ExportPoints);

            writer.WritePropertyName("toposolid");
            WriteToposolid(writer, record.Toposolid);

            writer.WritePropertyName("unitConversion");
            WriteUnitConversion(writer, record.UnitConversion);

            writer.WritePropertyName("localOrigin");
            WriteLocalOrigin(writer, record.LocalOrigin);

            writer.WritePropertyName("boundaryPlaneElevation");
            WriteBoundaryPlaneElevation(writer, record.BoundaryPlaneElevation);

            writer.WritePropertyName("revitCoordinates");
            WriteRevitCoordinates(writer, record.RevitCoordinates);

            writer.WriteString("sharedCoordinatesStatement", record.SharedCoordinatesStatement);

            writer.WritePropertyName("pointCounts");
            WritePointCounts(writer, record.PointCounts);

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteToposolid(Utf8JsonWriter writer, PlacementToposolidRecord toposolid)
    {
        writer.WriteStartObject();
        writer.WriteNumber("elementId", toposolid.ElementId);
        writer.WriteString("levelName", toposolid.LevelName);
        writer.WriteNumber("levelId", toposolid.LevelId);
        writer.WriteString("toposolidTypeName", toposolid.ToposolidTypeName);
        writer.WriteNumber("toposolidTypeId", toposolid.ToposolidTypeId);
        writer.WriteString("creationStrategy", toposolid.CreationStrategy);
        writer.WriteEndObject();
    }

    private static void WriteUnitConversion(Utf8JsonWriter writer, PlacementUnitConversionRecord unitConversion)
    {
        writer.WriteStartObject();
        writer.WriteString("outputUnit", unitConversion.OutputUnit);
        writer.WriteString("forgeTypeId", unitConversion.ForgeTypeId);
        writer.WriteNumber("metersPerOutputUnit", unitConversion.MetersPerOutputUnit);
        writer.WriteNumber("roundTripDelta", unitConversion.RoundTripDelta);
        writer.WriteEndObject();
    }

    private static void WriteLocalOrigin(Utf8JsonWriter writer, PlacementLocalOriginRecord localOrigin)
    {
        writer.WriteStartObject();
        writer.WriteNumber("sourceX", localOrigin.SourceX);
        writer.WriteNumber("sourceY", localOrigin.SourceY);
        writer.WriteNumber("sourceElevation", localOrigin.SourceElevation);
        writer.WriteString("horizontalReference", localOrigin.HorizontalReference);

        writer.WritePropertyName("verticalReference");
        writer.WriteStartObject();
        writer.WriteString("datum", localOrigin.VerticalReference.Datum);
        writer.WriteString("unit", localOrigin.VerticalReference.Unit);
        if (localOrigin.VerticalReference.GeoidModel is { } geoidModel)
        {
            writer.WriteString("geoidModel", geoidModel);
        }
        else
        {
            writer.WriteNull("geoidModel");
        }

        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteBoundaryPlaneElevation(Utf8JsonWriter writer, PlacementBoundaryPlaneElevationRecord boundaryPlaneElevation)
    {
        writer.WriteStartObject();
        writer.WriteNumber("constantZInternal", boundaryPlaneElevation.ConstantZInternal);
        writer.WriteString("source", boundaryPlaneElevation.Source);
        writer.WriteNumber("levelElevationInternal", boundaryPlaneElevation.LevelElevationInternal);
        writer.WriteString("note", boundaryPlaneElevation.Note);
        writer.WriteEndObject();
    }

    private static void WriteRevitCoordinates(Utf8JsonWriter writer, PlacementRevitCoordinatesRecord revitCoordinates)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("internalOriginIsZero", revitCoordinates.InternalOriginIsZero);

        writer.WritePropertyName("basePointPosition");
        WritePoint(writer, revitCoordinates.BasePointPosition);
        writer.WritePropertyName("basePointSharedPosition");
        WritePoint(writer, revitCoordinates.BasePointSharedPosition);
        writer.WritePropertyName("surveyPointPosition");
        WritePoint(writer, revitCoordinates.SurveyPointPosition);
        writer.WritePropertyName("surveyPointSharedPosition");
        WritePoint(writer, revitCoordinates.SurveyPointSharedPosition);

        writer.WriteString("activeProjectLocationName", revitCoordinates.ActiveProjectLocationName);
        writer.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter writer, PlacementPointRecord point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteNumber("z", point.Z);
        writer.WriteEndObject();
    }

    private static void WritePointCounts(Utf8JsonWriter writer, PlacementPointCountsRecord pointCounts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("original", pointCounts.Original);
        writer.WriteNumber("retained", pointCounts.Retained);
        writer.WriteNumber("budget", pointCounts.Budget);
        writer.WriteEndObject();
    }
}
