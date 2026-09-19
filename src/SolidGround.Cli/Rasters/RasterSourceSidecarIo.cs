using System.Globalization;
using System.Text.Json;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Rasters;

/// <summary>
/// Writes and strictly reads the <see cref="RasterSourceSidecar"/> <c>.source.json</c> document. See
/// docs/architecture/cli-workflow.md's "Raster set persistence" section for the fixed property order
/// <see cref="Write"/> emits and <see cref="Read"/> enforces, and its "Determinism" section for why the
/// writer uses <see cref="Utf8JsonWriter"/> with an explicit <c>"\n"</c> newline instead of a reflection-based
/// serializer.
/// </summary>
internal static class RasterSourceSidecarIo
{
    internal const string Schema = "solidground.raster-source";
    internal const int CurrentSchemaVersion = 1;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>
    /// Writes <paramref name="sidecar"/> to <paramref name="destination"/> as indented, UTF-8, no-BOM JSON
    /// with a trailing <c>"\n"</c>. Does not close or dispose <paramref name="destination"/>.
    /// </summary>
    internal static void Write(RasterSourceSidecar sidecar, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentNullException.ThrowIfNull(destination);

        JsonWriterOptions options = new()
        {
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
        };

        using (Utf8JsonWriter writer = new(destination, options))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("sourceName", sidecar.SourceName);
            writer.WriteString("datasetIdentifier", sidecar.DatasetIdentifier);

            writer.WritePropertyName("collectionPeriod");
            if (sidecar.CollectionPeriod is { } period)
            {
                writer.WriteStartObject();
                writer.WriteString("start", period.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteString("end", period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            if (sidecar.QualityLevel is { } qualityLevel)
            {
                writer.WriteString("qualityLevel", qualityLevel);
            }
            else
            {
                writer.WriteNull("qualityLevel");
            }

            writer.WritePropertyName("vertical");
            WriteVertical(writer, sidecar.Vertical);

            writer.WritePropertyName("acquisition");
            WriteAcquisition(writer, sidecar.Acquisition);

            writer.WriteEndObject();
        }

        destination.WriteByte((byte)'\n');
    }

    private static void WriteVertical(Utf8JsonWriter writer, RasterSourceVertical vertical)
    {
        writer.WriteStartObject();
        writer.WriteString("datum", vertical.Datum);
        writer.WriteString("unit", vertical.Unit.ToString());
        if (vertical.GeoidModel is { } geoidModel)
        {
            writer.WriteString("geoidModel", geoidModel);
        }
        else
        {
            writer.WriteNull("geoidModel");
        }

        writer.WriteEndObject();
    }

    private static void WriteAcquisition(Utf8JsonWriter writer, RasterSourceAcquisition acquisition)
    {
        writer.WriteStartObject();
        writer.WriteString("redactedRequestUri", acquisition.RedactedRequestUri);
        writer.WriteNumber("statusCode", acquisition.StatusCode);

        if (acquisition.ContentType is { } contentType)
        {
            writer.WriteString("contentType", contentType);
        }
        else
        {
            writer.WriteNull("contentType");
        }

        if (acquisition.ContentDispositionFileName is { } contentDispositionFileName)
        {
            writer.WriteString("contentDispositionFileName", contentDispositionFileName);
        }
        else
        {
            writer.WriteNull("contentDispositionFileName");
        }

        writer.WriteStartArray("archiveEntryNames");
        foreach (string entryName in acquisition.ArchiveEntryNames)
        {
            writer.WriteStringValue(entryName);
        }

        writer.WriteEndArray();

        writer.WriteString("referenceSource", acquisition.ReferenceSource);
        writer.WriteNumber("responseByteCount", acquisition.ResponseByteCount);
        writer.WriteEndObject();
    }

    /// <exception cref="CliUsageException">
    /// The JSON is malformed, or any property is missing, unrecognized, duplicated, or wrongly typed, or
    /// <c>schema</c>/<c>schemaVersion</c> do not match the constants above. <paramref name="sourcePath"/> is
    /// named in the message; the raw bytes never are.
    /// </exception>
    internal static RasterSourceSidecar Read(ReadOnlySpan<byte> json, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        JsonDocument document;
        try
        {
            // JsonDocument.Parse has no ReadOnlySpan<byte> overload (only ReadOnlyMemory<byte>,
            // ReadOnlySequence<byte>, Stream, and string), so the span is copied once here into an array.
            document = JsonDocument.Parse(json.ToArray(), DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' is not well-formed JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            RequireObject(root, sourcePath, "$");

            Dictionary<string, JsonElement> top = ReadObjectProperties(root, sourcePath, "$",
                ["schema", "schemaVersion", "sourceName", "datasetIdentifier", "collectionPeriod", "qualityLevel", "vertical", "acquisition"]);

            string schema = RequireString(top["schema"], sourcePath, "$.schema");
            if (!string.Equals(schema, Schema, StringComparison.Ordinal))
            {
                // Never echo the sidecar's actual value here -- see docs/architecture/cli-workflow.md's
                // "Diagnostics and redaction" section.
                throw new CliUsageException(
                    $"'{sourcePath}': property 'schema' must be \"{Schema}\".");
            }

            int schemaVersion = RequireInt(top["schemaVersion"], sourcePath, "$.schemaVersion");
            if (schemaVersion != CurrentSchemaVersion)
            {
                // Never echo the sidecar's actual value here -- see docs/architecture/cli-workflow.md's
                // "Diagnostics and redaction" section.
                throw new CliUsageException(
                    $"'{sourcePath}': property 'schemaVersion' must be {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
            }

            string sourceName = RequireString(top["sourceName"], sourcePath, "$.sourceName");
            string datasetIdentifier = RequireString(top["datasetIdentifier"], sourcePath, "$.datasetIdentifier");

            JsonElement collectionPeriodElement = top["collectionPeriod"];
            CollectionPeriod? collectionPeriod = collectionPeriodElement.ValueKind == JsonValueKind.Null
                ? null
                : ParseCollectionPeriod(collectionPeriodElement, sourcePath, "$.collectionPeriod");

            string? qualityLevel = RequireStringOrNull(top["qualityLevel"], sourcePath, "$.qualityLevel");
            RasterSourceVertical vertical = ParseVertical(top["vertical"], sourcePath, "$.vertical");
            RasterSourceAcquisition acquisition = ParseAcquisition(top["acquisition"], sourcePath, "$.acquisition");

            return new RasterSourceSidecar(sourceName, datasetIdentifier, collectionPeriod, qualityLevel, vertical, acquisition);
        }
    }

    private static CollectionPeriod ParseCollectionPeriod(JsonElement obj, string sourcePath, string jsonPath)
    {
        RequireObject(obj, sourcePath, jsonPath);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, sourcePath, jsonPath, ["start", "end"]);

        DateOnly start = RequireDate(props["start"], sourcePath, $"{jsonPath}.start");
        DateOnly end = RequireDate(props["end"], sourcePath, $"{jsonPath}.end");

        try
        {
            return new CollectionPeriod(start, end);
        }
        catch (ArgumentException ex)
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}'.", ex);
        }
    }

    private static RasterSourceVertical ParseVertical(JsonElement obj, string sourcePath, string jsonPath)
    {
        RequireObject(obj, sourcePath, jsonPath);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, sourcePath, jsonPath, ["datum", "unit", "geoidModel"]);

        string datum = RequireString(props["datum"], sourcePath, $"{jsonPath}.datum");
        LengthUnit unit = RequireEnum<LengthUnit>(props["unit"], sourcePath, $"{jsonPath}.unit");
        string? geoidModel = RequireStringOrNull(props["geoidModel"], sourcePath, $"{jsonPath}.geoidModel");
        return new RasterSourceVertical(datum, unit, geoidModel);
    }

