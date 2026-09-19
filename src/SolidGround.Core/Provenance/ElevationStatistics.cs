using SolidGround.Core.Terrain;

namespace SolidGround.Core.Provenance;

/// <summary>
/// Computes provenance statistics over an <see cref="ElevationGrid"/>'s valid (non-NODATA) cells only. See
/// docs/architecture/provenance-and-deterministic-exports.md's "NODATA, empty candidate sets, and statistics"
/// section for why an empty valid-cell set is a permissive count here and a rejection one level up, in
/// <see cref="TerrainExportPayloadAssembler"/>.
/// </summary>
public static class ElevationStatistics
{
    /// <summary>
    /// Counts <paramref name="grid"/>'s cells whose elevation is not <see langword="null"/>. Returns zero for
    /// an all-NODATA grid rather than throwing; a NODATA sentinel is already <see langword="null"/> by the
    /// time it reaches an <see cref="ElevationGrid"/>, so no magnitude comparison is performed here.
    /// </summary>
    public static int CountValidCells(ElevationGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        int count = 0;
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (grid.GetElevation(row, column) is not null)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Computes the minimum and maximum elevation over <paramref name="grid"/>'s valid cells only, tagged with
    /// <see cref="ElevationData.VerticalReference"/>'s unit.
    /// </summary>
    /// <exception cref="TerrainProvenanceException"><see cref="CountValidCells"/> is zero: every cell is NODATA, so no range exists.</exception>
    public static ElevationRange ComputeRange(ElevationGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        int validCellCount = 0;

        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (grid.GetElevation(row, column) is not double elevation)
                {
                    continue;
                }

                validCellCount++;
                if (elevation < minimum)
                {
                    minimum = elevation;
                }

                if (elevation > maximum)
                {
                    maximum = elevation;
                }
            }
        }

        if (validCellCount == 0)
        {
            throw new TerrainProvenanceException(
                "The grid has no valid elevation: every cell is NODATA, and NODATA cells are excluded from statistics.");
        }

        return new ElevationRange(minimum, maximum, grid.VerticalReference.Unit);
    }
}
