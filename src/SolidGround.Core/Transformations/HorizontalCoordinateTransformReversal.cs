using SolidGround.Core.Geometry;

namespace SolidGround.Core.Transformations;

/// <summary>
/// Builds the reverse of any <see cref="IHorizontalCoordinateTransform"/>, so a seam that needs the opposite
/// direction from the one an existing transform was built in does not need its own engine-specific adapter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Orientation contract.</b> <see cref="ProjNetHorizontalCoordinateTransformFactory.Create"/> always builds
/// a geographic-source, projected-target transform: its <c>Forward</c> maps (longitude, latitude) to
/// (easting, northing) and its <c>Inverse</c> maps back. That is the orientation
/// <see cref="Provenance.TerrainProvenance"/> and <see cref="LocalCoordinateFrame"/> both require --
/// <c>TerrainProvenance</c>'s constructor requires
/// <c>horizontalTransformation.TargetReference == localFrame.ProjectedHorizontalReference</c>, and
/// <c>LocalCoordinateFrame</c> itself requires a projected horizontal reference -- so a transform built by
/// <c>Create</c> can be handed to either directly.
/// </para>
/// <para>
/// <see cref="HorizontalCoordinateTransforms.Reverse(IHorizontalCoordinateTransform)"/> produces the opposite
/// orientation: a projected-source, geographic-target transform whose <c>Forward</c> maps (easting, northing)
/// to (longitude, latitude). The motivating seam is <see cref="Aois.AoiNormalizer"/>'s injected
/// <c>parcelToWgs84</c> transform (<c>AoiNormalizer.NormalizeProjectedParcel</c>), which calls <c>.Forward</c>
/// on vertices that are already in the parcel's own projected reference and treats the result as WGS 84
/// longitude/latitude directly -- exactly the projected-to-geographic direction
/// <c>Reverse(Create(wgs84Wkt, parcelWkt))</c> provides. Handing that seam the un-reversed
/// <c>Create(wgs84Wkt, parcelWkt)</c> transform instead would apply the wrong direction: its <c>Forward</c>
/// expects geographic input and would treat an already-projected easting/northing pair as if it were a
/// longitude/latitude pair.
/// </para>
/// </remarks>
public static class HorizontalCoordinateTransforms
{
    /// <summary>
    /// Returns a transform that is the reverse of <paramref name="transform"/>: its
    /// <see cref="IHorizontalCoordinateTransform.Forward"/> calls <paramref name="transform"/>'s own
    /// <see cref="IHorizontalCoordinateTransform.Inverse"/>, and its
    /// <see cref="IHorizontalCoordinateTransform.Inverse"/> calls <paramref name="transform"/>'s own
    /// <see cref="IHorizontalCoordinateTransform.Forward"/>. Its
    /// <see cref="IHorizontalCoordinateTransform.Definition"/> is <paramref name="transform"/>'s own
    /// <see cref="HorizontalTransformationDefinition"/> with
    /// <see cref="HorizontalTransformationDefinition.SourceReference"/> and
    /// <see cref="HorizontalTransformationDefinition.TargetReference"/> swapped, and its
    /// <see cref="HorizontalTransformationDefinition.ForwardOperation"/> and
    /// <see cref="HorizontalTransformationDefinition.InverseOperation"/> swapped; the engine name and version
    /// are unchanged. See this type's own remarks for the orientation contract this exists to serve.
    /// </summary>
    /// <remarks>
    /// Reversing twice returns a transform whose <see cref="IHorizontalCoordinateTransform.Definition"/>
    /// equals <paramref name="transform"/>'s own <see cref="IHorizontalCoordinateTransform.Definition"/>
    /// (record equality) and whose <c>Forward</c>/<c>Inverse</c> delegate to the exact same underlying calls
    /// as <paramref name="transform"/>'s own. Works with any implementation of
    /// <see cref="IHorizontalCoordinateTransform"/> -- including, but not limited to, the ProjNET-backed
    /// transform <see cref="ProjNetHorizontalCoordinateTransformFactory.Create"/> returns -- and never exposes
    /// an engine-specific type (for example a ProjNET type) in its own signature or return value.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="transform"/> is <see langword="null"/>.</exception>
    public static IHorizontalCoordinateTransform Reverse(IHorizontalCoordinateTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return new ReversedHorizontalCoordinateTransform(transform);
    }
}

/// <summary>
/// Wraps an <see cref="IHorizontalCoordinateTransform"/> to expose it in the opposite direction. See
/// <see cref="HorizontalCoordinateTransforms.Reverse(IHorizontalCoordinateTransform)"/> for the orientation
/// contract this serves. Not part of Core's public surface: callers only ever see this type through the
/// <see cref="IHorizontalCoordinateTransform"/> interface that method returns, exactly like the internal
/// ProjNET-backed implementation <see cref="ProjNetHorizontalCoordinateTransformFactory.Create"/> returns.
/// </summary>
internal sealed class ReversedHorizontalCoordinateTransform : IHorizontalCoordinateTransform
{
    private readonly IHorizontalCoordinateTransform inner;

    internal ReversedHorizontalCoordinateTransform(IHorizontalCoordinateTransform inner)
    {
        this.inner = inner;
        Definition = new HorizontalTransformationDefinition(
            inner.Definition.TargetReference,
            inner.Definition.SourceReference,
            inner.Definition.InverseOperation,
            inner.Definition.ForwardOperation,
            inner.Definition.EngineName,
            inner.Definition.EngineVersion);
    }

    public HorizontalTransformationDefinition Definition { get; }

    public Coordinate2D Forward(Coordinate2D source) => inner.Inverse(source);

    public Coordinate2D Inverse(Coordinate2D target) => inner.Forward(target);
}
