using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Transformations;

/// <summary>A horizontal coordinate transformation could not be built from, or applied using, Well-Known Text.</summary>
public sealed class HorizontalCoordinateTransformException : InvalidOperationException
{
    public HorizontalCoordinateTransformException() { }
    public HorizontalCoordinateTransformException(string message) : base(message) { }
    public HorizontalCoordinateTransformException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Builds a ProjNET-backed <see cref="IHorizontalCoordinateTransform"/> directly from raw WKT1 text.</summary>
public static class ProjNetHorizontalCoordinateTransformFactory
{
    /// <summary>The horizontal-transformation engine name recorded on every <see cref="HorizontalTransformationDefinition"/> this factory builds.</summary>
    public const string EngineName = "ProjNET";

    /// <summary>
    /// The pinned ProjNET NuGet package version recorded on every <see cref="HorizontalTransformationDefinition"/>
    /// this factory builds. Hardcoded, NOT <c>typeof(CoordinateSystemFactory).Assembly.GetName().Version</c>:
    /// that reflects <c>"2.0.0.0"</c>, which misleadingly does not match the pinned 2.1.0 NuGet package
    /// (verified empirically -- see
    /// <c>ProjNetHorizontalCoordinateTransformFactoryTests.EngineVersionIsThePinnedPackageVersionNotTheReflectedAssemblyVersion</c>).
    /// </summary>
    public const string EngineVersion = "2.1.0";

    /// <summary>
    /// A hardcoded EPSG:4326 geographic coordinate reference system, in ESRI-style WKT1, with no
    /// <c>TOWGS84</c> clause (SolidGround's accepted NAD83&lt;-&gt;WGS84 zero-datum-shift approximation; see
    /// docs/architecture/coordinate-transformation-and-units.md). Identical text to the existing
    /// <c>WellKnownTextReferenceParserTests.ParsesAGeographicRootWithAnAuthorityCode</c> fixture, so it is
    /// already independently proven to parse to coordinate reference system <c>"EPSG:4326"</c>. Passing this
    /// as <see cref="Create"/>'s source argument (paired with a projected target) is the geographic-source
    /// orientation <see cref="Create"/> always builds; see <see cref="HorizontalCoordinateTransforms.Reverse"/>
    /// for the opposite, projected-to-geographic orientation.
    /// </summary>
    public const string Wgs84WellKnownText = """
        GEOGCS["WGS 84",
            DATUM["WGS_1984",
                SPHEROID["WGS 84",6378137,298.257223563]],
            PRIMEM["Greenwich",0],
            UNIT["degree",0.0174532925199433],
            AUTHORITY["EPSG","4326"]]
        """;

    /// <summary>
    /// The maximum acceptable residual of a projected-reference round trip (<c>Forward(Inverse(x))</c>), in
    /// that reference's own linear unit (metres for the committed fixture). Measured 2026-09-19 against the
    /// pinned ProjNET 2.1.0 package and the committed <c>example-site-synthetic.prj</c> fixture
    /// (<c>Create(Wgs84WellKnownText, fixtureWkt)</c>, source geographic, target projected): the worst
    /// observed Inverse-then-Forward residual is ~8.633e-3 m at the fixture and ~9.292e-3 m across a broader
    /// zone-wide sweep. 0.02 m gives a small, deliberately documented margin (see
    /// docs/architecture/coordinate-transformation-and-units.md for the full measurement table) -- this
    /// bounds ProjNET's own numeric self-consistency for the geographic&lt;-&gt;projected leg of the engine
    /// ("numeric reversibility"), NOT survey/geodetic accuracy. SolidGround is a site-form tool, not a survey
    /// instrument (AGENTS.md's Accuracy section). The <see cref="LocalCoordinateFrame"/> leg (projected
    /// source &lt;-&gt; local) is bit-exact and is tested as such, separately.
    /// </summary>
    public static readonly LinearDistance ProjectedRoundTripTolerance = LinearDistance.Meters(0.02);

    /// <summary>
    /// The maximum acceptable residual of a geographic-reference round trip (<c>Inverse(Forward(x))</c>), in
    /// decimal degrees. Measured 2026-09-19, same spike as <see cref="ProjectedRoundTripTolerance"/>: the
    /// worst observed Forward-then-Inverse residual is ~7.776e-8 deg at the fixture and ~8.362e-8 deg across
    /// the same zone-wide sweep. See docs/architecture/coordinate-transformation-and-units.md for the full
    /// measurement table. Also numeric reversibility, not survey accuracy.
    /// </summary>
    public const double GeographicRoundTripToleranceDegrees = 2e-7;

