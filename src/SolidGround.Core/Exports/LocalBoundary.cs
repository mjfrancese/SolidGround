using SolidGround.Core.Geometry;

namespace SolidGround.Core.Exports;

/// <summary>
/// One closed ring of a <see cref="LocalBoundaryPolygon"/>, in a <see cref="Transformations.LocalCoordinateFrame"/>'s
/// horizontal axes. <see cref="Vertices"/> never repeats its first vertex as a trailing closing vertex --
/// unlike <see cref="Aois.PolygonRings"/> (whose <c>Shell</c>/<c>Holes</c> always carry the OGC-style
/// duplicated closing coordinate) -- so a caller (for example <c>SolidGround.Revit</c>'s boundary-to-
/// <c>CurveLoop</c> mapper) always closes the loop itself, from the last vertex back to the first.
/// <see cref="LocalBoundaryFactory.FromPolygonalRegion"/> strips that duplicate when building a ring from a
/// <see cref="Aois.PolygonalRegion"/>; <see cref="LocalBoundaryValidator"/> rejects a ring that still carries
/// one (or any other pair of cyclically consecutive duplicate vertices) as invalid input. See SolidGround
/// Issue #15's design record, orchestrator decision (a).
/// </summary>
public sealed record LocalBoundaryRing(IReadOnlyList<LocalCoordinate2D> Vertices);

/// <summary>One polygon's exterior shell and zero or more interior holes, every ring in the same horizontal frame.</summary>
public sealed record LocalBoundaryPolygon(LocalBoundaryRing Shell, IReadOnlyList<LocalBoundaryRing> Holes);

/// <summary>
/// A package-neutral, Revit-free localized boundary: one or more polygons, each with optional holes, in a
/// <see cref="Transformations.LocalCoordinateFrame"/>'s horizontal axes. Deliberately two-dimensional --
/// <see cref="LocalCoordinate2D"/> carries no elevation -- because the boundary is a plan-view extent, not a
/// terrain shape; see SolidGround Issue #15's design record §7.2 for why the created <c>Toposolid</c>'s
/// boundary ring elevation is a single Revit-side scalar applied uniformly, never derived per-vertex from
/// this type.
/// </summary>
public sealed record LocalBoundary(IReadOnlyList<LocalBoundaryPolygon> Polygons);
