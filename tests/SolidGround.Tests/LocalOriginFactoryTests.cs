using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="LocalOriginFactory"/>, lifted into <c>SolidGround.Core</c> for SolidGround
/// Issue #15 with its body unchanged (only its <see cref="LocalOriginRequest"/> parameter type is new). See
/// docs/architecture/cli-workflow.md's "Local origin selection and its consequences" section.
/// </summary>
public sealed class LocalOriginFactoryTests
{
    // 4x4 cells, cellsize 2, anchored at a whole-number lower-left corner: the resulting corner envelope
    // (minX=10, minY=20, maxX=18, maxY=28) and its center (14, 24) are already whole numbers, so
    // LocalOriginSnapping's flooring cannot change either expected value -- letting the Southwest/Centroid
    // assertions below be exact equality rather than an approximate/floored comparison.
    private static readonly ElevationGrid TestGrid = BuildGrid(rowCount: 4, columnCount: 4, cellSize: 2d, anchor: new Coordinate2D(10d, 20d));

    [Fact]
    public void ComputeOriginForSouthwestReturnsTheClippedGridsMinimumCorner()
    {
        LocalOriginRequest selection = new(LocalOriginKind.Southwest, 0, 0, 0);

        Coordinate3D origin = LocalOriginFactory.ComputeOrigin(selection, TestGrid, ProjectedReference(), VerticalReference());

        PlanarEnvelope envelope = TestGrid.GetCornerEnvelope();
        Assert.Equal(envelope.MinX, origin.X);
        Assert.Equal(envelope.MinY, origin.Y);
        Assert.Equal(0d, origin.Elevation);
    }

    [Fact]
    public void ComputeOriginForCentroidReturnsTheClippedGridsCenter()
    {
        LocalOriginRequest selection = new(LocalOriginKind.Centroid, 0, 0, 0);

        Coordinate3D origin = LocalOriginFactory.ComputeOrigin(selection, TestGrid, ProjectedReference(), VerticalReference());

        PlanarEnvelope envelope = TestGrid.GetCornerEnvelope();
        Assert.Equal((envelope.MinX + envelope.MaxX) / 2d, origin.X);
        Assert.Equal((envelope.MinY + envelope.MaxY) / 2d, origin.Y);
        Assert.Equal(0d, origin.Elevation);
    }

    [Fact]
    public void ComputeOriginForExplicitReturnsTheGivenCoordinateUnsnapped()
    {
        // Deliberately fractional, unlike TestGrid's whole-number corners above: Explicit is the one kind
        // LocalOriginFactory never routes through LocalOriginSnapping, so these exact fractional values must
        // survive unchanged.
        LocalOriginRequest selection = new(LocalOriginKind.Explicit, [withheld], [withheld], 184.1d);

        Coordinate3D origin = LocalOriginFactory.ComputeOrigin(selection, TestGrid, ProjectedReference(), VerticalReference());

        Assert.Equal(new Coordinate3D([withheld], [withheld], 184.1d), origin);
    }

    private static ElevationGrid BuildGrid(int rowCount, int columnCount, double cellSize, Coordinate2D anchor)
    {
        double?[,] values = new double?[rowCount, columnCount];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                values[row, column] = 1d;
            }
        }

        return new ElevationGrid(
            ProjectedReference(), VerticalReference(), anchor, cellSize, cellSize,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, values);
    }

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
