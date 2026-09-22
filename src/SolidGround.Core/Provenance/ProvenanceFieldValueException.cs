namespace SolidGround.Core.Provenance;

/// <summary>
/// One of <see cref="ExtensibleStorageProvenanceValues.From"/>'s computed Extensible Storage field values
/// could not be accepted -- today, only a non-finite Length-spec field (SolidGround Issue #16 design record
/// §2). See <see cref="ExtensibleStorageProvenanceSchema"/> for the field list this guards.
/// </summary>
public sealed class ProvenanceFieldValueException : InvalidOperationException
{
    public ProvenanceFieldValueException()
    {
    }

    public ProvenanceFieldValueException(string message)
        : base(message)
    {
    }

    public ProvenanceFieldValueException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
