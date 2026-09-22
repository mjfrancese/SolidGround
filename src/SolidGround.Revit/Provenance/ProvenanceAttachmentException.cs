namespace SolidGround.Revit.Provenance;

/// <summary>
/// SolidGround could not publish, validate, or verify the round trip of the Extensible Storage provenance
/// schema/entity it attaches to a created toposolid (SolidGround Issue #16 design record §3): a schema
/// already registered under <see cref="SolidGround.Core.Provenance.ExtensibleStorageProvenanceSchema.SchemaGuid"/>
/// has drifted from the expected shape, a <c>SchemaBuilder</c> precondition or <c>Finish()</c> call failed, the
/// immediate read-back entity is missing/invalid/unreadable/version-mismatched, a read-back field did not
/// match what was written, or reconstructing a retained sample's source coordinate from the read-back entity
/// exceeded <see cref="SolidGround.Core.Provenance.ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance"/>.
/// Thrown only from inside <c>CreateToposolidCommand</c>'s existing <c>ToposolidCreatedHook</c> call site
/// (<see cref="SolidGround.Revit.Provenance.ProvenanceEntityWriter.Attach"/>), where <c>RunTransaction</c>'s
/// own general catch rolls back the whole toposolid creation and surfaces <see cref="Exception.Message"/> --
/// SolidGround Issue #16 deliberately makes provenance attachment fatal (design record D1). No new catch
/// clause and no new dialog exist for this type; it relies entirely on the pre-existing hook contract.
/// </summary>
internal sealed class ProvenanceAttachmentException : InvalidOperationException
{
    internal ProvenanceAttachmentException()
    {
    }

    internal ProvenanceAttachmentException(string message)
        : base(message)
    {
    }

    internal ProvenanceAttachmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
