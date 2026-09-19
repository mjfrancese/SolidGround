using System.Globalization;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Rasters;

/// <summary>
/// Writes an <see cref="ElevationGrid"/> as an Esri ASCII (AAIGrid) raster, in the exact header and data
/// shape <see cref="AaiGridParser.Parse"/> reads back. See docs/architecture/cli-workflow.md's "Raster set
/// persistence" section for why the CLI needs a Core-owned inverse of the existing parser instead of
/// building its own raster serialization.
/// </summary>
public static class AaiGridWriter
{
    /// <summary>Writes <paramref name="grid"/> to <paramref name="writer"/> as an Esri ASCII raster.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> or <paramref name="writer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="grid"/>'s <see cref="ElevationGrid.CellSizeX"/> and <see cref="ElevationGrid.CellSizeY"/>
    /// differ (the Esri ASCII raster format has a single "cellsize" value); or a valid (non-null) elevation
    /// cell equals <paramref name="noDataValue"/>, which would silently turn a real elevation into a missing
    /// cell if the written file were read back.
    /// </exception>
    public static void Write(ElevationGrid grid, TextWriter writer, double noDataValue = -9999d)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(writer);
        if (grid.CellSizeX != grid.CellSizeY)
        {
            throw new ArgumentException(
                "AaiGridWriter requires equal CellSizeX and CellSizeY: the Esri ASCII raster format has one 'cellsize' value.",
                nameof(grid));
        }

        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (grid.GetElevation(row, column) is double value && value == noDataValue)
                {
                    throw new ArgumentException(
                        $"Cell (row {row}, column {column}) has elevation {value.ToString("R", CultureInfo.InvariantCulture)}, " +
                        $"which collides with the configured NODATA sentinel {noDataValue.ToString("R", CultureInfo.InvariantCulture)}.",
                        nameof(noDataValue));
                }
            }
        }

        (string xKey, string yKey) = grid.AnchorConvention == GridAnchorConvention.LowerLeftCorner
            ? ("xllcorner", "yllcorner")
            : ("xllcenter", "yllcenter");

        WriteHeaderLine(writer, "ncols", grid.ColumnCount.ToString(CultureInfo.InvariantCulture));
        WriteHeaderLine(writer, "nrows", grid.RowCount.ToString(CultureInfo.InvariantCulture));
        WriteHeaderLine(writer, xKey, grid.SouthwestAnchor.X.ToString("R", CultureInfo.InvariantCulture));
        WriteHeaderLine(writer, yKey, grid.SouthwestAnchor.Y.ToString("R", CultureInfo.InvariantCulture));
        WriteHeaderLine(writer, "cellsize", grid.CellSizeX.ToString("R", CultureInfo.InvariantCulture));
        WriteHeaderLine(writer, "NODATA_value", noDataValue.ToString("R", CultureInfo.InvariantCulture));

        // Rows are written in the grid's own stored order (index 0 first) regardless of grid.RowOrder: the
        // Esri ASCII raster format has no header field that records row order, so a SouthToNorth grid cannot
        // round-trip its orientation through this format -- see docs/architecture/cli-workflow.md's "Raster
        // set persistence" section.
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (column > 0)
                {
                    writer.Write(' ');
                }

                double cell = grid.GetElevation(row, column) ?? noDataValue;
                writer.Write(cell.ToString("R", CultureInfo.InvariantCulture));
            }

            // '\n' is written directly here -- never TextWriter.WriteLine, whose NewLine property defaults to
            // Environment.NewLine ('\r\n' on Windows). See docs/architecture/cli-workflow.md's "Determinism"
            // section for why every file this workflow generates is LF-only regardless of the caller's
            // TextWriter configuration or host platform.
            writer.Write('\n');
        }
    }

    private static void WriteHeaderLine(TextWriter writer, string key, string value)
    {
        writer.Write(key);
        writer.Write(' ');
        writer.Write(value);
        writer.Write('\n');
    }
}
