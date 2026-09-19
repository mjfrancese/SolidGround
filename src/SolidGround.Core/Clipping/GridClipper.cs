using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Buffer;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Clipping;

/// <summary>Which grid cells <see cref="GridClipper"/> counts as inside its effective clip region.</summary>
public enum GridCellInclusion
{
    /// <summary>A cell is included when its center point is covered by the effective region (boundary-inclusive).</summary>
    CellCenterCovered,

    /// <summary>A cell is included when its full footprint rectangle intersects the effective region at all.</summary>
    CellFootprintIntersects,
}

/// <summary>Options controlling how <see cref="GridClipper"/> interprets and crops a clip.</summary>
public sealed record GridClipOptions
{
    /// <summary>Which cells count as inside the effective region. Defaults to <see cref="GridCellInclusion.CellCenterCovered"/>.</summary>
    public GridCellInclusion Inclusion { get; init; } = GridCellInclusion.CellCenterCovered;

    /// <summary>Whether the output grid is cropped to the minimal window containing every included cell. Defaults to <see langword="true"/>.</summary>
    public bool CropToRegionEnvelope { get; init; } = true;
}

/// <summary>The disposition of one output grid cell after <see cref="GridClipper.Clip"/>.</summary>
public enum GridCellStatus
{
    /// <summary>The cell is inside the effective region and carries a source elevation value.</summary>
    Retained,

    /// <summary>The cell is inside the effective region, but the source grid had no elevation value there.</summary>
    SourceNoData,

    /// <summary>The cell is outside the effective region.</summary>
    RegionExcluded,
}

/// <summary>A grid could not be clipped to the requested region.</summary>
public sealed class GridClipException : InvalidOperationException
{
    public GridClipException()
    {
    }

    public GridClipException(string message)
        : base(message)
    {
    }

    public GridClipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The result of clipping an <see cref="ElevationGrid"/> to a <see cref="Clipping.ClipRegion"/>: the clipped
/// grid itself, a per-cell status matching its shape, the effective (possibly buffered) region that was
/// applied, and summary counts.
/// </summary>
public sealed class GridClipResult
{
    private readonly GridCellStatus[,] cellStatuses;

    internal GridClipResult(
        ElevationGrid grid,
        GridCellStatus[,] cellStatuses,
        PolygonalRegion effectiveRegion,
        GridCellInclusion inclusion,
        int sourceCellCount,
        int includedCellCount,
        int retainedElevationCount,
        int noDataCellsInsideRegion,
        int excludedCellCount)
    {
        Grid = grid;
        this.cellStatuses = cellStatuses;
        EffectiveRegion = effectiveRegion;
        Inclusion = inclusion;
        SourceCellCount = sourceCellCount;
        IncludedCellCount = includedCellCount;
        RetainedElevationCount = retainedElevationCount;
        NoDataCellsInsideRegion = noDataCellsInsideRegion;
        ExcludedCellCount = excludedCellCount;
    }

    /// <summary>The clipped grid. Excluded cells are <see langword="null"/>; a retained source NODATA cell stays <see langword="null"/>.</summary>
    public ElevationGrid Grid { get; }

    /// <summary>The region actually applied: <see cref="ClipRegion.Region"/> itself, or its buffered form when <see cref="ClipRegion.Buffer"/> is positive.</summary>
    public PolygonalRegion EffectiveRegion { get; }

    /// <summary>The inclusion rule that was applied.</summary>
    public GridCellInclusion Inclusion { get; }

    /// <summary>The number of cells in the source grid.</summary>
    public int SourceCellCount { get; }

    /// <summary>
    /// The number of cells in the source grid that are inside <see cref="EffectiveRegion"/>, including a
    /// source NODATA cell. Like <see cref="SourceCellCount"/>, this describes the full, uncropped source
    /// grid's disposition, not <see cref="Grid"/>'s shape: when <see cref="GridClipOptions.CropToRegionEnvelope"/>
    /// is <see langword="true"/>, <see cref="Grid"/> and <see cref="GetCellStatuses"/> are sliced down to the
    /// minimal covering window, but these summary counts are not recomputed from that cropped shape.
    /// </summary>
    public int IncludedCellCount { get; }

    /// <summary>
    /// The number of cells in the source grid that are inside <see cref="EffectiveRegion"/> and also carried
    /// a source elevation value. Scoped to the full source grid; see the <see cref="IncludedCellCount"/> remarks.
    /// </summary>
    public int RetainedElevationCount { get; }

    /// <summary>
    /// The number of cells in the source grid that are inside <see cref="EffectiveRegion"/> but had no source
    /// elevation value. Scoped to the full source grid; see the <see cref="IncludedCellCount"/> remarks.
    /// </summary>
    public int NoDataCellsInsideRegion { get; }

