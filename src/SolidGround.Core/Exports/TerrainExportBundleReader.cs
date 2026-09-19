using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Exports;

/// <summary>
/// Strictly reads a schema version 1 export document (and, for <see cref="Read"/>, its points file) back into
/// domain objects: every manifest property is required and exactly typed, every enum string must be an exact
/// defined member name, every domain record is reconstructed through its own public constructor so existing
/// invariants re-run, and any deviation is reported as a <see cref="TerrainExportException"/> naming the JSON
/// path. See docs/architecture/provenance-and-deterministic-exports.md's "Export document manifest, schema
/// version 1" and "Versioning and compatibility policy" sections for the manifest this reader enforces, and
/// its "Reconstructing source coordinates" section for how a caller uses the result.
/// </summary>
public static class TerrainExportBundleReader
{
    /// <summary>
    /// Strictly parses <paramref name="documentBytes"/> and returns its provenance record, without requiring
    /// or validating any points file bytes. The document's <c>points</c> descriptor is still fully validated
    /// against the reconstructed provenance (count, format, columns, unit); only the SHA-256 and line-by-line
    /// checks that need actual point bytes are skipped.
    /// </summary>
    /// <exception cref="TerrainExportException">The bytes are not a well-formed, strict schema version 1 export document.</exception>
    public static TerrainProvenance ReadProvenance(ReadOnlySpan<byte> documentBytes)
    {
        (TerrainProvenance provenance, _) = ParseDocument(documentBytes);
        return provenance;
    }

