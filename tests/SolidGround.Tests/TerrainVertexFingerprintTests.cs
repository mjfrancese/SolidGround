using SolidGround.Core.Geometry;

namespace SolidGround.Tests;

public sealed class TerrainVertexFingerprintTests
{
    [Fact]
    public void IsOrderIndependentButDetectsAnEditedVertex()
    {
        Coordinate3D[] vertices = [new(0d, 0d, 0d), new(1d, 0d, 2d), new(0d, 1d, 3d)];

        string first = TerrainVertexFingerprint.Compute(vertices);
        string reordered = TerrainVertexFingerprint.Compute(vertices.Reverse());
        string edited = TerrainVertexFingerprint.Compute([vertices[0], new Coordinate3D(1d, 0d, 2.01d), vertices[2]]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, edited);
    }

    [Fact]
    public void IncludesOrderedNativeProfileTopology()
    {
        Coordinate3D[] vertices = [new(0d, 0d, 0d), new(1d, 0d, 0d)];
        string first = TerrainVertexFingerprint.Compute(vertices, ["loop:0;curve:0;line:0,0|1,0", "loop:0;curve:1;line:1,0|0,0"]);
        string changedConnectivity = TerrainVertexFingerprint.Compute(vertices, ["loop:0;curve:0;line:0,0|1,0", "loop:1;curve:0;line:1,0|0,0"]);

        Assert.NotEqual(first, changedConnectivity);
    }
}
