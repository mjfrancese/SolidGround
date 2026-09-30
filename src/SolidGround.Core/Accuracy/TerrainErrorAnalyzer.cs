using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Triangulate;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Accuracy;

public sealed record TerrainErrorReport(
    double MaximumAbsoluteResidual,
    double RootMeanSquareResidual,
    int ComparedCellCount,
    int UncoveredCellCount,
    IReadOnlyDictionary<TerrainCoverageGapReason, int> UncoveredByReason,
    LengthUnit Unit);

public enum TerrainCoverageGapReason
{
    NoEligibleTriangle,
    OutsideRetainedDomain,
    CrossesDisconnectedSupport,
}

public static class TerrainErrorAnalyzer
{
    /// <summary>
    /// Measures an exact-sample retained surface against every valid cell of <paramref name="grid"/>. A
    /// triangle is eligible only when its complete area is covered by the union of valid cell footprints and
    /// all three vertices are in one connected valid-cell component. This deliberately rejects the convex
    /// hull that NTS's Delaunay builder otherwise supplies across concavities, NODATA holes, and islands.
    /// </summary>
    public static TerrainErrorReport Analyze(ElevationGrid grid, IEnumerable<TerrainSample> retainedSamples, LengthUnit reportUnit)
    {
        ArgumentNullException.ThrowIfNull(grid);
        GridCellStatus[,] statuses = new GridCellStatus[grid.RowCount, grid.ColumnCount];
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                statuses[row, column] = grid.GetElevation(row, column) is null
                    ? GridCellStatus.SourceNoData
                    : GridCellStatus.Retained;
            }
        }

        return AnalyzeCore(grid, statuses, retainedSamples, reportUnit, requireExactElevation: true);
    }

    /// <summary>Measures only the valid cells retained by an explicit clip result, preserving its NODATA and region masks.</summary>
    public static TerrainErrorReport Analyze(GridClipResult clippedGrid, IEnumerable<TerrainSample> retainedSamples, LengthUnit reportUnit)
    {
        ArgumentNullException.ThrowIfNull(clippedGrid);
        return AnalyzeCore(clippedGrid.Grid, clippedGrid.GetCellStatuses(), retainedSamples, reportUnit, requireExactElevation: true);
    }

    /// <summary>
    /// Scores an independently supplied surface against a reference grid. Vertices may occur anywhere within
    /// a valid connected support footprint; the complete triangle must still be covered by that single
    /// component, so an arbitrary external mesh cannot bridge a hole, concavity, or disconnected island.
    /// </summary>
    public static TerrainErrorReport AnalyzeSurface(ElevationGrid referenceGrid, IEnumerable<TerrainSample> surfaceSamples, LengthUnit reportUnit)
    {
        ArgumentNullException.ThrowIfNull(referenceGrid);
        GridCellStatus[,] statuses = new GridCellStatus[referenceGrid.RowCount, referenceGrid.ColumnCount];
        for (int row = 0; row < referenceGrid.RowCount; row++)
        {
            for (int column = 0; column < referenceGrid.ColumnCount; column++)
            {
                statuses[row, column] = referenceGrid.GetElevation(row, column) is null ? GridCellStatus.SourceNoData : GridCellStatus.Retained;
            }
        }
        return AnalyzeCore(referenceGrid, statuses, surfaceSamples, reportUnit, requireExactElevation: false);
    }

    private static TerrainErrorReport AnalyzeCore(
        ElevationGrid grid,
        GridCellStatus[,] statuses,
        IEnumerable<TerrainSample> retainedSamples,
        LengthUnit reportUnit,
        bool requireExactElevation)
    {
        ArgumentNullException.ThrowIfNull(retainedSamples);
        _ = LengthConverter.MetersPerUnit(reportUnit);
        TerrainSample[] retained = retainedSamples.ToArray();
        if (retained.Any(sample => sample is null))
        {
            throw new ArgumentException("Retained samples cannot contain null values.", nameof(retainedSamples));
        }

        Dictionary<CoordinateKey, Cell> cellsByCoordinate = BuildCells(grid, statuses, out SupportDomain support);
        if (support.ValidDomain.IsEmpty)
        {
            return new TerrainErrorReport(0d, 0d, 0, 0, EmptyGaps(), reportUnit);
        }

        List<Coordinate> sites = new(retained.Length);
        HashSet<CoordinateKey> seen = [];
        Dictionary<CoordinateKey, double> elevationsByCoordinate = [];
        foreach (TerrainSample sample in retained)
        {
            CoordinateKey key = new(sample.Position.X, sample.Position.Y);
            if (!seen.Add(key))
            {
                throw new ArgumentException("Retained samples must have unique horizontal coordinates.", nameof(retainedSamples));
            }

            bool validExactSample = cellsByCoordinate.TryGetValue(key, out Cell? cell) && cell.Status == GridCellStatus.Retained;
            if ((requireExactElevation && (!validExactSample || BitConverter.DoubleToInt64Bits(cell!.Elevation) != BitConverter.DoubleToInt64Bits(sample.Position.Elevation)))
                || (!requireExactElevation && !support.ComponentDomains.Values.Any(domain => domain.Covers(support.ValidDomain.Factory.CreatePoint(new Coordinate(sample.Position.X, sample.Position.Y))))))
            {
                throw new ArgumentException(
                    requireExactElevation
                        ? "Every retained sample must be an exact, valid sample from the full-resolution clipped grid."
                        : "Every surface sample must lie within a valid reference-grid support footprint.", nameof(retainedSamples));
            }

            sites.Add(new Coordinate(sample.Position.X, sample.Position.Y));
            elevationsByCoordinate.Add(key, sample.Position.Elevation);
        }

        if (sites.Count < 3)
        {
            return AllValidCellsUncovered(grid, statuses, reportUnit, TerrainCoverageGapReason.NoEligibleTriangle);
        }

        List<Triangle> triangles = BuildTriangles(sites, elevationsByCoordinate, cellsByCoordinate, support, requireExactElevation);
        STRtree<Triangle> triangleIndex = new();
        foreach (Triangle triangle in triangles)
        {
            triangleIndex.Insert(triangle.Geometry.EnvelopeInternal, triangle);
        }
        triangleIndex.Build();

        int compared = 0;
        int uncovered = 0;
        double maxResidual = 0d;
        double sumSquares = 0d;
        Dictionary<TerrainCoverageGapReason, int> gaps = EmptyGaps();
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (statuses[row, column] != GridCellStatus.Retained)
                {
                    continue;
                }

                Coordinate2D center = grid.GetCellCenter(row, column);
                Point point = support.ValidDomain.Factory.CreatePoint(new Coordinate(center.X, center.Y));
                List<Triangle> candidates = triangleIndex.Query(point.EnvelopeInternal).OrderBy(t => t.Id).ToList();
                Triangle? triangle = candidates.FirstOrDefault(candidate => candidate.Eligible && candidate.Geometry.Covers(point));
                if (triangle is null)
                {
                    uncovered++;
                    TerrainCoverageGapReason reason = ClassifyGap(candidates, point);
                    gaps[reason]++;
                    continue;
                }

                double actual = grid.GetElevation(row, column)!.Value;
                double interpolated = BarycentricElevation(triangle, center.X, center.Y);
                double residual = Math.Abs(LengthConverter.Convert(actual - interpolated, grid.VerticalReference.Unit, reportUnit));
                maxResidual = Math.Max(maxResidual, residual);
                sumSquares += residual * residual;
                compared++;
            }
        }

        return new TerrainErrorReport(
            maxResidual,
            compared == 0 ? 0d : Math.Sqrt(sumSquares / compared),
            compared,
            uncovered,
            gaps,
            reportUnit);
    }

    private static Dictionary<CoordinateKey, Cell> BuildCells(ElevationGrid grid, GridCellStatus[,] statuses, out SupportDomain support)
    {
        List<Polygon> validFootprints = [];
        Dictionary<int, List<Polygon>> footprintsByComponent = [];
        Dictionary<CoordinateKey, Cell> cells = [];
        int component = 0;
        int[,] components = new int[grid.RowCount, grid.ColumnCount];
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                if (statuses[row, column] == GridCellStatus.Retained && components[row, column] == 0)
                {
                    FloodComponent(statuses, components, row, column, ++component);
                }
            }
        }

        GeometryFactory factory = new();
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                Coordinate2D center = grid.GetCellCenter(row, column);
                double elevation = grid.GetElevation(row, column) ?? 0d;
                Polygon? footprint = null;
                if (statuses[row, column] == GridCellStatus.Retained)
                {
                    footprint = CellFootprint(factory, center, grid.CellSizeX, grid.CellSizeY);
                    validFootprints.Add(footprint);
                    if (!footprintsByComponent.TryGetValue(components[row, column], out List<Polygon>? componentFootprints))
                    {
                        componentFootprints = [];
                        footprintsByComponent.Add(components[row, column], componentFootprints);
                    }
                    componentFootprints.Add(footprint);
                }
                Cell cell = new(statuses[row, column], elevation, components[row, column], footprint);
                cells.Add(new CoordinateKey(center.X, center.Y), cell);
            }
        }
        NtsGeometry validDomain = validFootprints.Count == 0 ? factory.CreatePolygon() : UnaryUnionOp.Union(validFootprints);
        Dictionary<int, NtsGeometry> componentDomains = footprintsByComponent.ToDictionary(
            pair => pair.Key,
            pair => (NtsGeometry)UnaryUnionOp.Union(pair.Value));
        support = new SupportDomain(validDomain, componentDomains);
        return cells;
    }

    private static void FloodComponent(GridCellStatus[,] statuses, int[,] components, int startRow, int startColumn, int component)
    {
        Queue<(int Row, int Column)> pending = new();
        pending.Enqueue((startRow, startColumn));
        components[startRow, startColumn] = component;
        while (pending.Count > 0)
        {
            (int row, int column) = pending.Dequeue();
            for (int rowDelta = -1; rowDelta <= 1; rowDelta++)
            {
                for (int columnDelta = -1; columnDelta <= 1; columnDelta++)
                {
                    int nextRow = row + rowDelta;
                    int nextColumn = column + columnDelta;
                    if ((rowDelta == 0 && columnDelta == 0) || nextRow < 0 || nextColumn < 0
                        || nextRow >= statuses.GetLength(0) || nextColumn >= statuses.GetLength(1)
                        || components[nextRow, nextColumn] != 0 || statuses[nextRow, nextColumn] != GridCellStatus.Retained)
                    {
                        continue;
                    }
                    components[nextRow, nextColumn] = component;
                    pending.Enqueue((nextRow, nextColumn));
                }
            }
        }
    }

    private static Polygon CellFootprint(GeometryFactory factory, Coordinate2D center, double sizeX, double sizeY)
    {
        double halfX = sizeX / 2d;
        double halfY = sizeY / 2d;
        return factory.CreatePolygon([
            new Coordinate(center.X - halfX, center.Y - halfY), new Coordinate(center.X + halfX, center.Y - halfY),
            new Coordinate(center.X + halfX, center.Y + halfY), new Coordinate(center.X - halfX, center.Y + halfY),
            new Coordinate(center.X - halfX, center.Y - halfY),
        ]);
    }

    private static List<Triangle> BuildTriangles(
        IReadOnlyList<Coordinate> sites,
        IReadOnlyDictionary<CoordinateKey, double> elevations,
        IReadOnlyDictionary<CoordinateKey, Cell> cells,
        SupportDomain support,
        bool requireExactElevation)
    {
        DelaunayTriangulationBuilder builder = new();
        builder.SetSites(support.ValidDomain.Factory.CreateMultiPointFromCoords([.. sites]));
        NtsGeometry trianglesGeometry = builder.GetTriangles(support.ValidDomain.Factory);
        List<Triangle> triangles = [];
        for (int index = 0; index < trianglesGeometry.NumGeometries; index++)
        {
            Polygon polygon = (Polygon)trianglesGeometry.GetGeometryN(index);
            Coordinate[] coordinates = polygon.Coordinates;
            if (coordinates.Length != 4)
            {
                continue;
            }
            Coordinate a = coordinates[0];
            Coordinate b = coordinates[1];
            Coordinate c = coordinates[2];
            int? firstComponent = ResolveComponent(a, cells, support, requireExactElevation);
            int? secondComponent = ResolveComponent(b, cells, support, requireExactElevation);
            int? thirdComponent = ResolveComponent(c, cells, support, requireExactElevation);
            bool sameComponent = firstComponent is int component && secondComponent == component && thirdComponent == component;
            bool eligible = sameComponent && support.ComponentDomains[firstComponent!.Value].Covers(polygon);
            double Elevation(Coordinate coordinate) => elevations[new CoordinateKey(coordinate.X, coordinate.Y)];
            triangles.Add(new Triangle(
                index,
                polygon,
                new Vertex(a.X, a.Y, Elevation(a)),
                new Vertex(b.X, b.Y, Elevation(b)),
                new Vertex(c.X, c.Y, Elevation(c)),
                eligible,
                sameComponent));
        }
        return triangles;
    }

    private static int? ResolveComponent(Coordinate coordinate, IReadOnlyDictionary<CoordinateKey, Cell> cells, SupportDomain support, bool requireExactElevation)
    {
        if (requireExactElevation && cells.TryGetValue(new CoordinateKey(coordinate.X, coordinate.Y), out Cell? exact))
        {
            return exact.Component;
        }
        Point point = support.ValidDomain.Factory.CreatePoint(coordinate);
        int[] components = [.. support.ComponentDomains.Where(pair => pair.Value.Covers(point)).Select(pair => pair.Key).OrderBy(component => component)];
        return components.Length == 1 ? components[0] : null;
    }

    private static double BarycentricElevation(Triangle triangle, double x, double y)
    {
        double denominator = ((triangle.B.Y - triangle.C.Y) * (triangle.A.X - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (triangle.A.Y - triangle.C.Y));
        if (denominator == 0d)
        {
            throw new InvalidOperationException("Delaunay triangulation returned a degenerate triangle.");
        }
        double aWeight = (((triangle.B.Y - triangle.C.Y) * (x - triangle.C.X)) + ((triangle.C.X - triangle.B.X) * (y - triangle.C.Y))) / denominator;
        double bWeight = (((triangle.C.Y - triangle.A.Y) * (x - triangle.C.X)) + ((triangle.A.X - triangle.C.X) * (y - triangle.C.Y))) / denominator;
        double cWeight = 1d - aWeight - bWeight;
        return (aWeight * triangle.A.Elevation) + (bWeight * triangle.B.Elevation) + (cWeight * triangle.C.Elevation);
    }

    private static TerrainCoverageGapReason ClassifyGap(IEnumerable<Triangle> candidates, Point point)
    {
        List<Triangle> containing = candidates.Where(candidate => candidate.Geometry.Covers(point)).ToList();
        if (containing.Any(candidate => !candidate.SameComponent))
        {
            return TerrainCoverageGapReason.CrossesDisconnectedSupport;
        }
        return containing.Count > 0 ? TerrainCoverageGapReason.OutsideRetainedDomain : TerrainCoverageGapReason.NoEligibleTriangle;
    }

    private static TerrainErrorReport AllValidCellsUncovered(ElevationGrid grid, GridCellStatus[,] statuses, LengthUnit reportUnit, TerrainCoverageGapReason reason)
    {
        int valid = 0;
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                valid += statuses[row, column] == GridCellStatus.Retained ? 1 : 0;
            }
        }
        Dictionary<TerrainCoverageGapReason, int> gaps = EmptyGaps();
        gaps[reason] = valid;
        return new TerrainErrorReport(0d, 0d, 0, valid, gaps, reportUnit);
    }

    private static Dictionary<TerrainCoverageGapReason, int> EmptyGaps() => Enum.GetValues<TerrainCoverageGapReason>().ToDictionary(reason => reason, _ => 0);

    private readonly record struct CoordinateKey(long X, long Y)
    {
        public CoordinateKey(double x, double y) : this(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(y)) { }
    }

    private sealed record SupportDomain(NtsGeometry ValidDomain, IReadOnlyDictionary<int, NtsGeometry> ComponentDomains);
    private sealed record Cell(GridCellStatus Status, double Elevation, int Component, Polygon? Footprint);
    private sealed record Vertex(double X, double Y, double Elevation);
    private sealed record Triangle(int Id, Polygon Geometry, Vertex A, Vertex B, Vertex C, bool Eligible, bool SameComponent);
}
