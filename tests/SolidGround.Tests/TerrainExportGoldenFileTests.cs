using System.Globalization;
using System.Text.Json;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Rasters;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// The deterministic end-to-end golden-file contract, plus a separate in-process determinism check for the
/// real parcel-clipping path. See docs/architecture/provenance-and-deterministic-exports.md's "Determinism
/// rules", "Export document manifest, schema version 1", and "NODATA, empty candidate sets, and statistics"
/// sections for the contract this file proves byte-for-byte (the golden pipeline) and behaviorally (the real
/// parcel pipeline). The golden pipeline never calls <see cref="IHorizontalCoordinateTransform.Forward"/>,
/// <see cref="IHorizontalCoordinateTransform.Inverse"/>, or a geometric NTS buffer operation: it uses only
/// <see cref="HorizontalTransformationDefinition"/>'s own text (never a transformed coordinate) and a
/// zero-buffer <see cref="ClipRegion"/>, which GridClipper's own effective-region builder skips the NTS
/// buffer call for entirely (confirmed directly against GridClipper.BuildEffectiveRegion: it returns
/// ClipRegion.Region unchanged whenever ClipRegion.Buffer.Value == 0d). So the golden bytes depend only on
/// IEEE add/multiply/divide/compare, managed number formatting, and SHA-256, never on a transcendental
/// function whose last bit could differ between the Windows workstation and the Linux solidground-pve2
/// runner. The real-parcel pipeline below intentionally does call ProjNET's forward transform and an NTS
/// geometric buffer, so it is asserted deterministic only within one process, never byte-compared to a
/// committed golden.
/// </summary>
public sealed class TerrainExportGoldenFileTests
{
    private const string GoldenBaseName = "robandee-synthetic";
    private const string GoldenDocumentFileName = GoldenBaseName + TerrainExportBundleRenderer.DocumentFileSuffix;
    private const string GoldenPointsFileName = GoldenBaseName + TerrainExportBundleRenderer.PointsFileSuffix;

    // A fixed candidate inside both clip regions below, in the grid's own source (projected metre, NAVD88
    // metre) units; LocalOriginSnapping floors each axis independently to a deterministic grid-aligned origin
    // (719348, 4286526, 183).
    private static readonly Coordinate3D GoldenOriginCandidate = new(719348.7d, 4286526.3d, 183.9d);

    // A second, different candidate (still inside the golden clip region) that snaps to a different whole-number
    // origin (719347, 4286527, 184), used only to prove ToSource reversibility does not depend on which origin
    // was chosen.
    private static readonly Coordinate3D AlternateOriginCandidate = new(719347.6d, 4286527.2d, 184.1d);

    [Fact]
    public async Task GoldenPipelineRendersBytesIdenticalToTheCommittedGoldenFiles()
    {
        FixtureContext fixture = LoadFixtureContext();
        TerrainExportPayload payload = await RunGoldenPipelineAsync(
            fixture, LengthUnit.UsSurveyFoot, GoldenOriginCandidate, TestContext.Current.CancellationToken);

        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, GoldenBaseName);

        Assert.Equal(GoldenDocumentFileName, bundle.DocumentFileName);
        Assert.Equal(GoldenPointsFileName, bundle.PointsFileName);

