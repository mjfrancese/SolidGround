namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>The byte order a TIFF file declared in its header ("II" or "MM").</summary>
public enum GeoTiffByteOrder
{
    /// <summary>The header declared "II" (Intel, least-significant byte first).</summary>
    LittleEndian,

    /// <summary>The header declared "MM" (Motorola, most-significant byte first).</summary>
    BigEndian,
}

/// <summary>
/// The subset of a GeoTIFF's IFD0 tags and GeoKeys that SolidGround reads to recover a raster's horizontal
/// and vertical reference metadata, produced by <see cref="GeoTiffMetadataReader.Read(ReadOnlyMemory{byte})"/>.
/// No pixel data is decoded; every property here comes from the file's directory structure alone. See
/// docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis" section for why this
/// subset of tags and GeoKeys is the one SolidGround needs.
/// </summary>
/// <param name="ByteOrder">The byte order declared by the TIFF header.</param>
/// <param name="ImageWidth">Tag 256 (ImageWidth): the raster's column count.</param>
/// <param name="ImageLength">Tag 257 (ImageLength): the raster's row count.</param>
/// <param name="ModelType">
/// GeoKey 1024 (GTModelTypeGeoKey), when present: 1 for a projected model, 2 for geographic, 3 for geocentric.
/// </param>
/// <param name="RasterType">
/// GeoKey 1025 (GTRasterTypeGeoKey), when present: 1 for PixelIsArea, 2 for PixelIsPoint.
/// </param>
/// <param name="ProjectedCoordinateSystemCode">GeoKey 3072 (ProjectedCSTypeGeoKey), when present: an EPSG projected coordinate system code.</param>
/// <param name="GeographicCoordinateSystemCode">GeoKey 2048 (GeographicTypeGeoKey), when present: an EPSG geographic coordinate system code.</param>
/// <param name="LinearUnitsCode">GeoKey 3076 (ProjLinearUnitsGeoKey), when present: an EPSG linear unit code (9001 is metre).</param>
/// <param name="Citation">GeoKey 1026 (GTCitationGeoKey), when present: a free-text description of the model.</param>
/// <param name="GeographicCitation">GeoKey 2049 (GeogCitationGeoKey), when present: a free-text description of the geographic coordinate system.</param>
/// <param name="ProjectedCitation">GeoKey 3073 (PCSCitationGeoKey), when present: a free-text description of the projected coordinate system.</param>
/// <param name="VerticalCoordinateSystemCode">GeoKey 4096 (VerticalCSTypeGeoKey), when present: an EPSG vertical coordinate system code.</param>
/// <param name="ModelPixelScale">Tag 33550 (ModelPixelScaleTag), when present: the (X, Y, Z) scale of one pixel in the model's units.</param>
/// <param name="ModelTiepoint">
/// Tag 33922 (ModelTiepointTag), when present: the first tiepoint (I, J, K raster-space; X, Y, Z model-space)
/// even when the tag declares more than one tiepoint.
/// </param>
/// <param name="NoDataText">Tag 42113 (GDAL_NODATA), when present: the raster's NODATA sentinel, as the exact text GDAL wrote (trailing NUL stripped).</param>
public sealed record GeoTiffMetadata(
    GeoTiffByteOrder ByteOrder,
    uint ImageWidth,
    uint ImageLength,
    ushort? ModelType,
    ushort? RasterType,
    ushort? ProjectedCoordinateSystemCode,
    ushort? GeographicCoordinateSystemCode,
    ushort? LinearUnitsCode,
    string? Citation,
    string? GeographicCitation,
    string? ProjectedCitation,
    ushort? VerticalCoordinateSystemCode,
    (double X, double Y, double Z)? ModelPixelScale,
    (double I, double J, double K, double X, double Y, double Z)? ModelTiepoint,
    string? NoDataText);
