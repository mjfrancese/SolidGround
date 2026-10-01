using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Processing;

/// <summary>
/// Computes the local origin point from a <see cref="LocalOriginRequest"/>. See
/// docs/architecture/cli-workflow.md's "Local origin selection and its consequences" section: `southwest`
/// and `centroid` read the clipped grid's own cell-corner envelope through
/// <see cref="ElevationGrid.GetCornerEnvelope"/> -- the one member Issue #9 adds to Core -- rather than this
/// type re-deriving the grid's anchor-convention semantics itself. <see cref="LocalOriginKind.AreaCentroid"/>
/// is an opt-in precise midpoint for a grid and the true area centroid for an unbuffered legal parcel.
/// Lifted into <c>SolidGround.Core</c> for SolidGround Issue #15.
/// </summary>
public static class LocalOriginFactory
{
    /// <summary>
    /// Computes the historic southwest or centroid origin from an unbuffered legal parcel envelope, or the
    /// exact unsnapped area centroid for <see cref="LocalOriginKind.AreaCentroid"/>. This is used only by the
    /// opted-in parcel workflow so the legal PropertyLine frame cannot move when terrain context grows.
    /// Explicit origins remain exactly explicit.
    /// </summary>
    public static Coordinate3D ComputeOrigin(
        LocalOriginRequest selection,
        PolygonalRegion legalParcelRegion,
        HorizontalReference projectedReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(legalParcelRegion);
        ArgumentNullException.ThrowIfNull(projectedReference);
        ArgumentNullException.ThrowIfNull(verticalReference);
        if (legalParcelRegion.HorizontalReference != projectedReference)
        {
            throw new ArgumentException("The legal parcel region's horizontal reference must equal the projected reference.", nameof(legalParcelRegion));
        }

        if (selection.Kind == LocalOriginKind.AreaCentroid)
        {
            NetTopologySuite.Geometries.Coordinate centroid = legalParcelRegion.Geometry.Centroid.Coordinate;
            return new Coordinate3D(centroid.X, centroid.Y, 0d);
        }

        return ComputeOrigin(selection, legalParcelRegion.Envelope, projectedReference, verticalReference);
    }

    public static Coordinate3D ComputeOrigin(
        LocalOriginRequest selection,
        ElevationGrid clippedGrid,
        HorizontalReference projectedReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(clippedGrid);
        ArgumentNullException.ThrowIfNull(projectedReference);
        ArgumentNullException.ThrowIfNull(verticalReference);

        return ComputeOrigin(selection, clippedGrid.GetCornerEnvelope(), projectedReference, verticalReference);
    }

    private static Coordinate3D ComputeOrigin(
        LocalOriginRequest selection,
        PlanarEnvelope envelope,
        HorizontalReference projectedReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(projectedReference);
        ArgumentNullException.ThrowIfNull(verticalReference);

        switch (selection.Kind)
        {
            case LocalOriginKind.Southwest:
            {
                return LocalOriginSnapping.SnapToWholeSourceUnit(new Coordinate3D(envelope.MinX, envelope.MinY, 0d), projectedReference, verticalReference);
            }

            case LocalOriginKind.Centroid:
            {
                return LocalOriginSnapping.SnapToWholeSourceUnit(
                    new Coordinate3D((envelope.MinX + envelope.MaxX) / 2d, (envelope.MinY + envelope.MaxY) / 2d, 0d), projectedReference, verticalReference);
            }

            case LocalOriginKind.AreaCentroid:
                return new Coordinate3D((envelope.MinX + envelope.MaxX) / 2d, (envelope.MinY + envelope.MaxY) / 2d, 0d);

            case LocalOriginKind.Explicit:
                return new Coordinate3D(selection.X, selection.Y, selection.Z);

            default:
                throw new ArgumentOutOfRangeException(nameof(selection));
        }
    }
}
