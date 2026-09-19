namespace SolidGround.Core.Exports;

/// <summary>
/// An export bundle could not be rendered from a payload, read back from its bytes, or written to its
/// destination.
/// </summary>
public sealed class TerrainExportException : InvalidOperationException
{
    public TerrainExportException()
    {
    }

    public TerrainExportException(string message)
        : base(message)
    {
    }

    public TerrainExportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