    /// <summary>
    /// The number of cells in the source grid that are outside <see cref="EffectiveRegion"/>. Scoped to the
    /// full source grid; see the <see cref="IncludedCellCount"/> remarks.
    /// </summary>
    public int ExcludedCellCount { get; }

    /// <summary>The row count of <see cref="Grid"/>.</summary>
    public int OutputRowCount => Grid.RowCount;

    /// <summary>The column count of <see cref="Grid"/>.</summary>
    public int OutputColumnCount => Grid.ColumnCount;

    /// <summary>Returns a defensive clone of the per-cell status array, shaped and oriented exactly like <see cref="Grid"/>.</summary>
    public GridCellStatus[,] GetCellStatuses() => (GridCellStatus[,])cellStatuses.Clone();

    /// <summary>Returns the status of one output cell.</summary>
    public GridCellStatus GetCellStatus(int row, int column) => cellStatuses[row, column];
}

/// <summary>
/// Clips an <see cref="ElevationGrid"/> to a <see cref="Clipping.ClipRegion"/>: excludes cells outside the
/// (optionally buffered) region, optionally crops the output to the minimal covering window, and reports a
/// per-cell status that distinguishes a region-excluded cell from a retained source NODATA cell.
/// </summary>
public static class GridClipper
{
    /// <summary>
    /// Clips <paramref name="grid"/> to <paramref name="region"/> (optionally buffered first, per
    /// <see cref="ClipRegion.Buffer"/>), returning a <see cref="GridClipResult"/> with the clipped grid, a
    /// per-cell <see cref="GridCellStatus"/> array, the effective region that was applied, and summary counts.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> or <paramref name="region"/> is <see langword="null"/>.</exception>
    /// <exception cref="GridClipException">
    /// <paramref name="region"/>'s horizontal reference does not equal <paramref name="grid"/>'s; a positive
    /// <see cref="ClipRegion.Buffer"/> was requested in a reference that is not projected with a linear unit;
    /// or the effective region covers no cell of <paramref name="grid"/>.
    /// </exception>
    public static GridClipResult Clip(ElevationGrid grid, ClipRegion region, GridClipOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(region);
        GridClipOptions effectiveOptions = options ?? new GridClipOptions();
        if (!Enum.IsDefined(effectiveOptions.Inclusion))
        {
            throw new ArgumentOutOfRangeException(nameof(options), effectiveOptions.Inclusion, "Unsupported grid cell inclusion rule.");
        }

        if (region.Region.HorizontalReference != grid.HorizontalReference)
        {
            throw new GridClipException(
                "The clip region's horizontal reference does not match the grid's horizontal reference. Transform " +
                "the region into the grid's reference first (SolidGround Issue #6).");
        }

        PolygonalRegion effectiveRegion = BuildEffectiveRegion(region);
        GeometryFactory factory = effectiveRegion.Geometry.Factory;
        IPreparedGeometry prepared = PreparedGeometryFactory.Prepare(effectiveRegion.Geometry);

        int rowCount = grid.RowCount;
        int columnCount = grid.ColumnCount;
        double?[,] maskedElevations = new double?[rowCount, columnCount];
        GridCellStatus[,] statuses = new GridCellStatus[rowCount, columnCount];

        int includedCellCount = 0;
        int retainedElevationCount = 0;
        int noDataCellsInsideRegion = 0;
        int minRow = -1, maxRow = -1, minColumn = -1, maxColumn = -1;

        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                if (!IsCellIncluded(grid, prepared, factory, row, column, effectiveOptions.Inclusion))
                {
                    statuses[row, column] = GridCellStatus.RegionExcluded;
                    continue;
                }

                includedCellCount++;
                minRow = minRow < 0 ? row : Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
                minColumn = minColumn < 0 ? column : Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);

