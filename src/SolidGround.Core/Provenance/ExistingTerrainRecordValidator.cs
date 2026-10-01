namespace SolidGround.Core.Provenance;

/// <summary>
/// Validates the fields a read-only native-provenance scanner must trust before passing a record to the
/// duplicate-terrain decision. This intentionally leaves <see cref="TerrainIdentity"/> constructible for
/// ordinary Core comparison tests; the native boundary, where persisted untrusted fields enter, is strict.
/// </summary>
public static class ExistingTerrainRecordValidator
{
    public static TerrainIdentityKind ParseKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.TryParse(value, ignoreCase: false, out TerrainIdentityKind kind) ||
            !Enum.IsDefined(kind) ||
            !string.Equals(value, kind.ToString(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Terrain identity kind is not a supported named value.", nameof(value));
        }

        return kind;
    }

    /// <summary>
    /// Validates a modern entity's identity and persisted document identity. V1 identities predate a floor
    /// reference; v2 identities require floor or building-outline context, so a record cannot evade duplicate
    /// guarding by mixing contracts.
    /// </summary>
    public static void Validate(
        TerrainIdentity identity,
        bool hasFloorOrContext,
        string storedOriginalUniqueId,
        string storedOriginalDocumentCreationGuid)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(identity.Kind))
        {
            throw new ArgumentException("Terrain identity kind is not supported.", nameof(identity));
        }

        string expectedVersion = hasFloorOrContext ? TerrainIdentity.FloorReferenceVersion : TerrainIdentity.CurrentVersion;
        string expectedAlgorithm = hasFloorOrContext
            ? TerrainIdentity.FloorReferenceContentSignatureAlgorithm
            : TerrainIdentity.CurrentContentSignatureAlgorithm;
        if (!string.Equals(identity.Version, expectedVersion, StringComparison.Ordinal) ||
            !string.Equals(identity.ContentSignatureAlgorithm, expectedAlgorithm, StringComparison.Ordinal))
        {
            throw new ArgumentException("Terrain identity version and content-signature algorithm do not match this provenance contract.", nameof(identity));
        }

        RequireSha256(identity.Stem, nameof(identity.Stem));
        RequireSha256(identity.ContentSignature, nameof(identity.ContentSignature));
        RequireSha256(identity.PointFrameHash, nameof(identity.PointFrameHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(storedOriginalUniqueId);
        if (!Guid.TryParseExact(storedOriginalDocumentCreationGuid, "D", out Guid documentCreationGuid) ||
            documentCreationGuid == Guid.Empty)
        {
            throw new ArgumentException("Stored original document creation GUID must be a non-empty canonical GUID.", nameof(storedOriginalDocumentCreationGuid));
        }
    }

    private static void RequireSha256(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Terrain identity hash must be exactly 64 hexadecimal characters.", parameterName);
        }
    }
}
