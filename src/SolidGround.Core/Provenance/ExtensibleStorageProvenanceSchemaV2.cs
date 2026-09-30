namespace SolidGround.Core.Provenance;

/// <summary>Immutable self-contained v2 Extensible Storage contract. V1 remains untouched and readable.</summary>
public static class ExtensibleStorageProvenanceSchemaV2
{
    public const string SchemaGuidText = "a1ca95f5-2bf4-4b9b-a7f1-2b8b4f0fcd3a";
    public static readonly Guid SchemaGuid = Guid.Parse(SchemaGuidText);
    public const int CurrentVersion = 2;
    public const string SchemaName = "SolidGround_Provenance_Toposolid_V2";
    public const string VendorId = ExtensibleStorageProvenanceSchema.VendorId;
    public const string Documentation = "Reversible SolidGround terrain provenance, v2 identity guard, address/parcel provenance, and build identity.";

    public static IReadOnlyList<ProvenanceFieldDefinition> Fields { get; } =
    [
        .. ExtensibleStorageProvenanceSchema.Fields,
        new("hasAddressParcel", typeof(bool), ProvenanceFieldSpec.None, "Whether address or parcel provenance is present."),
        new("addressParcelRetrievalDateIso", typeof(string), ProvenanceFieldSpec.None, "Address/parcel lookup date in ISO 8601 format, or empty."),
        new("hasGeocode", typeof(bool), ProvenanceFieldSpec.None, "Whether address geocoding provenance is present."),
        new("geocodeProvider", typeof(string), ProvenanceFieldSpec.None, "Address geocoder provider, or empty."),
        new("geocodeQueryText", typeof(string), ProvenanceFieldSpec.None, "Operator address query text, or empty."),
        new("geocodeAttribution", typeof(string), ProvenanceFieldSpec.None, "Geocoder attribution, or empty."),
        new("hasParcel", typeof(bool), ProvenanceFieldSpec.None, "Whether parcel provenance is present."),
        new("parcelSourceKind", typeof(string), ProvenanceFieldSpec.None, "Parcel source kind, or empty."),
        new("parcelSourceIdentity", typeof(string), ProvenanceFieldSpec.None, "Parcel source identity, or empty."),
        new("parcelId", typeof(string), ProvenanceFieldSpec.None, "Parcel source identifier, or empty."),
        new("hasStableParcelId", typeof(bool), ProvenanceFieldSpec.None, "Whether a durable parcel id is present."),
        new("stableParcelId", typeof(string), ProvenanceFieldSpec.None, "Durable parcel id, or empty."),
        new("hasLegalDescription", typeof(bool), ProvenanceFieldSpec.None, "Whether a legal description is present."),
        new("legalDescription", typeof(string), ProvenanceFieldSpec.None, "Legal description, or empty."),
        new("parcelLicenseDisclaimerText", typeof(string), ProvenanceFieldSpec.None, "Parcel license/disclaimer text, or empty."),
        new("hasSourceAttribution", typeof(bool), ProvenanceFieldSpec.None, "Whether elevation-source attribution is present."),
        new("sourceAttribution", typeof(string), ProvenanceFieldSpec.None, "Elevation-source attribution, or empty."),
        new("coverageFloorFraction", typeof(double), ProvenanceFieldSpec.Number, "Configured coverage floor fraction."),
        new("collectionPeriodAvailability", typeof(string), ProvenanceFieldSpec.None, "reported or notReportedBySource."),
        new("terrainIdentityVersion", typeof(string), ProvenanceFieldSpec.None, "Canonical terrain identity version."),
        new("terrainIdentityKind", typeof(string), ProvenanceFieldSpec.None, "stableParcel, bbox, radius, or polygon."),
        new("terrainIdentityStem", typeof(string), ProvenanceFieldSpec.None, "SHA-256 logical terrain identity stem."),
        new("terrainContentSignatureAlgorithm", typeof(string), ProvenanceFieldSpec.None, "Canonical content signature algorithm."),
        new("terrainContentSignature", typeof(string), ProvenanceFieldSpec.None, "SHA-256 canonical content signature."),
        new("terrainPointFrameHash", typeof(string), ProvenanceFieldSpec.None, "SHA-256 canonical point set and frame hash."),
        new("storedOriginalUniqueId", typeof(string), ProvenanceFieldSpec.None, "Creating element UniqueId."),
        new("storedOriginalDocumentCreationGuid", typeof(string), ProvenanceFieldSpec.None, "Creating document CreationGUID."),
    ];
}
