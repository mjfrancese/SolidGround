using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Provenance;

namespace SolidGround.Revit.Provenance;

internal sealed class ExistingTerrainScanException(string message) : InvalidOperationException(message);

/// <summary>Read-only pre-transaction scanner. Malformed or unreadable entities are disclosed and fail closed.</summary>
internal static class ExistingTerrainScanner
{
    internal static (IReadOnlyList<ExistingTerrainRecord> V2, IReadOnlyList<string> LegacyV1) Scan(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Schema? v2 = Schema.Lookup(ExtensibleStorageProvenanceSchemaV2.SchemaGuid);
        Schema? v1 = Schema.Lookup(ExtensibleStorageProvenanceSchema.SchemaGuid);
        List<ExistingTerrainRecord> v2Records = [];
        List<string> legacy = [];
        foreach (Toposolid topography in new FilteredElementCollector(document).OfClass(typeof(Toposolid)).Cast<Toposolid>())
        {
            string elementId = topography.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ICollection<Guid> guids = topography.GetEntitySchemaGuids();
            if (v2 is not null && guids.Contains(v2.GUID))
            {
                Entity entity = topography.GetEntity(v2);
                if (!entity.IsValid() || !entity.ReadAccessGranted())
                {
                    throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} is malformed or unreadable.");
                }

                try
                {
                    if (entity.Get<int>("schemaVersion") != ExtensibleStorageProvenanceSchemaV2.CurrentVersion)
                    {
                        throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} has an unsupported schemaVersion.");
                    }

                    TerrainIdentity identity = new(
                        Require(entity.Get<string>("terrainIdentityVersion"), elementId, "terrainIdentityVersion"),
                        Enum.Parse<TerrainIdentityKind>(Require(entity.Get<string>("terrainIdentityKind"), elementId, "terrainIdentityKind"), ignoreCase: true),
                        Require(entity.Get<string>("terrainIdentityStem"), elementId, "terrainIdentityStem"),
                        Require(entity.Get<string>("terrainContentSignatureAlgorithm"), elementId, "terrainContentSignatureAlgorithm"),
                        Require(entity.Get<string>("terrainContentSignature"), elementId, "terrainContentSignature"),
                        Require(entity.Get<string>("terrainPointFrameHash"), elementId, "terrainPointFrameHash"));
                    string storedNativeFingerprint = Require(entity.Get<string>("nativeVertexFingerprint"), elementId, "nativeVertexFingerprint");
                    string currentNativeFingerprint = ProvenanceEntityWriterV2.NativeVertexFingerprint(topography);
                    if (!string.Equals(storedNativeFingerprint, currentNativeFingerprint, StringComparison.Ordinal))
                    {
                        throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} does not match its current native terrain vertices; it was edited or is stale.");
                    }
                    v2Records.Add(new ExistingTerrainRecord(elementId, identity,
                        Require(entity.Get<string>("storedOriginalUniqueId"), elementId, "storedOriginalUniqueId"), topography.UniqueId,
                        Require(entity.Get<string>("storedOriginalDocumentCreationGuid"), elementId, "storedOriginalDocumentCreationGuid")));
                }
                catch (ExistingTerrainScanException) { throw; }
                catch (Exception exception) when (exception is ArgumentException or Autodesk.Revit.Exceptions.ApplicationException)
                {
                    throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} is malformed: {exception.Message}");
                }
            }

            if (v1 is not null && guids.Contains(v1.GUID))
            {
                Entity entity = topography.GetEntity(v1);
                if (!entity.IsValid() || !entity.ReadAccessGranted())
                {
                    throw new ExistingTerrainScanException($"Legacy SolidGround v1 provenance on element {elementId} is malformed or unreadable.");
                }

                legacy.Add(elementId);
            }
        }

        return (v2Records.AsReadOnly(), legacy.AsReadOnly());
    }

    private static string Require(string value, string elementId, string field) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ExistingTerrainScanException($"SolidGround v2 provenance on element {elementId} has blank '{field}'.");
}