    private static RasterSourceAcquisition ParseAcquisition(JsonElement obj, string sourcePath, string jsonPath)
    {
        RequireObject(obj, sourcePath, jsonPath);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, sourcePath, jsonPath,
            ["redactedRequestUri", "statusCode", "contentType", "contentDispositionFileName", "archiveEntryNames", "referenceSource", "responseByteCount"]);

        string redactedRequestUri = RequireString(props["redactedRequestUri"], sourcePath, $"{jsonPath}.redactedRequestUri");
        int statusCode = RequireInt(props["statusCode"], sourcePath, $"{jsonPath}.statusCode");
        string? contentType = RequireStringOrNull(props["contentType"], sourcePath, $"{jsonPath}.contentType");
        string? contentDispositionFileName = RequireStringOrNull(props["contentDispositionFileName"], sourcePath, $"{jsonPath}.contentDispositionFileName");
        string[] archiveEntryNames = RequireStringArray(props["archiveEntryNames"], sourcePath, $"{jsonPath}.archiveEntryNames");
        string referenceSource = RequireEnumName<OpenTopographyReferenceSource>(props["referenceSource"], sourcePath, $"{jsonPath}.referenceSource");
        long responseByteCount = RequireLong(props["responseByteCount"], sourcePath, $"{jsonPath}.responseByteCount");

