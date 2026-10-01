namespace SolidGround.Core.Provenance;

/// <summary>
/// Immutable self-contained v3 Extensible Storage contract. The v1 and v2 contracts remain untouched and
/// readable; v3 adds serialized building-floor and building-outline context.
/// </summary>
public static class ExtensibleStorageProvenanceSchemaV3
{
    public const string SchemaGuidText = "ffeca007-c97b-4283-9ad1-c2c2f14d4141";
    public static readonly Guid SchemaGuid = Guid.Parse(SchemaGuidText);
    public const int CurrentVersion = 3;
    public const string SchemaName = "SolidGround_Provenance_Toposolid_V3";
    public const string VendorId = ExtensibleStorageProvenanceSchema.VendorId;
    public const string Documentation = "Reversible SolidGround terrain provenance, v3 building-floor/building-outline context, identity guard, address/parcel provenance, and build identity.";

    public static IReadOnlyList<ProvenanceFieldDefinition> Fields { get; } =
    [
        .. ExtensibleStorageProvenanceSchemaV2.Fields,
        new("floorReferenceJson", typeof(string), ProvenanceFieldSpec.None, "Canonical serialized building-floor reference used to place terrain vertically."),
        new("buildingOutlineJson", typeof(string), ProvenanceFieldSpec.None, "Canonical serialized building-outline acquisition attribution."),
    ];
}
