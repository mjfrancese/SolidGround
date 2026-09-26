namespace SolidGround.Core.Sources.LocalParcelFile;

/// <summary>Base type for every exception <see cref="LocalParcelFileSource"/> raises. <see cref="ParcelBoundarySourceException.SourceName"/> is always "LocalParcelFile".</summary>
public abstract class LocalParcelFileException : ParcelBoundarySourceException
{
    private protected LocalParcelFileException(string message, string path, Exception? innerException = null)
        : base("LocalParcelFile", message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>The configured <see cref="LocalParcelFileOptions.Path"/>.</summary>
    public string Path { get; }
}

/// <summary>The configured path does not exist.</summary>
public sealed class LocalParcelFileNotFoundException : LocalParcelFileException
{
    public LocalParcelFileNotFoundException(string message, string path)
        : base(message, path)
    {
    }
}

/// <summary>
/// The file exists but is not well-formed per this reader's contract: invalid JSON; a root that is not a
/// GeoJSON <c>FeatureCollection</c>; a <c>crs</c> member at the FeatureCollection or Feature level that is
/// present but not a recognized, agreeing WGS 84 declaration; a required mapped field missing or blank on a
/// feature whose geometry would otherwise be read; or a rewrapped <c>ParcelGeometryException</c> from the
/// reused geometry parser.
/// </summary>
public sealed class LocalParcelFileFormatException : LocalParcelFileException
{
    public LocalParcelFileFormatException(string message, string path, Exception? innerException = null)
        : base(message, path, innerException)
    {
    }
}

/// <summary>
/// The path exists but could not be opened or read for a reason other than "not found": an unauthorized-access
/// error, a sharing violation, or any other I/O failure while reading. Kept distinct from "not found" and
/// "malformed" because the remedy differs (fix a permission/lock problem, not the path or the file's content).
/// </summary>
public sealed class LocalParcelFileAccessException : LocalParcelFileException
{
    public LocalParcelFileAccessException(string message, string path, Exception innerException)
        : base(message, path, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
