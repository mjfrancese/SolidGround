using System.Globalization;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Provenance;

/// <summary>Writes only v2 provenance and immediately verifies every field before transaction commit.</summary>
internal static class ProvenanceEntityWriterV2
{
    internal static void Attach(Document document, Toposolid toposolid, TerrainExportPayload payload, TerrainIdentity identity, double coverageFloorFraction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(toposolid);
        if (payload.Samples.Count == 0)
        {
            throw new ProvenanceAttachmentException("SolidGround cannot attach v2 provenance without a retained terrain sample for the required source-coordinate read-back check.");
        }
        ExtensibleStorageProvenanceValuesV2 expected = ExtensibleStorageProvenanceValuesV2.From(
            payload, identity, coverageFloorFraction, BuildIdentity.Current.InformationalVersion,
            BuildIdentity.Current.ModuleVersionId, BuildIdentity.Current.Sha256)
            .WithStoredOriginalElementIdentity(toposolid.UniqueId, document.CreationGUID.ToString("D", CultureInfo.InvariantCulture))
            .WithNativeVertexFingerprint(NativeVertexFingerprint(toposolid));
        Schema schema = ProvenanceSchemaAdapterV2.EnsurePublishedSchema();
        Entity entity = new(schema);
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchemaV2.Fields)
        {
            Set(entity, field, ValueFor(expected, field.Name));
        }

        toposolid.SetEntity(entity);
        string elementId = toposolid.Id.Value.ToString(CultureInfo.InvariantCulture);
        if (!toposolid.GetEntitySchemaGuids().Contains(schema.GUID))
        {
            throw new ProvenanceAttachmentException($"SolidGround v2 provenance was not recognized after attachment to element {elementId}.");
        }

        Entity readBack = toposolid.GetEntity(schema);
        if (!readBack.IsValid() || !readBack.ReadAccessGranted())
        {
            throw new ProvenanceAttachmentException($"SolidGround v2 provenance on element {elementId} is invalid or unreadable after attachment.");
        }

