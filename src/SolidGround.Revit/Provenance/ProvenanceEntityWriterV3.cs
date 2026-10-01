using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Provenance;

/// <summary>Writes only v3 provenance and verifies recognition, readability, every field, and frame reversal before commit.</summary>
internal static class ProvenanceEntityWriterV3
{
    internal static void Attach(Document document, Toposolid toposolid, TerrainExportPayload payload, TerrainIdentity identity, double coverageFloorFraction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(toposolid);
        if (payload.Samples.Count == 0)
        {
            throw new ProvenanceAttachmentException("SolidGround cannot attach v3 provenance without a retained terrain sample for the required source-coordinate read-back check.");
        }

        ExtensibleStorageProvenanceValuesV3 expected = ExtensibleStorageProvenanceValuesV3.From(
            payload, identity, coverageFloorFraction, BuildIdentity.Current.InformationalVersion,
            BuildIdentity.Current.ModuleVersionId, BuildIdentity.Current.Sha256)
            .WithStoredOriginalElementIdentity(toposolid.UniqueId, document.CreationGUID.ToString("D", CultureInfo.InvariantCulture))
            .WithNativeVertexFingerprint(ProvenanceEntityWriterV2.NativeVertexFingerprint(toposolid));
        Schema schema = ProvenanceSchemaAdapterV3.EnsurePublishedSchema();
        Entity entity = new(schema);
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchemaV3.Fields)
        {
            ProvenanceEntityWriterV2.Set(entity, field, ValueFor(expected, field.Name));
        }

        toposolid.SetEntity(entity);
        string elementId = toposolid.Id.Value.ToString(CultureInfo.InvariantCulture);
        if (!toposolid.GetEntitySchemaGuids().Contains(schema.GUID))
        {
            throw new ProvenanceAttachmentException($"SolidGround v3 provenance was not recognized after attachment to element {elementId}.");
        }

        Entity readBack = toposolid.GetEntity(schema);
        if (!readBack.IsValid() || !readBack.ReadAccessGranted())
        {
            throw new ProvenanceAttachmentException($"SolidGround v3 provenance on element {elementId} is invalid or unreadable after attachment.");
        }

        List<string> mismatches = [];
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchemaV3.Fields)
        {
            object expectedValue = ValueFor(expected, field.Name);
            object actualValue = ProvenanceEntityWriterV2.Get(readBack, field);
            if (!ProvenanceEntityWriterV2.ValuesMatch(expectedValue, actualValue, field))
            {
                mismatches.Add(field.Name);
            }
        }

        if (mismatches.Count > 0)
        {
            AddInLog.Error($"SolidGround v3 Extensible Storage read-back mismatches for element {elementId}: {string.Join(", ", mismatches)}.");
            throw new ProvenanceAttachmentException($"SolidGround v3 provenance read-back mismatched {string.Join(", ", mismatches.Take(8))} on element {elementId}.");
        }

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
            throw new ProvenanceAttachmentException($"SolidGround v3 provenance on element {elementId} did not reconstruct the first retained source coordinate within {tolerance.ToString("R", CultureInfo.InvariantCulture)} m.");
        }

        AddInLog.Info($"Attached SolidGround v3 provenance to element {elementId}.");
    }

    private static object ValueFor(ExtensibleStorageProvenanceValuesV3 values, string fieldName) => fieldName switch
    {
        "schemaVersion" => ExtensibleStorageProvenanceSchemaV3.CurrentVersion,
        "floorReferenceJson" => values.FloorReferenceJson,
        "buildingOutlineJson" => values.BuildingOutlineJson,
        _ => ProvenanceEntityWriterV2.ValueFor(values.BaseValues, fieldName),
    };
}
