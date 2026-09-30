using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SolidGround.Core.Geometry;

/// <summary>Canonical, order-independent fingerprint for the native vertex set persisted after Revit verification.</summary>
public static class TerrainVertexFingerprint
{
    public static string Compute(IEnumerable<Coordinate3D> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        string canonical = string.Join("\n", vertices
            .OrderBy(vertex => vertex.X).ThenBy(vertex => vertex.Y).ThenBy(vertex => vertex.Elevation)
            .Select(vertex => string.Create(CultureInfo.InvariantCulture, $"{vertex.X:R},{vertex.Y:R},{vertex.Elevation:R}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
