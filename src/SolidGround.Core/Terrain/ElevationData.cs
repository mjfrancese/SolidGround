using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Terrain;

/// <summary>
/// Base contract for normalized elevation data returned by a source.
/// </summary>
public abstract class ElevationData
{
    protected ElevationData(HorizontalReference horizontalReference, VerticalReference verticalReference)
    {
        HorizontalReference = horizontalReference ?? throw new ArgumentNullException(nameof(horizontalReference));
        VerticalReference = verticalReference ?? throw new ArgumentNullException(nameof(verticalReference));
    }

    public HorizontalReference HorizontalReference { get; }
    public VerticalReference VerticalReference { get; }
}

/// <summary>A normalized elevation raster. Missing cells are represented only as null.</summary>
public enum GridAnchorConvention
{
    LowerLeftCorner,
    CellCenter,
}

public enum GridRowOrder
{
    NorthToSouth,
    SouthToNorth,
}

public sealed class ElevationGrid : ElevationData
{
    private readonly double?[,] elevations;

    public ElevationGrid(
        HorizontalReference horizontalReference,
        VerticalReference verticalReference,
        Coordinate2D southwestAnchor,
        double cellSizeX,
        double cellSizeY,
        GridAnchorConvention anchorConvention,
        GridRowOrder rowOrder,
        double?[,] elevations)
        : base(horizontalReference, verticalReference)
    {
        ArgumentNullException.ThrowIfNull(elevations);
        if (elevations.GetLength(0) == 0 || elevations.GetLength(1) == 0)
        {
            throw new ArgumentException("An elevation grid must have at least one row and column.", nameof(elevations));
        }

        if (!double.IsFinite(cellSizeX) || cellSizeX <= 0d || !double.IsFinite(cellSizeY) || cellSizeY <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizeX), "Cell sizes must be finite and positive.");
        }

        if (horizontalReference.Kind != HorizontalReferenceKind.Projected && horizontalReference.Kind != HorizontalReferenceKind.Geographic)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalReference));
        }

        if (!Enum.IsDefined(anchorConvention) || !Enum.IsDefined(rowOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(anchorConvention));
        }
        this.elevations = (double?[,])elevations.Clone();
        for (int row = 0; row < RowCount; row++)
        {
            for (int column = 0; column < ColumnCount; column++)
            {
                if (this.elevations[row, column] is double value && !double.IsFinite(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(elevations), "Elevation values must be finite or null.");
                }
            }
        }

        SouthwestAnchor = southwestAnchor;
        CellSizeX = cellSizeX;
        CellSizeY = cellSizeY;
        AnchorConvention = anchorConvention;
        RowOrder = rowOrder;
    }

    public Coordinate2D SouthwestAnchor { get; }
    public double CellSizeX { get; }
    public double CellSizeY { get; }
    public GridAnchorConvention AnchorConvention { get; }
    public GridRowOrder RowOrder { get; }
    public int RowCount => elevations.GetLength(0);
    public int ColumnCount => elevations.GetLength(1);
    public double? GetElevation(int row, int column) => elevations[row, column];
    public double?[,] ToArray() => (double?[,])elevations.Clone();

    public Coordinate2D GetCellCenter(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        if (row >= RowCount || column >= ColumnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Row and column must identify an existing cell.");
        }

        double halfCellOffset = AnchorConvention == GridAnchorConvention.LowerLeftCorner ? 0.5d : 0d;
        double x = SouthwestAnchor.X + ((column + halfCellOffset) * CellSizeX);
        double southBasedRow = RowOrder == GridRowOrder.NorthToSouth
            ? RowCount - 1 - row
            : row;
        double y = SouthwestAnchor.Y + ((southBasedRow + halfCellOffset) * CellSizeY);
        return new Coordinate2D(x, y);
    }
}

/// <summary>One normalized terrain sample with no missing elevation value.</summary>
public sealed record TerrainSample(Coordinate3D Position);

/// <summary>A representation-neutral collection for sources such as classified point clouds.</summary>
public sealed class TerrainSampleSet : ElevationData
{
    private readonly TerrainSample[] samples;

    public TerrainSampleSet(
        HorizontalReference horizontalReference,
        VerticalReference verticalReference,
        IEnumerable<TerrainSample> samples)
        : base(horizontalReference, verticalReference)
    {
        ArgumentNullException.ThrowIfNull(samples);
        this.samples = samples.ToArray();
        if (this.samples.Any(sample => sample is null))
        {
            throw new ArgumentException("Terrain samples cannot contain null values.", nameof(samples));
        }

    }

    public IReadOnlyList<TerrainSample> Samples => Array.AsReadOnly(samples);
}