        List<string> mismatches = [];
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchemaV2.Fields)
        {
            object expectedValue = ValueFor(expected, field.Name);
            object actualValue = Get(readBack, field);
            if (!ValuesMatch(expectedValue, actualValue, field))
            {
                mismatches.Add(field.Name);
            }
        }

        if (mismatches.Count > 0)
        {
            AddInLog.Error($"SolidGround v2 Extensible Storage read-back mismatches for element {elementId}: {string.Join(", ", mismatches)}.");
            throw new ProvenanceAttachmentException($"SolidGround v2 provenance read-back mismatched {string.Join(", ", mismatches.Take(8))} on element {elementId}.");
        }

        // Fourth read-back check: reconstruct a real retained source coordinate from the values just read,
        // rather than trusting a field-by-field equality check to prove the frame remains reversible.
        LocalCoordinate sample = payload.Samples[0].Position;
        double metersPerOutputUnit = readBack.Get<double>("metersPerOutputUnit", UnitTypeId.General);
        Coordinate3D reconstructed = new(
            readBack.Get<double>("localOriginXMeters", UnitTypeId.Meters) + (sample.X * metersPerOutputUnit),
            readBack.Get<double>("localOriginYMeters", UnitTypeId.Meters) + (sample.Y * metersPerOutputUnit),
            readBack.Get<double>("localOriginElevationMeters", UnitTypeId.Meters) + (sample.Elevation * metersPerOutputUnit));
        Coordinate3D source = ExtensibleStorageProvenanceValues.ToSourceMeters(payload.Provenance.LocalFrame, sample);
        double tolerance = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();
        if (Math.Abs(reconstructed.X - source.X) > tolerance || Math.Abs(reconstructed.Y - source.Y) > tolerance || Math.Abs(reconstructed.Elevation - source.Elevation) > tolerance)
        {
            throw new ProvenanceAttachmentException($"SolidGround v2 provenance on element {elementId} did not reconstruct the first retained source coordinate within {tolerance.ToString("R", CultureInfo.InvariantCulture)} m.");
        }

        AddInLog.Info($"Attached SolidGround v2 provenance to element {elementId}.");
    }

    internal static object ValueFor(ExtensibleStorageProvenanceValuesV2 values, string fieldName)
    {
        if (fieldName == "schemaVersion")
        {
            return ExtensibleStorageProvenanceSchemaV2.CurrentVersion;
        }

        object? identityValue = fieldName switch
        {
            "terrainIdentityVersion" => values.Identity.Version,
            "terrainIdentityKind" => values.Identity.Kind.ToString(),
            "terrainIdentityStem" => values.Identity.Stem,
            "terrainContentSignatureAlgorithm" => values.Identity.ContentSignatureAlgorithm,
            "terrainContentSignature" => values.Identity.ContentSignature,
            "terrainPointFrameHash" => values.Identity.PointFrameHash,
            _ => null,
        };
        if (identityValue is not null)
        {
            return identityValue;
        }

        PropertyInfo? property = typeof(ExtensibleStorageProvenanceValuesV2).GetProperty(ToPropertyName(fieldName));
        if (property is not null)
        {
            return property.GetValue(values) ?? string.Empty;
        }

        PropertyInfo? baseProperty = typeof(ExtensibleStorageProvenanceValues).GetProperty(ToPropertyName(fieldName));
        return baseProperty?.GetValue(values.BaseValues) ?? throw new ProvenanceAttachmentException($"No v2 provenance value maps to field '{fieldName}'.");
    }

    internal static void Set(Entity entity, ProvenanceFieldDefinition field, object value)
    {
        switch (value)
        {
            case int integer: entity.Set(field.Name, integer); break;
            case bool boolean: entity.Set(field.Name, boolean); break;
            case string text: entity.Set(field.Name, text); break;
            case double number when field.Spec == ProvenanceFieldSpec.Length: entity.Set(field.Name, number, UnitTypeId.Meters); break;
            case double number when field.Spec == ProvenanceFieldSpec.Number: entity.Set(field.Name, number, UnitTypeId.General); break;
            default: throw new ProvenanceAttachmentException($"V2 provenance field '{field.Name}' has an unsupported value/spec combination.");
        }
    }

    internal static object Get(Entity entity, ProvenanceFieldDefinition field) => field.ClrType == typeof(int) ? entity.Get<int>(field.Name)
        : field.ClrType == typeof(bool) ? entity.Get<bool>(field.Name)
        : field.ClrType == typeof(string) ? entity.Get<string>(field.Name)
        : field.Spec == ProvenanceFieldSpec.Length ? entity.Get<double>(field.Name, UnitTypeId.Meters)
        : entity.Get<double>(field.Name, UnitTypeId.General);

    internal static bool ValuesMatch(object expected, object actual, ProvenanceFieldDefinition field)
    {
        if (expected is not double expectedNumber || actual is not double actualNumber)
        {
            return Equals(expected, actual);
        }

        double tolerance = field.Spec == ProvenanceFieldSpec.Length
            ? ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters()
            : ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();
        return Math.Abs(expectedNumber - actualNumber) <= tolerance;
    }

    internal static string NativeVertexFingerprint(Toposolid toposolid)
    {
        SlabShapeEditor editor = toposolid.GetSlabShapeEditor();
        if (!editor.IsEnabled)
        {
            throw new ProvenanceAttachmentException("SolidGround cannot fingerprint an enabled-to-verify toposolid whose slab shape editor is disabled.");
        }
        List<Coordinate3D> vertices = [];
        for (int index = 0; index < editor.SlabShapeVertices.Size; index++)
        {
            XYZ point = editor.SlabShapeVertices.get_Item(index).Position;
            vertices.Add(new Coordinate3D(point.X, point.Y, point.Z));
        }
        Element? sketchElement = toposolid.Document.GetElement(toposolid.SketchId);
        if (sketchElement is not Sketch sketch)
        {
            throw new ProvenanceAttachmentException("SolidGround cannot fingerprint the created toposolid because its native Sketch/Profile is unavailable.");
        }
        List<string> profile = [];
        int loopIndex = 0;
        foreach (CurveArray loop in sketch.Profile)
        {
            int curveIndex = 0;
            foreach (Curve curve in loop)
            {
                IList<XYZ> tessellation = curve.Tessellate();
                profile.Add($"loop:{loopIndex};curve:{curveIndex};type:{curve.GetType().FullName};points:" + string.Join("|", tessellation.Select(point => string.Create(CultureInfo.InvariantCulture, $"{point.X:R},{point.Y:R},{point.Z:R}"))));
                curveIndex++;
            }
            loopIndex++;
        }
        return TerrainVertexFingerprint.Compute(vertices, profile);
    }

    private static string ToPropertyName(string fieldName) => char.ToUpperInvariant(fieldName[0]) + fieldName[1..];
}