    /// <summary>
    /// Builds a reversible horizontal transform from <paramref name="sourceWellKnownText"/> (which must
    /// resolve to a geographic coordinate reference system, for example WGS 84 or NAD83) to
    /// <paramref name="targetWellKnownText"/> (which must resolve to a projected coordinate reference system,
    /// for example a raster's own UTM zone). Each string may be a bare PROJCS/GEOGCS or a compound
    /// COMPD_CS/COMPOUNDCRS (its vertical part, if any, is parsed by SolidGround's own metadata but
    /// discarded before reaching ProjNET -- no vertical transform is ever attempted).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="sourceWellKnownText"/> or <paramref name="targetWellKnownText"/> is null, empty, or whitespace.</exception>
    /// <exception cref="HorizontalCoordinateTransformException">
    /// Either text cannot be interpreted by <see cref="WellKnownTextReferenceParser"/>; either text is WKT2 or
    /// otherwise not parseable by ProjNET (WKT1-only); either text resolves to a ProjNET coordinate system that
    /// is not geographic or projected (after unwrapping a compound coordinate system's head); the source does
    /// not resolve to a geographic coordinate system or the target does not resolve to a projected coordinate
    /// system (SolidGround transforms only geographic-to-projected, never the reverse and never
    /// geographic-to-geographic or projected-to-projected); ProjNET cannot build or invert a transformation
    /// between the two coordinate systems (for example, an unsupported projection method); or a runtime
    /// <c>Forward</c>/<c>Inverse</c> call fails or produces a non-finite result. No ProjNET exception type and
    /// no null ever escapes this type or the transform it returns.
    /// </exception>
    public static IHorizontalCoordinateTransform Create(string sourceWellKnownText, string targetWellKnownText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWellKnownText);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWellKnownText);

        HorizontalReference sourceReference = ParseSolidGroundReference(sourceWellKnownText, "source");
        HorizontalReference targetReference = ParseSolidGroundReference(targetWellKnownText, "target");

        CoordinateSystem sourceCs = ParseProjNetHorizontalCoordinateSystem(sourceWellKnownText, sourceReference, "source");
        CoordinateSystem targetCs = ParseProjNetHorizontalCoordinateSystem(targetWellKnownText, targetReference, "target");

        // SolidGround's own role contract, enforced before ProjNET ever sees the pair: a geographic source
        // reprojected to a projected target. This is deliberately stronger than ParseProjNetHorizontalCoordinateSystem's
        // per-role check (which only rules out an unsupported ProjNET coordinate system type, independent of
        // which role -- source or target -- is being validated). ProjNET 2.1.0 itself does not reject every
        // misuse of this contract: an independent spike (2026-09-19) found CreateFromCoordinateSystems builds a
        // Projected->Geographic transform, and even a Geographic->Geographic transform between two differing
        // datums with no TOWGS84 clause on either, without throwing -- silently producing an ellipsoid-only
        // conversion with no true datum shift, which would be a misleading, semantically-wrong "transform" for
        // SolidGround's established Forward=lon/lat->easting/northing convention (see
        // docs/architecture/coordinate-transformation-and-units.md). SolidGround therefore rejects both
        // misuses itself, with an actionable message, rather than relying on ProjNET to fail on its behalf.
        if (sourceCs is not GeographicCoordinateSystem)
        {
            throw new HorizontalCoordinateTransformException(
                $"The source coordinate reference system '{sourceReference.CoordinateReferenceSystem}' ({sourceReference.Datum}) " +
                "must be geographic (for example WGS 84 or NAD83); SolidGround transforms only from a geographic source to a " +
                "projected target (the raster's own coordinate reference system), never the reverse.");
        }

        if (targetCs is not ProjectedCoordinateSystem)
        {
            throw new HorizontalCoordinateTransformException(
                $"The target coordinate reference system '{targetReference.CoordinateReferenceSystem}' ({targetReference.Datum}) " +
                "must be projected (for example the raster's own UTM zone); SolidGround transforms only from a geographic " +
                "source to a projected target, never the reverse.");
        }

