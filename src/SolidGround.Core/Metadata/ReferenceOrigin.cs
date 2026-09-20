namespace SolidGround.Core.Metadata;

/// <summary>
/// Where a horizontal or vertical reference used by an acquisition, and later recorded in provenance, a
/// sidecar, or an export, actually came from.
/// </summary>
public enum ReferenceOrigin
{
    /// <summary>
    /// The reference was carried directly by the elevation data response itself, for example a zip
    /// archive's <c>.prj</c> or <c>.aux.xml</c> sidecar returned alongside the raster.
    /// </summary>
    SourceResponse,

    /// <summary>
    /// The reference was carried by a separate metadata response returned for a request identical to the
    /// data request, for example the GeoKeys of a GeoTIFF fetched only to recover reference metadata.
    /// </summary>
    SourceMetadataResponse,

    /// <summary>
    /// The reference was declared from the dataset's own published documentation rather than carried by
    /// any response returned for the request.
    /// </summary>
    DatasetDocumentation,

    /// <summary>
    /// The reference was supplied by the operator rather than by the source, for example a local
    /// <c>.prj</c> file or an explicit command-line flag.
    /// </summary>
    Operator,
}
