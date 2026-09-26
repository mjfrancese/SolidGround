using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Exports;

/// <summary>
/// Renders a <see cref="TerrainExportPayload"/> into a deterministic <see cref="TerrainExportBundle"/>: a
/// hand-written, property-order-fixed export document (never a reflection-based serializer) plus a bare
/// points CSV, bound together by a SHA-256 hash and a sample count. Rendering the same payload twice, on any
/// platform or thread culture, produces identical bytes. See
/// docs/architecture/provenance-and-deterministic-exports.md's "Export document manifest, schema version 3",
/// "Points file format, version 1", and "Determinism rules" sections for the full contract this class
/// implements.
/// </summary>
public static class TerrainExportBundleRenderer
{
    /// <summary>The export document's constant <c>schema</c> field value.</summary>
    public const string DocumentSchema = "solidground.terrain-export";

    /// <summary>The file name suffix appended to a base name to form the export document's file name.</summary>
    public const string DocumentFileSuffix = ".solidground.json";

    /// <summary>The file name suffix appended to a base name to form the points file's file name.</summary>
    public const string PointsFileSuffix = ".points.csv";

    /// <summary>
    /// Renders <paramref name="payload"/> into a <see cref="TerrainExportBundle"/> named from
    /// <paramref name="baseName"/>. Pure: the same payload and base name always render identical bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="baseName"/> is empty, contains whitespace, contains a character outside
    /// <c>[A-Za-z0-9._-]</c>, starts with <c>.</c> or <c>-</c>, or already ends with
    /// <see cref="DocumentFileSuffix"/> or <see cref="PointsFileSuffix"/>.
    /// </exception>
    /// <exception cref="TerrainExportException">
    /// <paramref name="payload"/>'s provenance schema version is not <see cref="TerrainProvenance.CurrentSchemaVersion"/>,
    /// or its original point count is zero (nothing to export).
    /// </exception>
    public static TerrainExportBundle Render(TerrainExportPayload payload, string baseName)
    {
        ArgumentNullException.ThrowIfNull(payload);
        TerrainExportBaseName.Validate(baseName, nameof(baseName));

        TerrainProvenance provenance = payload.Provenance;
        if (provenance.SchemaVersion != TerrainProvenance.CurrentSchemaVersion)
        {
            throw new TerrainExportException(
                $"Provenance schema version {provenance.SchemaVersion.ToString(CultureInfo.InvariantCulture)} is not " +
                $"supported; this writer implements only schema version " +
                $"{TerrainProvenance.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (provenance.OriginalPointCount == 0)
        {
            throw new TerrainExportException("Nothing to export: the provenance record's original point count is zero.");
        }

        byte[] pointsBytes = RenderPoints(payload.Samples);
        string pointsFileName = baseName + PointsFileSuffix;
        string documentFileName = baseName + DocumentFileSuffix;
        byte[] documentBytes = RenderDocument(provenance, pointsFileName, pointsBytes);

        return new TerrainExportBundle(documentFileName, documentBytes, pointsFileName, pointsBytes);
    }

    // ---- points file (SolidGround.Core.Exports points.csv, version 1) ---------------------------------

    private static byte[] RenderPoints(IReadOnlyList<LocalTerrainSample> samples)
    {
        StringBuilder builder = new();
        foreach (LocalTerrainSample sample in samples)
        {
            builder
                .Append(sample.Position.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Position.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Position.Elevation.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    // ---- export document --------------------------------------------------------------------------------

    private static byte[] RenderDocument(TerrainProvenance provenance, string pointsFileName, byte[] pointsBytes)
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
            writer.WriteString("schema", DocumentSchema);
            writer.WriteNumber("schemaVersion", provenance.SchemaVersion);

            writer.WritePropertyName("provenance");
            WriteProvenance(writer, provenance);

            writer.WritePropertyName("unitDefinitions");
            WriteUnitDefinitions(writer, CollectDistinctUnitsInEnumOrder(provenance));

            writer.WritePropertyName("points");
            WritePoints(writer, provenance, pointsFileName, pointsBytes);

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteProvenance(Utf8JsonWriter writer, TerrainProvenance provenance)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("source");
        WriteSource(writer, provenance.Source);

        writer.WritePropertyName("horizontalTransformation");
        WriteHorizontalTransformation(writer, provenance.HorizontalTransformation);

        writer.WritePropertyName("sourceVerticalReference");
        WriteVerticalReference(writer, provenance.SourceVerticalReference);

        writer.WriteString("sourceHorizontalReferenceOrigin", provenance.SourceHorizontalReferenceOrigin.ToString());
        writer.WriteString("sourceVerticalReferenceOrigin", provenance.SourceVerticalReferenceOrigin.ToString());

        writer.WritePropertyName("localFrame");
        WriteLocalFrame(writer, provenance.LocalFrame);

        writer.WritePropertyName("simplification");
        WriteSimplificationRequest(writer, provenance.SimplificationRequest);

        writer.WriteNumber("originalPointCount", provenance.OriginalPointCount);
        writer.WriteNumber("retainedPointCount", provenance.RetainedPointCount);

        writer.WritePropertyName("elevationRange");

        // Never null here: Render already rejected OriginalPointCount == 0 above, and TerrainProvenance's own
        // constructor requires a non-null ElevationRange whenever OriginalPointCount is positive.
        WriteElevationRange(writer, provenance.ElevationRange!);

        writer.WritePropertyName("addressParcel");
        if (provenance.AddressParcel is { } addressParcel)
        {
            WriteAddressParcelProvenance(writer, addressParcel);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
    }

    private static void WriteSource(Utf8JsonWriter writer, ElevationSourceMetadata source)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceName", source.SourceName);
        writer.WriteString("datasetIdentifier", source.DatasetIdentifier);

        writer.WritePropertyName("collectionPeriod");
        if (source.CollectionPeriod is { } period)
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

        if (source.QualityLevel is { } qualityLevel)
        {
            writer.WriteString("qualityLevel", qualityLevel);
        }
        else
        {
            writer.WriteNull("qualityLevel");
        }

        writer.WriteEndObject();
    }

    private static void WriteHorizontalTransformation(Utf8JsonWriter writer, HorizontalTransformationDefinition transformation)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("sourceReference");
        WriteHorizontalReference(writer, transformation.SourceReference);

        writer.WritePropertyName("targetReference");
        WriteHorizontalReference(writer, transformation.TargetReference);

        writer.WritePropertyName("forwardOperation");
        WriteCoordinateOperationDefinition(writer, transformation.ForwardOperation);

        writer.WritePropertyName("inverseOperation");
        WriteCoordinateOperationDefinition(writer, transformation.InverseOperation);

        writer.WriteString("engineName", transformation.EngineName);
        writer.WriteString("engineVersion", transformation.EngineVersion);

        writer.WriteEndObject();
    }

    private static void WriteHorizontalReference(Utf8JsonWriter writer, HorizontalReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString("coordinateReferenceSystem", reference.CoordinateReferenceSystem);
        writer.WriteString("datum", reference.Datum);
        writer.WriteString("kind", reference.Kind.ToString());

        writer.WritePropertyName("unit");
        WriteHorizontalUnit(writer, reference.Unit);

        writer.WriteString("axisOrder", reference.AxisOrder.ToString());
        writer.WriteEndObject();
    }

    private static void WriteHorizontalUnit(Utf8JsonWriter writer, HorizontalUnit unit)
    {
        writer.WriteStartObject();
        writer.WriteString("referenceKind", unit.ReferenceKind.ToString());
        if (unit.LinearUnit is { } linearUnit)
        {
            writer.WriteString("linearUnit", linearUnit.ToString());
        }
        else
        {
            writer.WriteNull("linearUnit");
        }

        writer.WriteEndObject();
    }

    private static void WriteCoordinateOperationDefinition(Utf8JsonWriter writer, CoordinateOperationDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("format", definition.Format);
        writer.WriteString("definition", definition.Definition);
        writer.WriteEndObject();
    }

    private static void WriteVerticalReference(Utf8JsonWriter writer, VerticalReference reference)
    {
        writer.WriteStartObject();
        writer.WriteString("datum", reference.Datum);
        writer.WriteString("unit", reference.Unit.ToString());
        if (reference.GeoidModel is { } geoidModel)
        {
            writer.WriteString("geoidModel", geoidModel);
        }
        else
        {
            writer.WriteNull("geoidModel");
        }

        writer.WriteEndObject();
    }

    private static void WriteLocalFrame(Utf8JsonWriter writer, LocalCoordinateFrame localFrame)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("origin");
        writer.WriteStartObject();
        writer.WriteNumber("x", localFrame.Origin.X);
        writer.WriteNumber("y", localFrame.Origin.Y);
        writer.WriteNumber("elevation", localFrame.Origin.Elevation);
        writer.WriteEndObject();

        writer.WritePropertyName("projectedHorizontalReference");
        WriteHorizontalReference(writer, localFrame.ProjectedHorizontalReference);

        writer.WritePropertyName("verticalReference");
        WriteVerticalReference(writer, localFrame.VerticalReference);

        writer.WriteString("outputUnit", localFrame.OutputUnit.ToString());

        writer.WriteEndObject();
    }

    private static void WriteSimplificationRequest(Utf8JsonWriter writer, SimplificationRequest request)
    {
        writer.WriteStartObject();
        writer.WriteNumber("pointBudget", request.PointBudget);
        writer.WriteString("method", request.Method.ToString());
        writer.WriteEndObject();
    }

    private static void WriteElevationRange(Utf8JsonWriter writer, ElevationRange range)
    {
        writer.WriteStartObject();
        writer.WriteNumber("minimum", range.Minimum);
        writer.WriteNumber("maximum", range.Maximum);
        writer.WriteString("unit", range.Unit.ToString());
        writer.WriteEndObject();
    }

    private static void WriteAddressParcelProvenance(Utf8JsonWriter writer, AddressParcelProvenance addressParcel)
    {
        writer.WriteStartObject();
        writer.WriteString("retrievalDate", addressParcel.RetrievalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        writer.WritePropertyName("geocode");
        if (addressParcel.Geocode is { } geocode)
        {
            writer.WriteStartObject();
            writer.WriteString("provider", geocode.Provider.ToString());
            writer.WriteString("queryText", geocode.QueryText);
            writer.WriteString("attribution", geocode.Attribution);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName("parcel");
        if (addressParcel.Parcel is { } parcel)
        {
            writer.WriteStartObject();
            writer.WriteString("sourceKind", parcel.SourceKind.ToString());
            writer.WriteString("sourceIdentity", parcel.SourceIdentity);
            writer.WriteString("parcelId", parcel.ParcelId);
            if (parcel.StableParcelId is { } stableParcelId)
            {
                writer.WriteString("stableParcelId", stableParcelId);
            }
            else
            {
                writer.WriteNull("stableParcelId");
            }

            if (parcel.LegalDescription is { } legalDescription)
            {
                writer.WriteString("legalDescription", legalDescription);
            }
            else
            {
                writer.WriteNull("legalDescription");
            }

            writer.WriteString("licenseDisclaimerText", parcel.LicenseDisclaimerText);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
    }

    private static void WriteUnitDefinitions(Utf8JsonWriter writer, List<LengthUnit> units)
    {
        writer.WriteStartArray();
        foreach (LengthUnit unit in units)
        {
            writer.WriteStartObject();
            writer.WriteString("unit", unit.ToString());
            writer.WriteNumber("metersPerUnit", LengthConverter.MetersPerUnit(unit));
            writer.WriteString("definition", UnitDefinitionText(unit));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePoints(Utf8JsonWriter writer, TerrainProvenance provenance, string pointsFileName, byte[] pointsBytes)
    {
        writer.WriteStartObject();
        writer.WriteString("file", pointsFileName);
        writer.WriteString("format", "csv");

        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        writer.WriteStringValue("x");
        writer.WriteStringValue("y");
        writer.WriteStringValue("elevation");
        writer.WriteEndArray();

        writer.WriteString("unit", provenance.LocalFrame.OutputUnit.ToString());
        writer.WriteNumber("count", provenance.RetainedPointCount);
        writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(pointsBytes)));
        writer.WriteEndObject();
    }

    // ---- helpers shared with TerrainExportBundleReader, so the writer and the strict reader can never disagree ----

    /// <summary>
    /// Every distinct <see cref="LengthUnit"/> that appears anywhere in <paramref name="provenance"/>,
    /// ordered by the enum's underlying integer value.
    /// </summary>
    internal static List<LengthUnit> CollectDistinctUnitsInEnumOrder(TerrainProvenance provenance)
    {
        HashSet<LengthUnit> distinct = [];

        AddHorizontalUnit(distinct, provenance.HorizontalTransformation.SourceReference);
        AddHorizontalUnit(distinct, provenance.HorizontalTransformation.TargetReference);
        distinct.Add(provenance.SourceVerticalReference.Unit);
        AddHorizontalUnit(distinct, provenance.LocalFrame.ProjectedHorizontalReference);
        distinct.Add(provenance.LocalFrame.VerticalReference.Unit);
        distinct.Add(provenance.LocalFrame.OutputUnit);
        if (provenance.ElevationRange is { } range)
        {
            distinct.Add(range.Unit);
        }

        return [.. distinct.OrderBy(unit => (int)unit)];
    }

    private static void AddHorizontalUnit(HashSet<LengthUnit> distinct, HorizontalReference reference)
    {
        if (reference.Unit.LinearUnit is { } linearUnit)
        {
            distinct.Add(linearUnit);
        }
    }

    /// <summary>
    /// The exact, human-readable definition text for a <see cref="LengthUnit"/> member, matching
    /// <see cref="LengthConverter.MetersPerUnit(LengthUnit)"/>'s literal values.
    /// </summary>
    internal static string UnitDefinitionText(LengthUnit unit) => unit switch
    {
        LengthUnit.Meter => "1 m",
        LengthUnit.UsSurveyFoot => "1200/3937 m",
        LengthUnit.InternationalFoot => "0.3048 m",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit."),
    };
}

/// <summary>
/// Validates a bundle or export base file name -- the one rule shared by
/// <see cref="TerrainExportBundleRenderer.Render"/> and <see cref="FileSystemTerrainExporter"/>, so the two
/// can never accept a name the other would reject. <c>internal</c> -&gt; <c>public</c> for SolidGround
/// Issue #15: <c>SolidGround.Core.Processing.TerrainRequestSettings.Validate</c> reuses this exact rule for
/// <c>output.baseName</c> rather than re-deriving it (see the design record's file-level plan, row 18).
/// </summary>
public static class TerrainExportBaseName
{
    public static void Validate(string baseName, string paramName)
    {
        if (string.IsNullOrEmpty(baseName) || baseName.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A base name must be non-empty and cannot contain whitespace.", paramName);
        }

        if (!baseName.All(IsAllowedCharacter))
        {
            throw new ArgumentException("A base name can contain only ASCII letters, digits, '.', '_', and '-'.", paramName);
        }

        if (baseName[0] is '.' or '-')
        {
            throw new ArgumentException("A base name cannot start with '.' or '-'.", paramName);
        }

        if (baseName.EndsWith(TerrainExportBundleRenderer.DocumentFileSuffix, StringComparison.Ordinal)
            || baseName.EndsWith(TerrainExportBundleRenderer.PointsFileSuffix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A base name cannot already end with the document or points file suffix.", paramName);
        }
    }

    private static bool IsAllowedCharacter(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-';
}