        ICoordinateTransformation transformation;
        try
        {
            transformation = new CoordinateTransformationFactory().CreateFromCoordinateSystems(sourceCs, targetCs);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET could not build a coordinate transformation from source reference " +
                $"'{sourceReference.CoordinateReferenceSystem}' ({sourceReference.Datum}) to target reference " +
                $"'{targetReference.CoordinateReferenceSystem}' ({targetReference.Datum}): {ex.Message}", ex);
        }

        MathTransform forward = transformation.MathTransform;
        MathTransform inverse;
        try
        {
            inverse = forward.Inverse();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET could not derive an inverse transform from target reference " +
                $"'{targetReference.CoordinateReferenceSystem}' back to source reference " +
                $"'{sourceReference.CoordinateReferenceSystem}': {ex.Message}", ex);
        }

        var definition = new HorizontalTransformationDefinition(
            sourceReference,
            targetReference,
            new CoordinateOperationDefinition("WKT1", targetWellKnownText),
            new CoordinateOperationDefinition("WKT1", sourceWellKnownText),
            EngineName,
            EngineVersion);

        return new ProjNetHorizontalCoordinateTransform(definition, forward, inverse);
    }

    private static HorizontalReference ParseSolidGroundReference(string wellKnownText, string role)
    {
        try
        {
            return WellKnownTextReferenceParser.Parse(wellKnownText).Horizontal;
        }
        catch (FormatException ex)
        {
            throw new HorizontalCoordinateTransformException(
                $"The {role} coordinate reference system's Well-Known Text could not be interpreted: {ex.Message}", ex);
        }
    }

    private static CoordinateSystem ParseProjNetHorizontalCoordinateSystem(string wellKnownText, HorizontalReference reference, string role)
    {
        CoordinateSystem? parsed;
        try
        {
            parsed = new CoordinateSystemFactory().CreateFromWkt(wellKnownText);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET could not parse the {role} coordinate reference system " +
                $"'{reference.CoordinateReferenceSystem}' ({reference.Datum}) from its Well-Known Text " +
                $"(ProjNET's WKT support is WKT1-only): {ex.Message}", ex);
        }

        if (parsed is null)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET returned no coordinate system for the {role} coordinate reference system " +
                $"'{reference.CoordinateReferenceSystem}' ({reference.Datum}).");
        }

        CoordinateSystem horizontal = parsed is CompoundCoordinateSystem compound ? compound.HeadCoordinateSystem : parsed;
        if (horizontal is not (GeographicCoordinateSystem or ProjectedCoordinateSystem))
        {
            throw new HorizontalCoordinateTransformException(
                $"The {role} coordinate reference system '{reference.CoordinateReferenceSystem}' ({reference.Datum}) " +
                $"resolved to an unsupported ProjNET coordinate system type '{horizontal.GetType().Name}'; SolidGround " +
                "supports only a geographic or projected horizontal definition, optionally paired with a vertical " +
                "definition in a compound coordinate reference system.");
        }

        return horizontal;
    }
}

/// <summary>ProjNET-backed <see cref="IHorizontalCoordinateTransform"/>. Not part of Core's public surface.</summary>
internal sealed class ProjNetHorizontalCoordinateTransform : IHorizontalCoordinateTransform
{
    private readonly MathTransform forward;
    private readonly MathTransform inverse;

    internal ProjNetHorizontalCoordinateTransform(HorizontalTransformationDefinition definition, MathTransform forward, MathTransform inverse)
    {
        Definition = definition;
        this.forward = forward;
        this.inverse = inverse;
    }

    public HorizontalTransformationDefinition Definition { get; }

    public Coordinate2D Forward(Coordinate2D source) => TransformCore(forward, source, "forward");

    public Coordinate2D Inverse(Coordinate2D target) => TransformCore(inverse, target, "inverse");

    private static Coordinate2D TransformCore(MathTransform transform, Coordinate2D input, string direction)
    {
        double x;
        double y;
        try
        {
            (x, y) = transform.Transform(input.X, input.Y);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET's {direction} transform failed for coordinate {FormatInput(input)}: {ex.Message}", ex);
        }

        try
        {
            return new Coordinate2D(x, y);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new HorizontalCoordinateTransformException(
                $"ProjNET's {direction} transform produced a non-finite coordinate for input {FormatInput(input)}.", ex);
        }
    }

    // Formatted only on a failure path (inside a catch block above), never on every call: GeometryInterop.FormatOrdinate
    // is pure but not free, and TransformCore's success path is the hot path for every retained sample or
    // parcel vertex a real acquisition transforms.
    private static string FormatInput(Coordinate2D input) =>
        $"({GeometryInterop.FormatOrdinate(input.X)}, {GeometryInterop.FormatOrdinate(input.Y)})";
}
