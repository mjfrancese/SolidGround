using SolidGround.Core.Geometry;
using SolidGround.Core.Units;

namespace SolidGround.Core.Transformations;

/// <summary>
/// Resolves a <see cref="LocalCoordinateFrame"/>'s own origin -- the exact real-world, source-projected
/// coordinate SolidGround's local (0,0,0) stands for -- paired with the two units needed to interpret it.
/// Added for SolidGround Issue #30's (PH3-3) shared-coordinates write. Deliberately never reads
/// <see cref="LocalCoordinateFrame.OutputUnit"/>: that property governs only <see cref="LocalCoordinateFrame.ToLocal"/>/
/// <see cref="LocalCoordinateFrame.ToLocalHorizontal"/>'s shifted results, never <see cref="LocalCoordinateFrame.Origin"/>
/// itself. See docs/architecture/revit-property-line-and-shared-coordinates.md's "Unit convention for the
/// shared-coordinates value" section for the full contract this type implements, including the failure mode it
/// exists to rule out.
/// </summary>
public static class SharedCoordinateOrigin
{
    /// <summary><paramref name="frame"/>'s own origin, paired with the exact unit needed to interpret each axis.</summary>
    public readonly record struct Resolved(Coordinate3D Origin, LengthUnit HorizontalUnit, LengthUnit VerticalUnit);

    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    public static Resolved Resolve(LocalCoordinateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // LinearUnit is guaranteed non-null here: LocalCoordinateFrame's own constructor already requires
        // ProjectedHorizontalReference.Kind == Projected and rejects a null LinearUnit for that kind.
        LengthUnit horizontalUnit = frame.ProjectedHorizontalReference.Unit.LinearUnit!.Value;
        return new Resolved(frame.Origin, horizontalUnit, frame.VerticalReference.Unit);
    }
}
