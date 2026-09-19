namespace SolidGround.Core.Provenance;

/// <summary>
/// A terrain provenance record or export payload could not be assembled or validated for the given inputs.
/// </summary>
public sealed class TerrainProvenanceException : InvalidOperationException
{
    public TerrainProvenanceException()
    {
    }

    public TerrainProvenanceException(string message)
        : base(message)
    {
    }

    public TerrainProvenanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