    /// <summary>
    /// Strictly parses <paramref name="documentBytes"/> and <paramref name="pointsBytes"/> together and
    /// returns the reconstructed <see cref="TerrainExportPayload"/>: the document's recorded SHA-256 must
    /// match <paramref name="pointsBytes"/> exactly, and every points line must parse as three finite,
    /// invariant-culture, round-trippable numbers.
    /// </summary>
    /// <exception cref="TerrainExportException">
    /// The document is not a well-formed, strict schema version 1 export document; the points bytes do not
    /// match the document's recorded SHA-256 or sample count; or a points line is malformed.
    /// </exception>
    public static TerrainExportPayload Read(ReadOnlySpan<byte> documentBytes, ReadOnlySpan<byte> pointsBytes)
    {
        (TerrainProvenance provenance, string expectedSha256) = ParseDocument(documentBytes);

        string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(pointsBytes));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new TerrainExportException("The points file does not match the export document's recorded 'points.sha256' hash.");
        }

        LocalTerrainSample[] samples = ParsePointsBytes(pointsBytes, provenance.RetainedPointCount);

        try
        {
            return new TerrainExportPayload(samples, provenance);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException(
                "The export payload could not be reconstructed from the points file and the provenance record.", ex);
        }
    }

    // ---- document parsing (shared by ReadProvenance and Read) -----------------------------------------

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static (TerrainProvenance Provenance, string PointsSha256) ParseDocument(ReadOnlySpan<byte> documentBytes)
    {
        JsonDocument document;
        try
        {
            // JsonDocument.Parse has no ReadOnlySpan<byte> overload (only ReadOnlyMemory<byte>, ReadOnlySequence<byte>,
            // Stream, and string), so the span is copied once here into an array.
            document = JsonDocument.Parse(documentBytes.ToArray(), DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new TerrainExportException("The export document is not well-formed JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new TerrainExportException("'$' must be a JSON object.");
            }

            Dictionary<string, JsonElement> top = ReadObjectProperties(root, "$", ["schema", "schemaVersion", "provenance", "unitDefinitions", "points"]);

            string schema = RequireString(top["schema"], "$.schema");
            if (!string.Equals(schema, TerrainExportBundleRenderer.DocumentSchema, StringComparison.Ordinal))
            {
                throw new TerrainExportException(
                    $"'$.schema' is '{schema}', but this reader implements only '{TerrainExportBundleRenderer.DocumentSchema}'.");
            }

            int schemaVersion = RequireInt(top["schemaVersion"], "$.schemaVersion");
            if (schemaVersion != TerrainProvenance.CurrentSchemaVersion)
            {
                throw new TerrainExportException(
                    $"'$.schemaVersion' is {schemaVersion.ToString(CultureInfo.InvariantCulture)}, but this reader " +
                    $"implements only schema version {TerrainProvenance.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
            }

            TerrainProvenance provenance = ParseProvenance(top["provenance"], "$.provenance");
            ValidateUnitDefinitions(top["unitDefinitions"], "$.unitDefinitions", TerrainExportBundleRenderer.CollectDistinctUnitsInEnumOrder(provenance));
            string pointsSha256 = ParsePoints(top["points"], "$.points", provenance);

            return (provenance, pointsSha256);
        }
    }

    private static TerrainProvenance ParseProvenance(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path,
            ["source", "horizontalTransformation", "sourceVerticalReference", "localFrame", "simplification", "originalPointCount", "retainedPointCount", "elevationRange"]);

        ElevationSourceMetadata source = ParseSource(props["source"], $"{path}.source");
        HorizontalTransformationDefinition horizontalTransformation = ParseHorizontalTransformation(props["horizontalTransformation"], $"{path}.horizontalTransformation");
        VerticalReference sourceVerticalReference = ParseVerticalReference(props["sourceVerticalReference"], $"{path}.sourceVerticalReference");
        LocalCoordinateFrame localFrame = ParseLocalFrame(props["localFrame"], $"{path}.localFrame");
        SimplificationRequest simplification = ParseSimplificationRequest(props["simplification"], $"{path}.simplification");
        int originalPointCount = RequireInt(props["originalPointCount"], $"{path}.originalPointCount");
        int retainedPointCount = RequireInt(props["retainedPointCount"], $"{path}.retainedPointCount");
        ElevationRange elevationRange = ParseElevationRange(props["elevationRange"], $"{path}.elevationRange");

        try
        {
            return new TerrainProvenance(
                TerrainProvenance.CurrentSchemaVersion,
                source,
                horizontalTransformation,
                sourceVerticalReference,
                localFrame,
                simplification,
                originalPointCount,
                retainedPointCount,
                elevationRange);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid provenance record.", ex);
        }
    }

    private static ElevationSourceMetadata ParseSource(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["sourceName", "datasetIdentifier", "collectionPeriod", "qualityLevel"]);

        string sourceName = RequireString(props["sourceName"], $"{path}.sourceName");
        string datasetIdentifier = RequireString(props["datasetIdentifier"], $"{path}.datasetIdentifier");

        JsonElement collectionPeriodElement = props["collectionPeriod"];
        CollectionPeriod? collectionPeriod = collectionPeriodElement.ValueKind == JsonValueKind.Null
            ? null
            : ParseCollectionPeriod(collectionPeriodElement, $"{path}.collectionPeriod");

        string? qualityLevel = RequireStringOrNull(props["qualityLevel"], $"{path}.qualityLevel");

        try
        {
            return new ElevationSourceMetadata(sourceName, datasetIdentifier, collectionPeriod, qualityLevel);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid source metadata record.", ex);
        }
    }

    private static CollectionPeriod ParseCollectionPeriod(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["start", "end"]);

        DateOnly start = RequireDate(props["start"], $"{path}.start");
        DateOnly end = RequireDate(props["end"], $"{path}.end");

        try
        {
            return new CollectionPeriod(start, end);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid collection period.", ex);
        }
    }

    private static HorizontalTransformationDefinition ParseHorizontalTransformation(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path,
            ["sourceReference", "targetReference", "forwardOperation", "inverseOperation", "engineName", "engineVersion"]);

        HorizontalReference sourceReference = ParseHorizontalReference(props["sourceReference"], $"{path}.sourceReference");
        HorizontalReference targetReference = ParseHorizontalReference(props["targetReference"], $"{path}.targetReference");
        CoordinateOperationDefinition forwardOperation = ParseCoordinateOperationDefinition(props["forwardOperation"], $"{path}.forwardOperation");
        CoordinateOperationDefinition inverseOperation = ParseCoordinateOperationDefinition(props["inverseOperation"], $"{path}.inverseOperation");
        string engineName = RequireString(props["engineName"], $"{path}.engineName");
        string engineVersion = RequireString(props["engineVersion"], $"{path}.engineVersion");

        try
        {
            return new HorizontalTransformationDefinition(sourceReference, targetReference, forwardOperation, inverseOperation, engineName, engineVersion);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid horizontal transformation definition.", ex);
        }
    }

    private static HorizontalReference ParseHorizontalReference(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["coordinateReferenceSystem", "datum", "kind", "unit", "axisOrder"]);

        string coordinateReferenceSystem = RequireString(props["coordinateReferenceSystem"], $"{path}.coordinateReferenceSystem");
        string datum = RequireString(props["datum"], $"{path}.datum");
        HorizontalReferenceKind kind = RequireEnum<HorizontalReferenceKind>(props["kind"], $"{path}.kind");
        HorizontalUnit unit = ParseHorizontalUnit(props["unit"], $"{path}.unit");
        HorizontalAxisOrder axisOrder = RequireEnum<HorizontalAxisOrder>(props["axisOrder"], $"{path}.axisOrder");

        try
        {
            return new HorizontalReference(coordinateReferenceSystem, datum, kind, unit, axisOrder);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid horizontal reference.", ex);
        }
    }

    /// <summary>
    /// Rebuilds a <see cref="HorizontalUnit"/> from its two-property JSON shape. Mirrors
    /// <c>WellKnownTextReferenceParser.InterpretHorizontal</c>'s own Geographic-is-decimal-degrees,
    /// Projected-has-a-linear-unit rule, because <see cref="HorizontalUnit"/> has no public constructor of its
    /// own.
    /// </summary>
    private static HorizontalUnit ParseHorizontalUnit(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["referenceKind", "linearUnit"]);

        HorizontalReferenceKind referenceKind = RequireEnum<HorizontalReferenceKind>(props["referenceKind"], $"{path}.referenceKind");
        LengthUnit? linearUnit = RequireLengthUnitOrNull(props["linearUnit"], $"{path}.linearUnit");

        if (referenceKind == HorizontalReferenceKind.Geographic && linearUnit is null)
        {
            return HorizontalUnit.DecimalDegrees;
        }

        if (referenceKind == HorizontalReferenceKind.Projected && linearUnit is { } value)
        {
            return HorizontalUnit.Linear(value);
        }

        throw new TerrainExportException(
            $"'{path}' combines referenceKind '{referenceKind}' with an incompatible linearUnit; Geographic " +
            "requires a null linearUnit and Projected requires a non-null linearUnit.");
    }

    private static CoordinateOperationDefinition ParseCoordinateOperationDefinition(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["format", "definition"]);

        string format = RequireString(props["format"], $"{path}.format");
        string definition = RequireString(props["definition"], $"{path}.definition");

        try
        {
            return new CoordinateOperationDefinition(format, definition);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid coordinate operation definition.", ex);
        }
    }

    private static VerticalReference ParseVerticalReference(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["datum", "unit", "geoidModel"]);

        string datum = RequireString(props["datum"], $"{path}.datum");
        LengthUnit unit = RequireEnum<LengthUnit>(props["unit"], $"{path}.unit");
        string? geoidModel = RequireStringOrNull(props["geoidModel"], $"{path}.geoidModel");

        try
        {
            return new VerticalReference(datum, unit, geoidModel);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid vertical reference.", ex);
        }
    }

    private static LocalCoordinateFrame ParseLocalFrame(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path,
            ["origin", "projectedHorizontalReference", "verticalReference", "outputUnit"]);

        Coordinate3D origin = ParseOrigin(props["origin"], $"{path}.origin");
        HorizontalReference projectedHorizontalReference = ParseHorizontalReference(props["projectedHorizontalReference"], $"{path}.projectedHorizontalReference");
        VerticalReference verticalReference = ParseVerticalReference(props["verticalReference"], $"{path}.verticalReference");
        LengthUnit outputUnit = RequireEnum<LengthUnit>(props["outputUnit"], $"{path}.outputUnit");

        try
        {
            return new LocalCoordinateFrame(origin, projectedHorizontalReference, verticalReference, outputUnit);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid local coordinate frame.", ex);
        }
    }

    private static Coordinate3D ParseOrigin(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["x", "y", "elevation"]);

        double x = RequireFiniteDouble(props["x"], $"{path}.x");
        double y = RequireFiniteDouble(props["y"], $"{path}.y");
        double elevation = RequireFiniteDouble(props["elevation"], $"{path}.elevation");

        try
        {
            return new Coordinate3D(x, y, elevation);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid coordinate.", ex);
        }
    }

    private static SimplificationRequest ParseSimplificationRequest(JsonElement obj, string path)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["pointBudget", "method"]);

        int pointBudget = RequireInt(props["pointBudget"], $"{path}.pointBudget");
        SimplificationMethod method = RequireEnum<SimplificationMethod>(props["method"], $"{path}.method");

        try
        {
            return new SimplificationRequest(pointBudget, method);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid simplification request.", ex);
        }
    }

    private static ElevationRange ParseElevationRange(JsonElement obj, string path)
    {
        if (obj.ValueKind == JsonValueKind.Null)
        {
            // Schema version 1 always exports a non-null elevation range (see docs/architecture/
            // provenance-and-deterministic-exports.md's "NODATA, empty candidate sets, and statistics"
            // section): the writer rejects an original point count of zero, and a positive original point
            // count always carries a non-null elevation range.
            throw new TerrainExportException($"'{path}' must not be null in schema version 1.");
        }

        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["minimum", "maximum", "unit"]);

        double minimum = RequireFiniteDouble(props["minimum"], $"{path}.minimum");
        double maximum = RequireFiniteDouble(props["maximum"], $"{path}.maximum");
        LengthUnit unit = RequireEnum<LengthUnit>(props["unit"], $"{path}.unit");

        try
        {
            return new ElevationRange(minimum, maximum, unit);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainExportException($"'{path}' is not a valid elevation range.", ex);
        }
    }

    private static void ValidateUnitDefinitions(JsonElement array, string path, List<LengthUnit> expectedUnits)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new TerrainExportException($"'{path}' must be an array.");
        }

        JsonElement[] items = [.. array.EnumerateArray()];
        if (items.Length != expectedUnits.Count)
        {
            throw new TerrainExportException(
                $"'{path}' must contain exactly {expectedUnits.Count.ToString(CultureInfo.InvariantCulture)} unit definition(s).");
        }

        for (int index = 0; index < items.Length; index++)
        {
            string itemPath = $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";
            RequireObject(items[index], itemPath);
            Dictionary<string, JsonElement> props = ReadObjectProperties(items[index], itemPath, ["unit", "metersPerUnit", "definition"]);

            LengthUnit unit = RequireEnum<LengthUnit>(props["unit"], $"{itemPath}.unit");
            double metersPerUnit = RequireFiniteDouble(props["metersPerUnit"], $"{itemPath}.metersPerUnit");
            string definition = RequireString(props["definition"], $"{itemPath}.definition");

            LengthUnit expectedUnit = expectedUnits[index];
            if (unit != expectedUnit)
            {
                throw new TerrainExportException($"'{itemPath}.unit' does not match the computed distinct-unit order.");
            }

            double expectedMetersPerUnit = LengthConverter.MetersPerUnit(expectedUnit);
            if (BitConverter.DoubleToInt64Bits(metersPerUnit) != BitConverter.DoubleToInt64Bits(expectedMetersPerUnit))
            {
                throw new TerrainExportException($"'{itemPath}.metersPerUnit' is not bit-equal to the computed value.");
            }

            string expectedDefinition = TerrainExportBundleRenderer.UnitDefinitionText(expectedUnit);
            if (!string.Equals(definition, expectedDefinition, StringComparison.Ordinal))
            {
                throw new TerrainExportException($"'{itemPath}.definition' does not match the expected definition text.");
            }
        }
    }

    private static string ParsePoints(JsonElement obj, string path, TerrainProvenance provenance)
    {
        RequireObject(obj, path);
        Dictionary<string, JsonElement> props = ReadObjectProperties(obj, path, ["file", "format", "columns", "unit", "count", "sha256"]);

        _ = RequireString(props["file"], $"{path}.file");

        string format = RequireString(props["format"], $"{path}.format");
        if (!string.Equals(format, "csv", StringComparison.Ordinal))
        {
            throw new TerrainExportException($"'{path}.format' is '{format}', but schema version 1 requires 'csv'.");
        }

        ValidateColumns(props["columns"], $"{path}.columns");

        LengthUnit unit = RequireEnum<LengthUnit>(props["unit"], $"{path}.unit");
        if (unit != provenance.LocalFrame.OutputUnit)
        {
            throw new TerrainExportException($"'{path}.unit' does not match the provenance local frame output unit.");
        }

        int count = RequireInt(props["count"], $"{path}.count");
        if (count != provenance.RetainedPointCount)
        {
            throw new TerrainExportException($"'{path}.count' does not match the provenance retained point count.");
        }

        return RequireString(props["sha256"], $"{path}.sha256");
    }

    private static void ValidateColumns(JsonElement array, string path)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new TerrainExportException($"'{path}' must be an array.");
        }

        string[] expected = ["x", "y", "elevation"];
        JsonElement[] items = [.. array.EnumerateArray()];
        if (items.Length != expected.Length)
        {
            throw new TerrainExportException(
                $"'{path}' must contain exactly {expected.Length.ToString(CultureInfo.InvariantCulture)} column name(s).");
        }

        for (int index = 0; index < items.Length; index++)
        {
            string itemPath = $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";
            string value = RequireString(items[index], itemPath);
            if (!string.Equals(value, expected[index], StringComparison.Ordinal))
            {
                throw new TerrainExportException($"'{itemPath}' must be '{expected[index]}'.");
            }
        }
    }

    // ---- points.csv parsing ----------------------------------------------------------------------------

    private static LocalTerrainSample[] ParsePointsBytes(ReadOnlySpan<byte> pointsBytes, int expectedCount)
    {
        if (pointsBytes.Length == 0)
        {
            if (expectedCount != 0)
            {
                throw new TerrainExportException(
                    $"The points file is empty, but {expectedCount.ToString(CultureInfo.InvariantCulture)} sample(s) were expected.");
            }

            return [];
        }

        string text = Encoding.UTF8.GetString(pointsBytes);
        if (text[^1] != '\n')
        {
            throw new TerrainExportException("The points file must end with a line terminator.");
        }

        string[] lines = text[..^1].Split('\n');
        if (lines.Length != expectedCount)
        {
            throw new TerrainExportException(
                $"The points file has {lines.Length.ToString(CultureInfo.InvariantCulture)} line(s), but " +
                $"{expectedCount.ToString(CultureInfo.InvariantCulture)} were expected.");
        }

        LocalTerrainSample[] samples = new LocalTerrainSample[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split(',');
            if (fields.Length != 3)
            {
                throw new TerrainExportException(
                    $"Points file line {(index + 1).ToString(CultureInfo.InvariantCulture)} has " +
                    $"{fields.Length.ToString(CultureInfo.InvariantCulture)} field(s); exactly 3 are required.");
            }

            double x = RequireFiniteCsvField(fields[0], index, "x");
            double y = RequireFiniteCsvField(fields[1], index, "y");
            double elevation = RequireFiniteCsvField(fields[2], index, "elevation");

            try
            {
                samples[index] = new LocalTerrainSample(new LocalCoordinate(x, y, elevation));
            }
            catch (ArgumentException ex)
            {
                throw new TerrainExportException(
                    $"Points file line {(index + 1).ToString(CultureInfo.InvariantCulture)} is not a valid coordinate.", ex);
            }
        }

        return samples;
    }

    private static double RequireFiniteCsvField(string field, int lineIndex, string columnName)
    {
        if (!double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new TerrainExportException(
                $"Points file line {(lineIndex + 1).ToString(CultureInfo.InvariantCulture)} field '{columnName}' " +
                "is not a finite invariant-culture number.");
        }

        return value;
    }

    // ---- low-level JSON value helpers ------------------------------------------------------------------

    private static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new TerrainExportException($"'{path}' must be an object.");
        }
    }

    /// <summary>
    /// Returns <paramref name="obj"/>'s properties keyed by name, after strictly checking that its property
    /// set is exactly <paramref name="expectedNames"/>: no missing property, no unrecognized property, and no
    /// duplicate property name.
    /// </summary>
    private static Dictionary<string, JsonElement> ReadObjectProperties(JsonElement obj, string path, IReadOnlyCollection<string> expectedNames)
    {
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new TerrainExportException($"'{path}' has a duplicate property '{property.Name}'.");
            }
        }

        foreach (string name in properties.Keys)
        {
            if (!expectedNames.Contains(name, StringComparer.Ordinal))
            {
                throw new TerrainExportException($"'{path}' has an unrecognized property '{name}'.");
            }
        }

        foreach (string name in expectedNames)
        {
            if (!properties.ContainsKey(name))
            {
                throw new TerrainExportException($"'{path}' is missing required property '{name}'.");
            }
        }

        return properties;
    }

    private static string RequireString(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new TerrainExportException($"'{path}' must be a string.");
        }

        return value.GetString()!;
    }

    private static string? RequireStringOrNull(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return RequireString(value, path);
    }

    private static int RequireInt(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new TerrainExportException($"'{path}' must be an integer.");
        }

        return result;
    }

    private static double RequireFiniteDouble(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double result) || !double.IsFinite(result))
        {
            throw new TerrainExportException($"'{path}' must be a finite number.");
        }

        return result;
    }

    private static DateOnly RequireDate(JsonElement value, string path)
    {
        string text = RequireString(value, path);
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly result))
        {
            throw new TerrainExportException($"'{path}' is not a valid yyyy-MM-dd date.");
        }

        return result;
    }

    /// <summary>
    /// Requires <paramref name="value"/> to be a JSON string equal to a defined <typeparamref name="TEnum"/>
    /// member name, matched case-sensitively and exactly against <see cref="Enum.GetNames{TEnum}()"/>. This is
    /// deliberately stricter than <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> alone, which also
    /// accepts a defined member's underlying numeric value as a string, whitespace-padded names, and (for an
    /// enum with no <c>[Flags]</c> attribute, exactly like every enum in this manifest) comma-joined name
    /// lists -- any of which would let a corrupted document be silently reinterpreted as a different,
    /// still-valid value instead of being rejected.
    /// </summary>
    private static TEnum RequireEnum<TEnum>(JsonElement value, string path) where TEnum : struct, Enum
    {
        string text = RequireString(value, path);
        if (!Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal))
        {
            throw new TerrainExportException($"'{path}' has an unrecognized value '{text}'.");
        }

        return Enum.Parse<TEnum>(text, ignoreCase: false);
    }

    private static LengthUnit? RequireLengthUnitOrNull(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return RequireEnum<LengthUnit>(value, path);
    }
}
