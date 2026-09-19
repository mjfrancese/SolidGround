using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;

namespace SolidGround.Core.Transformations;

/// <summary>
/// Snaps a caller-supplied candidate local origin to a whole number of its own source units on every axis, by
/// flooring: a candidate's X and Y floor to a whole number of the projected horizontal reference's linear
/// unit (whole metres for a UTM zone), and its Elevation floors to a whole number of the vertical reference's
/// unit. This does not choose WHICH point becomes the origin (a southwest corner, a centroid, or any other
/// point remain the caller's choice -- Issue #9's CLI is expected to expose that choice); it only makes
/// whatever candidate is chosen exactly representable as a short whole-number decimal, in its own source
/// units. Flooring guarantees a minimum-corner candidate's local coordinates stay non-negative.
/// </summary>
public static class LocalOriginSnapping
{
    /// <summary>
    /// Snaps <paramref name="candidate"/> to a whole number of source units on every axis. Snapping happens in
    /// the SOURCE coordinate reference's own units, never a chosen output unit: the local origin is carried in
    /// provenance in the authoritative source coordinate reference system (the projected horizontal reference
    /// and the vertical reference), so a whole number of source units is exactly representable there and reads
    /// cleanly, with no unit-conversion round trip needed to interpret it. <see cref="LocalCoordinateFrame"/>'s
    /// own <c>ToLocal</c>/<c>ToSource</c> round trip is bit-exact regardless of the origin's value, so snapping
    /// never affects that round trip's exactness -- it only makes the origin itself a short, exact decimal.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="projectedHorizontalReference"/> or <paramref name="verticalReference"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectedHorizontalReference"/> is not <see cref="HorizontalReferenceKind.Projected"/>, or
    /// has no linear unit.
    /// </exception>
    public static Coordinate3D SnapToWholeSourceUnit(
        Coordinate3D candidate,
        HorizontalReference projectedHorizontalReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(projectedHorizontalReference);
        ArgumentNullException.ThrowIfNull(verticalReference);
        if (projectedHorizontalReference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new ArgumentException("Snapping a local origin requires a projected horizontal reference.", nameof(projectedHorizontalReference));
        }

        if (projectedHorizontalReference.Unit.LinearUnit is null)
        {
            throw new ArgumentException("A projected horizontal reference requires a linear unit.", nameof(projectedHorizontalReference));
        }

        // No unit conversion: candidate.X/Y are already expressed in projectedHorizontalReference's own linear
        // unit, and candidate.Elevation is already expressed in verticalReference's own unit, exactly like
        // every other Coordinate3D that flows through LocalCoordinateFrame's Origin. Flooring directly in that
        // native unit is what "a whole number of source units" means; there is no output unit to convert to.
        double x = Math.Floor(candidate.X);
        double y = Math.Floor(candidate.Y);
        double elevation = Math.Floor(candidate.Elevation);
        return new Coordinate3D(x, y, elevation);
    }
}
