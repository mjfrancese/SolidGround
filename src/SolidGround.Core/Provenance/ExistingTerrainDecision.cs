namespace SolidGround.Core.Provenance;

public enum ExistingTerrainDecisionKind
{
    Create,
    Reuse,
    Refuse,
    RequiresLegacyAcknowledgement,
}

/// <summary>One safely readable v2 entity as observed on a current document element.</summary>
public sealed record ExistingTerrainRecord(
    string ElementId,
    TerrainIdentity Identity,
    string StoredOriginalUniqueId,
    string CurrentUniqueId,
    string StoredOriginalDocumentCreationGuid);

/// <summary>Plain decision data for the command UI; no Revit API types cross this boundary.</summary>
public sealed record ExistingTerrainDecision(
    ExistingTerrainDecisionKind Kind,
    IReadOnlyList<string> ElementIds,
    string Detail)
{
    public static ExistingTerrainDecision Decide(
        TerrainIdentity proposed,
        string currentDocumentCreationGuid,
        IEnumerable<ExistingTerrainRecord> existing,
        IEnumerable<string> legacyV1ElementIds)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDocumentCreationGuid);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(legacyV1ElementIds);

        string[] legacy = legacyV1ElementIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (legacy.Length > 0)
        {
            return new(ExistingTerrainDecisionKind.RequiresLegacyAcknowledgement, legacy,
                "Existing legacy v1 SolidGround provenance cannot verify this run. Create only after an explicit current-run acknowledgement.");
        }

        ExistingTerrainRecord[] matches = existing.Where(record => string.Equals(record.Identity.Stem, proposed.Stem, StringComparison.Ordinal))
            .OrderBy(record => record.ElementId, StringComparer.Ordinal).ToArray();
        if (matches.Length == 0)
        {
            return new(ExistingTerrainDecisionKind.Create, [], "No existing SolidGround terrain has this identity stem.");
        }

        string[] ids = matches.Select(match => match.ElementId).ToArray();
        if (matches.Length > 1)
        {
            return new(ExistingTerrainDecisionKind.Refuse, ids, "More than one existing SolidGround terrain has this identity stem; no element was selected automatically.");
        }

        ExistingTerrainRecord match = matches[0];
        if (!string.Equals(match.StoredOriginalDocumentCreationGuid, currentDocumentCreationGuid, StringComparison.Ordinal))
        {
            return new(ExistingTerrainDecisionKind.Refuse, ids, "The matching terrain was copied into a document with a different creation GUID.");
        }

        if (!string.Equals(match.StoredOriginalUniqueId, match.CurrentUniqueId, StringComparison.Ordinal))
        {
            return new(ExistingTerrainDecisionKind.Refuse, ids, "The matching terrain has a different current UniqueId and is copied or stale.");
        }

        if (!string.Equals(match.Identity.Version, proposed.Version, StringComparison.Ordinal) ||
            !string.Equals(match.Identity.ContentSignatureAlgorithm, proposed.ContentSignatureAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(match.Identity.ContentSignature, proposed.ContentSignature, StringComparison.Ordinal) ||
            !string.Equals(match.Identity.PointFrameHash, proposed.PointFrameHash, StringComparison.Ordinal))
        {
            return new(ExistingTerrainDecisionKind.Refuse, ids, "The same logical terrain identity has different physical run content or point/frame data.");
        }

        return new(ExistingTerrainDecisionKind.Reuse, ids, "The existing terrain has matching identity, content, point/frame data, document history, and UniqueId.");
    }
}