        return new RasterSourceAcquisition(
            redactedRequestUri, statusCode, contentType, contentDispositionFileName, archiveEntryNames, referenceSource, responseByteCount);
    }

    // ---- low-level JSON value helpers ------------------------------------------------------------------
    //
    // Duplicated in shape from TerrainExportBundleReader's own private ReadObjectProperties/Require* helpers
    // (that type is internal to a different assembly, so it cannot be referenced directly) but reporting
    // CliUsageException and always naming sourcePath rather than only the in-document JSON path, per
    // docs/architecture/cli-workflow.md's "Raster set persistence" section.

    private static void RequireObject(JsonElement value, string sourcePath, string jsonPath)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected an object.");
        }
    }

    private static Dictionary<string, JsonElement> ReadObjectProperties(
        JsonElement obj, string sourcePath, string jsonPath, IReadOnlyCollection<string> expectedNames)
    {
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new CliUsageException(
                    $"The raster source sidecar '{sourcePath}' has a duplicate property '{jsonPath}.{property.Name}'.");
            }
        }

        foreach (string name in properties.Keys)
        {
            if (!expectedNames.Contains(name, StringComparer.Ordinal))
            {
                throw new CliUsageException(
                    $"The raster source sidecar '{sourcePath}' has an unrecognized property '{jsonPath}.{name}'.");
            }
        }

        foreach (string name in expectedNames)
        {
            if (!properties.ContainsKey(name))
            {
                throw new CliUsageException(
                    $"The raster source sidecar '{sourcePath}' is missing required property '{jsonPath}.{name}'.");
            }
        }

        return properties;
    }

    private static string RequireString(JsonElement value, string sourcePath, string jsonPath)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected a string.");
        }

        return value.GetString()!;
    }

    private static string? RequireStringOrNull(JsonElement value, string sourcePath, string jsonPath) =>
        value.ValueKind == JsonValueKind.Null ? null : RequireString(value, sourcePath, jsonPath);

    private static int RequireInt(JsonElement value, string sourcePath, string jsonPath)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected an integer.");
        }

        return result;
    }

    private static long RequireLong(JsonElement value, string sourcePath, string jsonPath)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected an integer.");
        }

        return result;
    }

    private static DateOnly RequireDate(JsonElement value, string sourcePath, string jsonPath)
    {
        string text = RequireString(value, sourcePath, jsonPath);
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly result))
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected a yyyy-MM-dd date.");
        }

        return result;
    }

    /// <summary>
    /// Requires <paramref name="value"/> to be a JSON string equal to a defined <typeparamref name="TEnum"/>
    /// member name, matched case-sensitively and exactly against <see cref="Enum.GetNames{TEnum}()"/> --
    /// deliberately stricter than <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> alone, which
    /// also accepts a defined member's underlying numeric value as a string, whitespace-padded names, and
    /// comma-joined name lists.
    /// </summary>
    private static TEnum RequireEnum<TEnum>(JsonElement value, string sourcePath, string jsonPath) where TEnum : struct, Enum
    {
        string text = RequireString(value, sourcePath, jsonPath);
        if (!Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal))
        {
            // Never echo the sidecar's actual value here -- see docs/architecture/cli-workflow.md's
            // "Diagnostics and redaction" section.
            throw new CliUsageException($"'{sourcePath}': property '{jsonPath}' is not a recognized value.");
        }

        return Enum.Parse<TEnum>(text, ignoreCase: false);
    }

    private static string RequireEnumName<TEnum>(JsonElement value, string sourcePath, string jsonPath) where TEnum : struct, Enum =>
        RequireEnum<TEnum>(value, sourcePath, jsonPath).ToString();

    private static string[] RequireStringArray(JsonElement value, string sourcePath, string jsonPath)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new CliUsageException($"The raster source sidecar '{sourcePath}' has an invalid '{jsonPath}': expected an array.");
        }

        JsonElement[] items = [.. value.EnumerateArray()];
        string[] result = new string[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            result[index] = RequireString(items[index], sourcePath, $"{jsonPath}[{index.ToString(CultureInfo.InvariantCulture)}]");
        }

        return result;
    }
}
