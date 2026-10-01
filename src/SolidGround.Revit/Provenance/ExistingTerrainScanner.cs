using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Provenance;

internal sealed class ExistingTerrainScanException(string message) : InvalidOperationException(message);

/// <summary>
/// Read-only pre-transaction scanner. Malformed, unreadable, stale, or multiply-versioned SolidGround
/// provenance is disclosed and fails closed. The V2 result name remains the modern-record decision seam;
/// it deliberately includes validated v3 records because their terrain identity contract is unchanged.
/// </summary>
internal static class ExistingTerrainScanner
{
    internal static (IReadOnlyList<ExistingTerrainRecord> V2, IReadOnlyList<string> LegacyV1) Scan(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Schema? v3 = Schema.Lookup(ExtensibleStorageProvenanceSchemaV3.SchemaGuid);
        if (v3 is not null)
        {
            ProvenanceSchemaAdapterV3.RequireSchema(v3);
        }

        Schema? v2 = Schema.Lookup(ExtensibleStorageProvenanceSchemaV2.SchemaGuid);
        if (v2 is not null)
        {
            ProvenanceSchemaAdapterV2.RequireSchema(v2);
        }

        Schema? v1 = Schema.Lookup(ExtensibleStorageProvenanceSchema.SchemaGuid);
        if (v1 is not null)
        {
            ProvenanceSchemaAdapter.RequireSchema(v1);
        }
        List<ExistingTerrainRecord> modernRecords = [];
        List<string> legacy = [];
        foreach (Toposolid topography in new FilteredElementCollector(document).OfClass(typeof(Toposolid)).Cast<Toposolid>())
        {
            string elementId = topography.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ICollection<Guid> guids = topography.GetEntitySchemaGuids();
            int recognized = CountRecognized(guids);
            if (recognized > 1)
            {
                throw new ExistingTerrainScanException($"SolidGround provenance on element {elementId} contains more than one recognized schema version and cannot be interpreted safely.");
            }

            if (guids.Contains(ExtensibleStorageProvenanceSchemaV3.SchemaGuid))
            {
                if (v3 is null)
                {
                    throw new ExistingTerrainScanException($"SolidGround v3 provenance on element {elementId} is registered but its schema is unavailable.");
                }
                modernRecords.Add(ReadModern(topography, v3, ExtensibleStorageProvenanceSchemaV3.CurrentVersion, "v3", validateV3Context: true));
            }
            else if (guids.Contains(ExtensibleStorageProvenanceSchemaV2.SchemaGuid))
            {
                if (v2 is null)
                {
                    throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} is registered but its schema is unavailable.");
                }
                modernRecords.Add(ReadModern(topography, v2, ExtensibleStorageProvenanceSchemaV2.CurrentVersion, "v2", validateV3Context: false));
            }
            else if (guids.Contains(ExtensibleStorageProvenanceSchema.SchemaGuid))
            {
                if (v1 is null)
                {
                    throw new ExistingTerrainScanException($"Legacy SolidGround v1 provenance on element {elementId} is registered but its schema is unavailable.");
                }
                try
                {
                    Entity entity = topography.GetEntity(v1);
                    if (!entity.IsValid() || !entity.ReadAccessGranted())
                    {
                        throw new ExistingTerrainScanException($"Legacy SolidGround v1 provenance on element {elementId} is malformed or unreadable.");
                    }
                    if (entity.Get<int>("schemaVersion") != ExtensibleStorageProvenanceSchema.CurrentVersion)
                    {
                        throw new ExistingTerrainScanException($"Legacy SolidGround v1 provenance on element {elementId} has an unsupported schemaVersion.");
                    }

                    legacy.Add(elementId);
                }
                catch (ExistingTerrainScanException) { throw; }
                catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException)
                {
                    throw new ExistingTerrainScanException($"Legacy SolidGround v1 provenance on element {elementId} is malformed: {exception.Message}");
                }
            }
        }

        return (modernRecords.AsReadOnly(), legacy.AsReadOnly());
    }

    private static ExistingTerrainRecord ReadModern(Toposolid topography, Schema schema, int expectedVersion, string versionLabel, bool validateV3Context)
    {
        string elementId = topography.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Entity entity = topography.GetEntity(schema);
        if (!entity.IsValid() || !entity.ReadAccessGranted())
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} is malformed or unreadable.");
        }

        try
        {
            if (entity.Get<int>("schemaVersion") != expectedVersion)
            {
                throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has an unsupported schemaVersion.");
            }

            TerrainIdentity identity = new(
                Require(entity.Get<string>("terrainIdentityVersion"), elementId, versionLabel, "terrainIdentityVersion"),
                ExistingTerrainRecordValidator.ParseKind(Require(entity.Get<string>("terrainIdentityKind"), elementId, versionLabel, "terrainIdentityKind")),
                Require(entity.Get<string>("terrainIdentityStem"), elementId, versionLabel, "terrainIdentityStem"),
                Require(entity.Get<string>("terrainContentSignatureAlgorithm"), elementId, versionLabel, "terrainContentSignatureAlgorithm"),
                Require(entity.Get<string>("terrainContentSignature"), elementId, versionLabel, "terrainContentSignature"),
                Require(entity.Get<string>("terrainPointFrameHash"), elementId, versionLabel, "terrainPointFrameHash"));
            string storedOriginalUniqueId = Require(entity.Get<string>("storedOriginalUniqueId"), elementId, versionLabel, "storedOriginalUniqueId");
            string storedOriginalDocumentCreationGuid = Require(entity.Get<string>("storedOriginalDocumentCreationGuid"), elementId, versionLabel, "storedOriginalDocumentCreationGuid");
            bool hasFloorOrContext = validateV3Context && ValidateV3Context(entity, elementId, versionLabel);
            ExistingTerrainRecordValidator.Validate(identity, hasFloorOrContext, storedOriginalUniqueId, storedOriginalDocumentCreationGuid);

            string storedNativeFingerprint = Require(entity.Get<string>("nativeVertexFingerprint"), elementId, versionLabel, "nativeVertexFingerprint");
            string currentNativeFingerprint = ProvenanceEntityWriterV2.NativeVertexFingerprint(topography);
            if (!string.Equals(storedNativeFingerprint, currentNativeFingerprint, StringComparison.Ordinal))
            {
                throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} does not match its current native terrain vertices; it was edited or is stale.");
            }

            return new ExistingTerrainRecord(elementId, identity, storedOriginalUniqueId, topography.UniqueId, storedOriginalDocumentCreationGuid);
        }
        catch (ExistingTerrainScanException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or System.Text.Json.JsonException or Autodesk.Revit.Exceptions.ApplicationException)
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} is malformed: {exception.Message}");
        }
    }

    private static bool ValidateV3Context(Entity entity, string elementId, string versionLabel)
    {
        string floorJson = Require(entity.Get<string>("floorReferenceJson"), elementId, versionLabel, "floorReferenceJson");
        string outlineJson = Require(entity.Get<string>("buildingOutlineJson"), elementId, versionLabel, "buildingOutlineJson");
        BuildingFloorReference? floorReference = BuildingFloorReferenceJson.Deserialize(floorJson);
        BuildingOutlineProvenance? buildingOutline = BuildingOutlineProvenanceJson.Deserialize(outlineJson);
        if (floorReference is null && buildingOutline is null)
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has neither floor-reference nor building-outline context.");
        }

        if (!string.Equals(BuildingFloorReferenceJson.Serialize(floorReference), floorJson, StringComparison.Ordinal) ||
            !string.Equals(BuildingOutlineProvenanceJson.Serialize(buildingOutline), outlineJson, StringComparison.Ordinal))
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has noncanonical floor or building-outline context JSON.");
        }

        if (floorReference?.BuildingOutline is { } embeddedOutline &&
            (buildingOutline is null || !BuildingOutlineProvenanceJson.HasSameValue(embeddedOutline, buildingOutline)))
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has conflicting floor and standalone building-outline context.");
        }

        if (floorReference is null)
        {
            return true;
        }

        if (!string.Equals(floorReference.SourceReference.Datum, entity.Get<string>("verticalDatum"), StringComparison.Ordinal))
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has a floor reference whose vertical datum differs from its stored terrain metadata.");
        }
        bool hasStoredGeoid = entity.Get<bool>("hasVerticalGeoidModel");
        string storedGeoid = entity.Get<string>("verticalGeoidModel");
        if (hasStoredGeoid != (floorReference.SourceReference.GeoidModel is not null) ||
            (hasStoredGeoid && !string.Equals(floorReference.SourceReference.GeoidModel, storedGeoid, StringComparison.Ordinal)))
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has a floor reference whose geoid metadata differs from its stored terrain metadata.");
        }

        double targetLevelInSourceUnit = LengthConverter.Convert(
            floorReference.TargetLevel.ProjectElevationInternal,
            LengthUnit.InternationalFoot,
            floorReference.SourceReference.Unit);
        double frameElevationMeters = LengthConverter.Convert(
            floorReference.ResolveFrameReferenceElevation() - targetLevelInSourceUnit,
            floorReference.SourceReference.Unit,
            LengthUnit.Meter);
        double storedFrameElevationMeters = entity.Get<double>("localOriginElevationMeters", UnitTypeId.Meters);
        double tolerance = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();
        if (!double.IsFinite(frameElevationMeters) || Math.Abs(frameElevationMeters - storedFrameElevationMeters) > tolerance)
        {
            throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has a floor reference that does not reproduce its stored local-frame elevation origin.");
        }

        return true;
    }

    private static int CountRecognized(ICollection<Guid> guids) =>
        (guids.Contains(ExtensibleStorageProvenanceSchemaV3.SchemaGuid) ? 1 : 0) +
        (guids.Contains(ExtensibleStorageProvenanceSchemaV2.SchemaGuid) ? 1 : 0) +
        (guids.Contains(ExtensibleStorageProvenanceSchema.SchemaGuid) ? 1 : 0);

    private static string Require(string value, string elementId, string versionLabel, string field) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ExistingTerrainScanException($"SolidGround {versionLabel} provenance on element {elementId} has blank '{field}'.");
}
