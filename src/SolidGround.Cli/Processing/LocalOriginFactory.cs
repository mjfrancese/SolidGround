using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Cli.Processing;

/// <summary>
/// Computes the local origin point from a <see cref="LocalOriginSelection"/>. See
/// docs/architecture/cli-workflow.md's "Local origin selection and its consequences" section: `southwest`
/// and `centroid` read the clipped grid's own cell-corner envelope through
/// <see cref="ElevationGrid.GetCornerEnvelope"/> -- the one member Issue #9 adds to Core -- rather than this
/// type re-deriving the grid's anchor-convention semantics itself.
/// </summary>
internal static class LocalOriginFactory
{
    internal static Coordinate3D ComputeOrigin(
        LocalOriginSelection selection,
        ElevationGrid clippedGrid,
        HorizontalReference projectedReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(clippedGrid);
        ArgumentNullException.ThrowIfNull(projectedReference);
        ArgumentNullException.ThrowIfNull(verticalReference);

        switch (selection.Kind)
        {
            case LocalOriginKind.Southwest:
            {
                PlanarEnvelope envelope = clippedGrid.GetCornerEnvelope();
                return LocalOriginSnapping.SnapToWholeSourceUnit(new Coordinate3D(envelope.MinX, envelope.MinY, 0d), projectedReference, verticalReference);
            }

            case LocalOriginKind.Centroid:
            {
                PlanarEnvelope envelope = clippedGrid.GetCornerEnvelope();
                return LocalOriginSnapping.SnapToWholeSourceUnit(
                    new Coordinate3D((envelope.MinX + envelope.MaxX) / 2d, (envelope.MinY + envelope.MaxY) / 2d, 0d), projectedReference, verticalReference);
            }

            case LocalOriginKind.Explicit:
                return new Coordinate3D(selection.X, selection.Y, selection.Z);

            default:
                throw new ArgumentOutOfRangeException(nameof(selection));
        }
    }
}
