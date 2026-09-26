using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// Converts a resolved <see cref="ParcelBoundaryCandidate"/> into today's <see cref="ParcelGeometryAoi"/> --
/// owner decision 8 (2026-09-21): a resolved parcel converts into today's parcel AOI shape, no new
/// <c>AreaOfInterestKind</c>. Placed here (mirroring <c>AoiSettingsFactory</c>'s own placement/naming for
/// "build the real AOI type"), not as an instance method on <see cref="ParcelBoundaryCandidate"/>, so
/// <c>SolidGround.Core.Sources</c> never needs to reference <see cref="WKTWriter"/> at all -- the NTS usage
/// stays entirely inside this one method body, never a public signature element.
/// </summary>
public static class ParcelBoundaryAoiFactory
{
    /// <summary>
    /// Writes <paramref name="candidate"/>'s boundary as WKT and wraps it in a <see cref="ParcelGeometryAoi"/>
    /// carrying the boundary's own horizontal reference and the supplied <paramref name="buffer"/>. WKT (not
    /// GeoJSON) is the round-trip format because NTS's <see cref="WKTWriter"/>/<see cref="WKTReader"/> pair is
    /// already in use elsewhere in this codebase and is an exact inverse for the polygon/multipolygon-with-holes
    /// shapes a resolved candidate can carry.
    /// </summary>
    public static ParcelGeometryAoi FromCandidate(ParcelBoundaryCandidate candidate, LinearDistance? buffer = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string wkt = new WKTWriter().Write(candidate.Boundary.Geometry);
        return new ParcelGeometryAoi(ParcelGeometryFormat.Wkt, wkt, candidate.Boundary.HorizontalReference, buffer);
    }
}
