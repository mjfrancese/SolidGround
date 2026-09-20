using SolidGround.Core.Terrain;

namespace SolidGround.Core.Rasters;

/// <summary>
/// The parsed header of an Esri ASCII (AAIGrid) raster, before any data row has been read. Returned by
/// <see cref="AaiGridParser.ReadHeader(TextReader)"/> and consumed internally by
/// <see cref="AaiGridParser.Parse(TextReader, Metadata.HorizontalReference, Metadata.VerticalReference)"/>.
/// </summary>
/// <param name="ColumnCount">The declared <c>ncols</c> value. Must be a positive integer.</param>
/// <param name="RowCount">The declared <c>nrows</c> value. Must be a positive integer.</param>
/// <param name="AnchorX">
/// The declared <c>xllcorner</c> or <c>xllcenter</c> value, in the raster's own horizontal unit.
/// </param>
/// <param name="AnchorY">
/// The declared <c>yllcorner</c> or <c>yllcenter</c> value, in the raster's own horizontal unit.
/// </param>
/// <param name="AnchorConvention">
/// Whether <see cref="AnchorX"/> and <see cref="AnchorY"/> name the lower-left corner of the grid or the
/// center of the lower-left cell.
/// </param>
/// <param name="CellSize">The declared <c>cellsize</c> value, applied to both axes. Must be finite and positive.</param>
/// <param name="NoDataValue">
/// The sentinel elevation value that represents missing data: the declared <c>NODATA_value</c> when
/// <see cref="NoDataDeclared"/> is <see langword="true"/>, or AAIGrid's documented default of <c>-9999</c>
/// otherwise.
/// </param>
/// <param name="NoDataDeclared">
/// Whether the header included an explicit <c>NODATA_value</c> line, as opposed to relying on the default.
/// </param>
/// <remarks>
/// <see cref="ColumnCount"/>, <see cref="RowCount"/>, and <see cref="CellSize"/> validate when an
/// <see cref="AaiGridHeader"/> is constructed through this positional constructor. As with any record's
/// property initializer, a subsequent <c>with</c>-expression that only touches other properties does not
/// re-run this validation; SolidGround never mutates an already-parsed header this way.
/// </remarks>
public sealed record AaiGridHeader(
    int ColumnCount,
    int RowCount,
    double AnchorX,
    double AnchorY,
    GridAnchorConvention AnchorConvention,
    double CellSize,
    double NoDataValue,
    bool NoDataDeclared)
{
    /// <inheritdoc cref="AaiGridHeader"/>
    public int ColumnCount { get; init; } = ColumnCount > 0
        ? ColumnCount
        : throw new ArgumentOutOfRangeException(nameof(ColumnCount), ColumnCount, "ncols must be a positive integer.");

    /// <inheritdoc cref="AaiGridHeader"/>
    public int RowCount { get; init; } = RowCount > 0
        ? RowCount
        : throw new ArgumentOutOfRangeException(nameof(RowCount), RowCount, "nrows must be a positive integer.");

    /// <inheritdoc cref="AaiGridHeader"/>
    public double CellSize { get; init; } = double.IsFinite(CellSize) && CellSize > 0d
        ? CellSize
        : throw new ArgumentOutOfRangeException(nameof(CellSize), CellSize, "cellsize must be finite and positive.");
}
