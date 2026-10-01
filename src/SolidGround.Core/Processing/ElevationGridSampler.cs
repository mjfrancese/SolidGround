using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Processing;

/// <summary>A named failure to sample an elevation grid without clamping, extrapolating, or bridging NODATA.</summary>
public sealed class ElevationGridSamplingException(string message) : InvalidOperationException(message);

/// <summary>One nonzero-weight source cell used by a strict bilinear grid sample.</summary>
public sealed record ElevationGridSupportCell(int Row, int Column, Coordinate2D Center, double Elevation, double Weight);

/// <summary>A source-grid sample with its projected coordinate, declared vertical reference, and exact support.</summary>
public sealed record ElevationGridSample(
    Coordinate2D ProjectedPoint,
    double Elevation,
    VerticalReference SourceReference,
    IReadOnlyList<ElevationGridSupportCell> Support);

/// <summary>
/// Samples normalized elevation grids on their cell-center lattice. This intentionally has no nearest-cell
/// fallback: only cells with a nonzero bilinear weight are required, so an exact centre can use one valid
/// cell while an interior point needs four valid cells.
/// </summary>
public static class ElevationGridSampler
{
    /// <summary>Index-space tolerance used only to normalize transform noise at an exact cell centre or edge.</summary>
    public const double IndexTolerance = 1e-9;

    public static ElevationGridSample SampleStrictBilinear(ElevationGrid grid, Coordinate2D projectedPoint)
    {
        ArgumentNullException.ThrowIfNull(grid);

        double xIndex = NormalizeIndex((projectedPoint.X - grid.GetCellCenter(0, 0).X) / grid.CellSizeX);
        int southRowZero = ToGridRow(grid, 0);
        double yIndex = NormalizeIndex((projectedPoint.Y - grid.GetCellCenter(southRowZero, 0).Y) / grid.CellSizeY);
        if (xIndex < 0d || xIndex > grid.ColumnCount - 1d || yIndex < 0d || yIndex > grid.RowCount - 1d)
        {
            throw new ElevationGridSamplingException("The selected point is outside the grid's closed cell-center lattice.");
        }

        (int lowerX, int upperX, double fractionX) = ResolveAxis(xIndex, grid.ColumnCount);
        (int lowerY, int upperY, double fractionY) = ResolveAxis(yIndex, grid.RowCount);
        List<ElevationGridSupportCell> support = [];
        double sampled = 0d;

        AddSupport(lowerY, lowerX, (1d - fractionY) * (1d - fractionX));
        AddSupport(lowerY, upperX, (1d - fractionY) * fractionX);
        AddSupport(upperY, lowerX, fractionY * (1d - fractionX));
        AddSupport(upperY, upperX, fractionY * fractionX);

        return new ElevationGridSample(projectedPoint, sampled, grid.VerticalReference, support.AsReadOnly());

        void AddSupport(int southRow, int column, double weight)
        {
            if (weight == 0d)
            {
                return;
            }

            int row = ToGridRow(grid, southRow);
            double? elevation = grid.GetElevation(row, column);
            if (elevation is not double value)
            {
                throw new ElevationGridSamplingException("The selected point needs a NODATA source cell for bilinear support.");
            }

            support.Add(new ElevationGridSupportCell(row, column, grid.GetCellCenter(row, column), value, weight));
            sampled += value * weight;
        }
    }

    private static double NormalizeIndex(double value)
    {
        double nearest = Math.Round(value, MidpointRounding.ToEven);
        return Math.Abs(value - nearest) <= IndexTolerance ? nearest : value;
    }

    private static (int Lower, int Upper, double Fraction) ResolveAxis(double index, int count)
    {
        if (count == 1)
        {
            return (0, 0, 0d);
        }

        if (index == count - 1d)
        {
            return (count - 2, count - 1, 1d);
        }

        int lower = (int)Math.Floor(index);
        return (lower, lower + 1, index - lower);
    }

    private static int ToGridRow(ElevationGrid grid, int southRow) => grid.RowOrder == GridRowOrder.SouthToNorth
        ? southRow
        : grid.RowCount - 1 - southRow;
}
