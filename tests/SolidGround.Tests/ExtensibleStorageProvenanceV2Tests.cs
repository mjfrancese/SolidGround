using SolidGround.Core.Provenance;

namespace SolidGround.Tests;

public sealed class ExtensibleStorageProvenanceV2Tests
{
    [Fact]
    public void V2IsSelfContainedAndLeavesTheV1ContractUntouched()
    {
        Assert.Equal(36, ExtensibleStorageProvenanceSchema.Fields.Count);
        Assert.Equal(2, ExtensibleStorageProvenanceSchemaV2.CurrentVersion);
        Assert.NotEqual(ExtensibleStorageProvenanceSchema.SchemaGuid, ExtensibleStorageProvenanceSchemaV2.SchemaGuid);
        Assert.Equal(36, ExtensibleStorageProvenanceSchemaV2.Fields.Take(36).Count());
        Assert.Equal(63, ExtensibleStorageProvenanceSchemaV2.Fields.Count);
        Assert.All(ExtensibleStorageProvenanceSchemaV2.Fields.Where(field => field.ClrType == typeof(double)),
            field => Assert.True(field.Spec is ProvenanceFieldSpec.Length or ProvenanceFieldSpec.Number));
    }

    [Fact]
    public void V2FieldListIncludesEveryAddressParcelAndDuplicateGuardField()
    {
        HashSet<string> names = ExtensibleStorageProvenanceSchemaV2.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        string[] required =
        [
            "hasAddressParcel", "geocodeQueryText", "parcelSourceIdentity", "stableParcelId", "sourceAttribution",
            "coverageFloorFraction", "collectionPeriodAvailability", "terrainIdentityVersion", "terrainIdentityKind",
            "terrainIdentityStem", "terrainContentSignature", "terrainPointFrameHash", "storedOriginalUniqueId",
            "storedOriginalDocumentCreationGuid",
        ];

        Assert.All(required, name => Assert.Contains(name, names));
        Assert.Equal(ProvenanceFieldSpec.Number, ExtensibleStorageProvenanceSchemaV2.Fields.Single(field => field.Name == "coverageFloorFraction").Spec);
    }
}