                double? elevation = grid.GetElevation(row, column);
                maskedElevations[row, column] = elevation;
                if (elevation is null)
                {
                    statuses[row, column] = GridCellStatus.SourceNoData;
                    noDataCellsInsideRegion++;
                }
                else
                {
                    statuses[row, column] = GridCellStatus.Retained;
                    retainedElevationCount++;
                }
            }
        }

        if (includedCellCount == 0)
        {
            throw new GridClipException("The clip region covers no cell of the grid.");
        }

        (ElevationGrid outputGrid, GridCellStatus[,] outputStatuses) = effectiveOptions.CropToRegionEnvelope
            ? BuildCroppedGrid(grid, maskedElevations, statuses, minRow, maxRow, minColumn, maxColumn)
            : (new ElevationGrid(
                grid.HorizontalReference, grid.VerticalReference, grid.SouthwestAnchor, grid.CellSizeX, grid.CellSizeY,
                grid.AnchorConvention, grid.RowOrder, maskedElevations), statuses);

        int excludedCellCount = (rowCount * columnCount) - includedCellCount;
        return new GridClipResult(
            outputGrid,
            outputStatuses,
            effectiveRegion,
            effectiveOptions.Inclusion,
            sourceCellCount: rowCount * columnCount,
            includedCellCount,
            retainedElevationCount,
            noDataCellsInsideRegion,
            excludedCellCount);
    }

    /// <summary>
    /// Returns <see cref="ClipRegion.Region"/> unchanged when <see cref="ClipRegion.Buffer"/> is zero — NTS
    /// documents <c>Geometry.Buffer(0)</c> as a topology "validify" operation, not a no-op, so it is never
    /// called for a zero buffer. Otherwise buffers <see cref="ClipRegion.Region"/> by the buffer distance,
    /// converted into the region's own linear unit, and re-validates the result through
    /// <see cref="PolygonalRegion.FromGeometry"/>.
    /// </summary>
    private static PolygonalRegion BuildEffectiveRegion(ClipRegion region)
    {
        if (region.Buffer.Value == 0d)
        {
            return region.Region;
        }

        HorizontalReference reference = region.Region.HorizontalReference;
        if (reference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new GridClipException(
                "A positive clip buffer requires a projected horizontal reference; a buffer in degrees is not a distance.");
        }

        LengthUnit linearUnit = reference.Unit.LinearUnit
            ?? throw new GridClipException(
                "A positive clip buffer requires a projected horizontal reference with a linear unit; a buffer in degrees is not a distance.");

        double bufferInReferenceUnit = region.Buffer.In(linearUnit);
        NtsGeometry buffered = region.Region.Geometry.Buffer(
            bufferInReferenceUnit,
            new BufferParameters(8, EndCapStyle.Round, JoinStyle.Round, BufferParameters.DefaultMitreLimit));

        return PolygonalRegion.FromGeometry(buffered, reference);
    }

    private static bool IsCellIncluded(
        ElevationGrid grid, IPreparedGeometry prepared, GeometryFactory factory, int row, int column, GridCellInclusion inclusion)
    {
        Coordinate2D center = grid.GetCellCenter(row, column);
        if (inclusion == GridCellInclusion.CellCenterCovered)
        {
            Point centerPoint = factory.CreatePoint(new Coordinate(center.X, center.Y));
            return prepared.Covers(centerPoint);
        }

        double halfSizeX = grid.CellSizeX / 2d;
        double halfSizeY = grid.CellSizeY / 2d;
        Envelope footprint = new(center.X - halfSizeX, center.X + halfSizeX, center.Y - halfSizeY, center.Y + halfSizeY);
        NtsGeometry footprintGeometry = factory.ToGeometry(footprint);
        return prepared.Intersects(footprintGeometry);
    }

    /// <summary>
    /// Crops <paramref name="elevations"/>/<paramref name="statuses"/> to the minimal row/column window
    /// containing every included cell, and computes the new southwest anchor so that every retained cell
    /// center still equals the source grid's <see cref="ElevationGrid.GetCellCenter"/> for the same cell
    /// (within a small floating-point tolerance: the anchor shift introduces one extra rounding compared to
    /// reading the source grid directly).
    /// </summary>
    private static (ElevationGrid Grid, GridCellStatus[,] Statuses) BuildCroppedGrid(
        ElevationGrid grid, double?[,] elevations, GridCellStatus[,] statuses,
        int rowStart, int rowEnd, int columnStart, int columnEnd)
    {
        int newRowCount = rowEnd - rowStart + 1;
        int newColumnCount = columnEnd - columnStart + 1;
        double?[,] croppedElevations = new double?[newRowCount, newColumnCount];
        GridCellStatus[,] croppedStatuses = new GridCellStatus[newRowCount, newColumnCount];

        for (int row = 0; row < newRowCount; row++)
        {
            for (int column = 0; column < newColumnCount; column++)
            {
                croppedElevations[row, column] = elevations[rowStart + row, columnStart + column];
                croppedStatuses[row, column] = statuses[rowStart + row, columnStart + column];
            }
        }

        double anchorX = grid.SouthwestAnchor.X + (columnStart * grid.CellSizeX);
        double anchorY = grid.RowOrder == GridRowOrder.SouthToNorth
            ? grid.SouthwestAnchor.Y + (rowStart * grid.CellSizeY)
            : grid.SouthwestAnchor.Y + ((grid.RowCount - 1 - rowEnd) * grid.CellSizeY);

        ElevationGrid croppedGrid = new(
            grid.HorizontalReference, grid.VerticalReference, new Coordinate2D(anchorX, anchorY),
            grid.CellSizeX, grid.CellSizeY, grid.AnchorConvention, grid.RowOrder, croppedElevations);
        return (croppedGrid, croppedStatuses);
    }
}