        AssertMatchesGoldenFile(GoldenDocumentFileName, bundle.DocumentBytes.ToArray());
        AssertMatchesGoldenFile(GoldenPointsFileName, bundle.PointsBytes.ToArray());
    }

    [Fact]
    public async Task GoldenPipelineUnderInternationalFootScalesSampleCoordinatesAndKeepsOriginAndElevationRangeBitIdentical()
    {
        FixtureContext fixture = LoadFixtureContext();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        TerrainExportPayload usSurveyFootPayload = await RunGoldenPipelineAsync(
            fixture, LengthUnit.UsSurveyFoot, GoldenOriginCandidate, cancellationToken);
        TerrainExportPayload internationalFootPayload = await RunGoldenPipelineAsync(
            fixture, LengthUnit.InternationalFoot, GoldenOriginCandidate, cancellationToken);

        Assert.NotEmpty(usSurveyFootPayload.Samples);
        Assert.Equal(usSurveyFootPayload.Samples.Count, internationalFootPayload.Samples.Count);

        // (1200/3937) m per US survey foot, divided by 0.3048 m per international foot: converting the same
        // metre delta into international feet instead of US survey feet multiplies the result by exactly this
        // ratio (both are LengthConverter.Convert(delta, Meter, outputUnit), i.e. delta / metersPerOutputUnit).
        double expectedRatio = (1200d / 3937d) / 0.3048d;
        for (int index = 0; index < usSurveyFootPayload.Samples.Count; index++)
        {
            LocalCoordinate usSurveyFoot = usSurveyFootPayload.Samples[index].Position;
            LocalCoordinate internationalFoot = internationalFootPayload.Samples[index].Position;

            AssertWithinRelativeTolerance(expectedRatio, internationalFoot.X / usSurveyFoot.X, 1e-9);
            AssertWithinRelativeTolerance(expectedRatio, internationalFoot.Y / usSurveyFoot.Y, 1e-9);
            AssertWithinRelativeTolerance(expectedRatio, internationalFoot.Elevation / usSurveyFoot.Elevation, 1e-9);
        }

        // Origin and elevationRange are both expressed in SOURCE units (projected metres / NAVD88 metres),
        // never OutputUnit, so choosing a different OutputUnit cannot move either -- proven bit-exactly, not
        // merely with record equality, because record equality alone would not catch a sign-of-zero regression.
        Assert.Equal(usSurveyFootPayload.Provenance.LocalFrame.Origin, internationalFootPayload.Provenance.LocalFrame.Origin);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(usSurveyFootPayload.Provenance.LocalFrame.Origin.X),
            BitConverter.DoubleToInt64Bits(internationalFootPayload.Provenance.LocalFrame.Origin.X));
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(usSurveyFootPayload.Provenance.LocalFrame.Origin.Y),
            BitConverter.DoubleToInt64Bits(internationalFootPayload.Provenance.LocalFrame.Origin.Y));
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(usSurveyFootPayload.Provenance.LocalFrame.Origin.Elevation),
            BitConverter.DoubleToInt64Bits(internationalFootPayload.Provenance.LocalFrame.Origin.Elevation));
        ElevationRange usSurveyFootRange = usSurveyFootPayload.Provenance.ElevationRange!;
        ElevationRange internationalFootRange = internationalFootPayload.Provenance.ElevationRange!;
        Assert.Equal(BitConverter.DoubleToInt64Bits(usSurveyFootRange.Minimum), BitConverter.DoubleToInt64Bits(internationalFootRange.Minimum));
        Assert.Equal(BitConverter.DoubleToInt64Bits(usSurveyFootRange.Maximum), BitConverter.DoubleToInt64Bits(internationalFootRange.Maximum));
        Assert.Equal(usSurveyFootRange.Unit, internationalFootRange.Unit);

        Assert.Equal(LengthUnit.UsSurveyFoot, usSurveyFootPayload.Provenance.LocalFrame.OutputUnit);
        Assert.Equal(LengthUnit.InternationalFoot, internationalFootPayload.Provenance.LocalFrame.OutputUnit);

        TerrainExportBundle usSurveyFootBundle = TerrainExportBundleRenderer.Render(usSurveyFootPayload, "golden-units-us-survey-foot");
        TerrainExportBundle internationalFootBundle = TerrainExportBundleRenderer.Render(internationalFootPayload, "golden-units-international-foot");

        List<string> usSurveyFootUnits = UnitDefinitionUnits(usSurveyFootBundle.DocumentBytes);
        List<string> internationalFootUnits = UnitDefinitionUnits(internationalFootBundle.DocumentBytes);
        Assert.Contains(nameof(LengthUnit.UsSurveyFoot), usSurveyFootUnits);
        Assert.DoesNotContain(nameof(LengthUnit.InternationalFoot), usSurveyFootUnits);
        Assert.Contains(nameof(LengthUnit.InternationalFoot), internationalFootUnits);

        // points.unit is asserted indirectly, but strictly: TerrainExportBundleReader.Read rejects any document
        // whose points.unit disagrees with provenance.localFrame.outputUnit, so successfully reading each
        // bundle below already proves that agreement for both output units.
        TerrainExportPayload usSurveyFootRoundTrip = TerrainExportBundleReader.Read(usSurveyFootBundle.DocumentBytes.Span, usSurveyFootBundle.PointsBytes.Span);
        TerrainExportPayload internationalFootRoundTrip = TerrainExportBundleReader.Read(internationalFootBundle.DocumentBytes.Span, internationalFootBundle.PointsBytes.Span);
        Assert.Equal(LengthUnit.UsSurveyFoot, usSurveyFootRoundTrip.Provenance.LocalFrame.OutputUnit);
        Assert.Equal(LengthUnit.InternationalFoot, internationalFootRoundTrip.Provenance.LocalFrame.OutputUnit);
    }

    [Fact]
    public async Task GoldenPipelineUnderADifferentSnappedOriginStillReproducesTheSameProjectedCoordinatesThroughToSource()
    {
        FixtureContext fixture = LoadFixtureContext();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        TerrainExportPayload defaultRun = await RunGoldenPipelineAsync(
            fixture, LengthUnit.UsSurveyFoot, GoldenOriginCandidate, cancellationToken);
        TerrainExportPayload alternateOriginRun = await RunGoldenPipelineAsync(
            fixture, LengthUnit.UsSurveyFoot, AlternateOriginCandidate, cancellationToken);

        Assert.NotEqual(defaultRun.Provenance.LocalFrame.Origin, alternateOriginRun.Provenance.LocalFrame.Origin);
        Assert.Equal(defaultRun.Samples.Count, alternateOriginRun.Samples.Count);
        Assert.NotEmpty(defaultRun.Samples);

        for (int index = 0; index < defaultRun.Samples.Count; index++)
        {
            Coordinate3D fromDefault = defaultRun.Provenance.LocalFrame.ToSource(defaultRun.Samples[index].Position);
            Coordinate3D fromAlternate = alternateOriginRun.Provenance.LocalFrame.ToSource(alternateOriginRun.Samples[index].Position);

            Assert.Equal(fromDefault, fromAlternate);
            Assert.Equal(BitConverter.DoubleToInt64Bits(fromDefault.X), BitConverter.DoubleToInt64Bits(fromAlternate.X));
            Assert.Equal(BitConverter.DoubleToInt64Bits(fromDefault.Y), BitConverter.DoubleToInt64Bits(fromAlternate.Y));
            Assert.Equal(BitConverter.DoubleToInt64Bits(fromDefault.Elevation), BitConverter.DoubleToInt64Bits(fromAlternate.Elevation));
        }
    }

    [Fact]
    public async Task GoldenDocumentSchemaTokensMatchTheCurrentWriterConstants()
    {
        FixtureContext fixture = LoadFixtureContext();
        TerrainExportPayload payload = await RunGoldenPipelineAsync(
            fixture, LengthUnit.UsSurveyFoot, GoldenOriginCandidate, TestContext.Current.CancellationToken);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, GoldenBaseName);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        Assert.Equal(TerrainExportBundleRenderer.DocumentSchema, document.RootElement.GetProperty("schema").GetString());
        Assert.Equal(TerrainProvenance.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task TheRealParcelPipelineIsDeterministicAcrossTwoRunsRoundTripsAndExcludesTheNodataHoleCellFromTheOriginalPointCount()
    {
        FixtureContext fixture = LoadFixtureContext();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        TerrainExportBundle first = await RunRealParcelPipelineAsync(fixture, cancellationToken);
        TerrainExportBundle second = await RunRealParcelPipelineAsync(fixture, cancellationToken);

        Assert.Equal(first.DocumentBytes.ToArray(), second.DocumentBytes.ToArray());
        Assert.Equal(first.PointsBytes.ToArray(), second.PointsBytes.ToArray());

        TerrainExportPayload roundTripped = TerrainExportBundleReader.Read(first.DocumentBytes.Span, first.PointsBytes.Span);

        // robandee-synthetic.asc has exactly one true NODATA cell (its declared NODATA_value) among the grid's
        // 9 cells; the real-parcel clip region below is buffered generously enough to cover the whole grid, so
        // the candidate set is every OTHER cell -- proving the NODATA hole is excluded regardless of the clip
        // boundary, exactly like the golden pipeline's own region-excluded corner (a different exclusion path)
        // never lets a NODATA cell through either.
        Assert.Equal(8, roundTripped.Provenance.OriginalPointCount);
    }

    // ---- shared pipeline steps 1-3: parse .prj, parse .asc, build the ProjNET transform (definition text only) ----

    private sealed record FixtureContext(
        HorizontalReference ProjectedReference, VerticalReference VerticalReference, ElevationGrid Grid, IHorizontalCoordinateTransform Transform);

    private static FixtureContext LoadFixtureContext()
    {
        string prjWkt = ReadFixture("robandee-synthetic.prj");
        WellKnownTextReference parsedPrj = WellKnownTextReferenceParser.Parse(prjWkt);
        Assert.NotNull(parsedPrj.Vertical);
        VerticalReference verticalReference = parsedPrj.Vertical!;

        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, prjWkt);

        // Reuses the transform's own TargetReference (rather than parsedPrj.Horizontal, a second independent
        // parse of the same text) as the grid's horizontal reference, so TerrainProvenance's "horizontalTransformation.
        // TargetReference must equal localFrame.ProjectedHorizontalReference" invariant, and GridClipper's own
        // "region reference must equal grid reference" check, both hold by construction -- mirroring
        // ProjNetHorizontalCoordinateTransformFactoryTests's own LocalCoordinateFrame fixture, which does the same.
        HorizontalReference projectedReference = transform.Definition.TargetReference;

        using StringReader ascReader = new(ReadFixture("robandee-synthetic.asc"));
        ElevationGrid grid = AaiGridParser.Parse(ascReader, projectedReference, verticalReference);

        return new FixtureContext(projectedReference, verticalReference, grid, transform);
    }

    // ---- golden pipeline steps 4-7 ----

    private static async Task<TerrainExportPayload> RunGoldenPipelineAsync(
        FixtureContext fixture, LengthUnit outputUnit, Coordinate3D originCandidate, CancellationToken cancellationToken)
    {
        PolygonalRegion clipPolygon = BuildGoldenClipPentagon(fixture.ProjectedReference);
        ClipRegion clipRegion = ClipRegion.FromRegion(clipPolygon, LinearDistance.Meters(0d));
        GridClipResult clipResult = GridClipper.Clip(fixture.Grid, clipRegion);

        Coordinate3D origin = LocalOriginSnapping.SnapToWholeSourceUnit(originCandidate, fixture.ProjectedReference, fixture.VerticalReference);
        LocalCoordinateFrame localFrame = new(origin, fixture.ProjectedReference, fixture.VerticalReference, outputUnit);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            clipResult.Grid,
            new SimplificationRequest(pointBudget: 500, SimplificationMethod.CurvatureAware),
            cancellationToken);

        return TerrainExportPayloadAssembler.Assemble(
            GoldenSourceMetadata(), fixture.Transform.Definition, fixture.VerticalReference, localFrame, clipResult.Grid, simplification);
    }

    /// <summary>
    /// An irregular (non-regular) pentagon: a rectangle spanning roughly the grid's middle two-thirds, fractional-
    /// metre on every side so no edge passes through a cell centre (which sit only on whole-plus-one-half metre
    /// coordinates), with its southwest corner cut off by two more fractional-metre points. The uncut northeast
    /// portion still reaches past the synthetic NODATA cell's centre (719349.5, 4286527.5), so the region cuts
    /// across that hole instead of excluding it outright; the cut southwest corner instead excludes the
    /// (719347.5, 4286525.5) cell's centre, so the clip is neither the whole grid nor a trivial single cell.
    /// </summary>
    private static PolygonalRegion BuildGoldenClipPentagon(HorizontalReference reference)
    {
        Geometry geometry = ReadWkt(
            "POLYGON ((719348.11 4286525.19, 719349.79 4286525.19, 719349.79 4286527.87, " +
            "719347.24 4286527.87, 719347.24 4286526.02, 719348.11 4286525.19))");
        return PolygonalRegion.FromGeometry(geometry, reference);
    }

    private static ElevationSourceMetadata GoldenSourceMetadata() => new("OpenTopography", "USGS1m");

    // ---- real parcel pipeline: GeoJSON parcel -> AoiNormalizer -> forward reprojection -> buffered clip -> steps 5-7 ----

    private static async Task<TerrainExportBundle> RunRealParcelPipelineAsync(FixtureContext fixture, CancellationToken cancellationToken)
    {
        string geoJsonText = ReadFixture("robandee-synthetic-parcel.geojson");
        HorizontalReference wgs84 = fixture.Transform.Definition.SourceReference;

        // robandee-synthetic-parcel.geojson and robandee-synthetic.asc are independently authored synthetic
        // fixtures placed in the same neighborhood, not literally overlapping (see Fixtures/README.md), so the
        // buffer is deliberately generous: large enough for the clip to reach the tiny 3x3 grid regardless of
        // the exact gap between the two fixtures' footprints.
        ParcelGeometryAoi aoi = new(ParcelGeometryFormat.GeoJson, geoJsonText, wgs84, LinearDistance.Meters(100d));

        NormalizedAoi normalized = AoiNormalizer.Normalize(aoi);
        Assert.NotNull(normalized.Parcel);
        Assert.True(normalized.Buffer.Value > 0d);

        PolygonalRegion projectedParcel = PolygonalRegionReprojection.Reproject(normalized.Parcel!, fixture.Transform, HorizontalTransformDirection.Forward);
        ClipRegion clipRegion = ClipRegion.FromRegion(projectedParcel, normalized.Buffer);
        GridClipResult clipResult = GridClipper.Clip(fixture.Grid, clipRegion);

        Coordinate3D origin = LocalOriginSnapping.SnapToWholeSourceUnit(GoldenOriginCandidate, fixture.ProjectedReference, fixture.VerticalReference);
        LocalCoordinateFrame localFrame = new(origin, fixture.ProjectedReference, fixture.VerticalReference, LengthUnit.UsSurveyFoot);

        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            clipResult.Grid,
            new SimplificationRequest(pointBudget: 500, SimplificationMethod.CurvatureAware),
            cancellationToken);

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            GoldenSourceMetadata(), fixture.Transform.Definition, fixture.VerticalReference, localFrame, clipResult.Grid, simplification);

        return TerrainExportBundleRenderer.Render(payload, "robandee-real-parcel");
    }

    // ---- shared low-level helpers ----

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static Geometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static List<string> UnitDefinitionUnits(ReadOnlyMemory<byte> documentBytes)
    {
        using JsonDocument document = JsonDocument.Parse(documentBytes);
        return [.. document.RootElement.GetProperty("unitDefinitions").EnumerateArray()
            .Select(item => item.GetProperty("unit").GetString()!)];
    }

    private static void AssertWithinRelativeTolerance(double expected, double actual, double relativeTolerance)
    {
        double relativeError = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            relativeError <= relativeTolerance,
            $"Expected a ratio within {relativeTolerance.ToString("G17", CultureInfo.InvariantCulture)} of " +
            $"{expected.ToString("G17", CultureInfo.InvariantCulture)}, but observed " +
            $"{actual.ToString("G17", CultureInfo.InvariantCulture)} (relative error " +
            $"{relativeError.ToString("G17", CultureInfo.InvariantCulture)}).");
    }

    /// <summary>
    /// Compares <paramref name="actualBytes"/> byte-for-byte against the committed golden file
    /// tests/SolidGround.Tests/Fixtures/<paramref name="fileName"/>. On a missing golden or a mismatch, writes
    /// <paramref name="actualBytes"/> to a sibling ".actual" file (under the test output directory's own copy
    /// of Fixtures, never the committed source tree) and fails with that path plus the first differing byte
    /// offset. Also asserts the committed golden contains no 0x0D byte, guarding against line-ending
    /// normalization on checkout.
    /// </summary>
    private static void AssertMatchesGoldenFile(string fileName, byte[] actualBytes)
    {
        string goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        string actualPath = goldenPath + ".actual";

        if (!File.Exists(goldenPath))
        {
            File.WriteAllBytes(actualPath, actualBytes);
            Assert.Fail(
                $"The committed golden file '{goldenPath}' does not exist. The actual rendered bytes were " +
                $"written to '{actualPath}'; inspect them (no http://, https://, apikey, api_key, " +
                "authorization:, 'bearer ', or 0x0D byte), then copy the file over the committed golden under " +
                "tests/SolidGround.Tests/Fixtures/ and rerun.");
            return;
        }

        byte[] expectedBytes = File.ReadAllBytes(goldenPath);
        Assert.DoesNotContain((byte)0x0D, expectedBytes);

        if (expectedBytes.AsSpan().SequenceEqual(actualBytes))
        {
            return;
        }

        File.WriteAllBytes(actualPath, actualBytes);
        int offset = FirstDifferingOffset(expectedBytes, actualBytes);
        Assert.Fail(
            $"The rendered bytes do not match the committed golden file '{goldenPath}'; first differing byte " +
            $"offset {offset.ToString(CultureInfo.InvariantCulture)} (expected length " +
            $"{expectedBytes.Length.ToString(CultureInfo.InvariantCulture)}, actual length " +
            $"{actualBytes.Length.ToString(CultureInfo.InvariantCulture)}). Expected bytes near the offset: " +
            $"{Excerpt(expectedBytes, offset)}. Actual bytes near the offset: {Excerpt(actualBytes, offset)}. " +
            $"The actual rendered bytes were written to '{actualPath}'; inspect them, then copy the file over " +
            "the committed golden under tests/SolidGround.Tests/Fixtures/ and rerun.");
    }

    private static int FirstDifferingOffset(byte[] expected, byte[] actual)
    {
        int length = Math.Min(expected.Length, actual.Length);
        for (int index = 0; index < length; index++)
        {
            if (expected[index] != actual[index])
            {
                return index;
            }
        }

        return length;
    }

    private static string Excerpt(byte[] bytes, int offset)
    {
        int start = Math.Max(0, offset - 24);
        int end = Math.Min(bytes.Length, offset + 24);
        return Convert.ToHexStringLower(bytes.AsSpan(start, end - start));
    }
}
