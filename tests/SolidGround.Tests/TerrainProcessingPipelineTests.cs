using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Rasters;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="TerrainProcessingPipeline"/>, lifted into <c>SolidGround.Core</c> for
/// SolidGround Issue #15 with its body unchanged; no dedicated test existed for this type at any visibility
/// level before this move, only indirect coverage via the CLI's own `process`/`run` command tests. See
/// docs/architecture/cli-workflow.md's "Commands" section: "share one internal processing pipeline... so
/// their clipping, local-origin, unit, and simplification behavior can never drift apart from each other".
/// </summary>
public sealed class TerrainProcessingPipelineTests
{
    private const int GenerousBudget = 500;
    private const double DefaultCoverageFloor = 0.2d;

    // example-site-synthetic.asc: a 3x3, 1 m cellsize grid with exactly one NODATA cell (top-right in the file, the
    // grid's northeast corner), so 8 of its 9 cells are valid -- see TerrainExportGoldenFileTests's own use of
    // the identical fixture for the same count.
    private const int WholeGridValidCellCount = 8;

    private sealed record Fixture(HorizontalReference ProjectedReference, VerticalReference VerticalReference, ElevationGrid Grid, IHorizontalCoordinateTransform Transform);

    [Fact]
    public async Task RunAsyncWithNoAreaOfInterestProcessesTheWholeGrid()
    {
        Fixture fixture = LoadFixture();

        TerrainProcessingOutcome outcome = await RunPipelineAsync(fixture, aoi: null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken);

        Assert.Null(outcome.ClipResult);
        Assert.Equal(WholeGridValidCellCount, outcome.Payload.Provenance.OriginalPointCount);
    }

    [Fact]
    public async Task RunAsyncWithABoundingBoxAreaOfInterestClipsBeforeSimplifying()
    {
        Fixture fixture = LoadFixture();

        // Covers only the grid's west two columns (UTM X in [[withheld], [withheld]), Y in [[withheld], [withheld]]): column
        // 2's cell centers sit at X=[withheld], half a metre past this box's east edge, so they -- including the
        // one NODATA cell, itself in column 2 -- are excluded before the grid even reaches GridClipper's own
        // NODATA accounting. 6 of the remaining 6 cells (both retained columns, all three rows) are valid.
        Coordinate2D sw = fixture.Transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Coordinate2D ne = fixture.Transform.Inverse(new Coordinate2D([withheld], [withheld]));
        Wgs84BoundingBoxAoi bbox = new(sw.X, sw.Y, ne.X, ne.Y);

        TerrainProcessingOutcome outcome = await RunPipelineAsync(fixture, bbox, LocalSouthwestOrigin(), TestContext.Current.CancellationToken);

        Assert.NotNull(outcome.ClipResult);
        Assert.Equal(6, outcome.Payload.Provenance.OriginalPointCount);
        Assert.True(outcome.Payload.Provenance.OriginalPointCount < WholeGridValidCellCount);
    }

    [Fact]
    public async Task RunAsyncAppliesTheConfiguredLocalOriginToEverySample()
    {
        Fixture fixture = LoadFixture();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        LocalOriginRequest originA = new(LocalOriginKind.Explicit, [withheld], [withheld], 183d);
        LocalOriginRequest originB = new(LocalOriginKind.Explicit, [withheld], [withheld], 180d);

        TerrainProcessingOutcome outcomeA = await RunPipelineAsync(fixture, aoi: null, originA, cancellationToken);
        TerrainProcessingOutcome outcomeB = await RunPipelineAsync(fixture, aoi: null, originB, cancellationToken);

        Assert.Equal(outcomeA.Payload.Samples.Count, outcomeB.Payload.Samples.Count);
        Assert.NotEmpty(outcomeA.Payload.Samples);

        for (int index = 0; index < outcomeA.Payload.Samples.Count; index++)
        {
            LocalCoordinate a = outcomeA.Payload.Samples[index].Position;
            LocalCoordinate b = outcomeB.Payload.Samples[index].Position;

            // local = source - origin, in meters (the fixture's own output unit here): shifting every sample
            // by the exact same origin delta, for every retained sample, is what "applies the configured
            // local origin to every sample" means.
            Assert.Equal(originB.X - originA.X, a.X - b.X, 9);
            Assert.Equal(originB.Y - originA.Y, a.Y - b.Y, 9);
            Assert.Equal(originB.Z - originA.Z, a.Elevation - b.Elevation, 9);
        }
    }

    [Fact]
    public async Task RunAsyncReturnsSimplificationDiagnosticsFromTheUnderlyingSimplifier()
    {
        Fixture fixture = LoadFixture();

        TerrainProcessingOutcome outcome = await RunPipelineAsync(fixture, aoi: null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken);

        Assert.NotNull(outcome.SimplificationDiagnostics);
        Assert.Equal(WholeGridValidCellCount, outcome.SimplificationDiagnostics!.CandidatePointCount);
        Assert.Equal(outcome.Payload.Provenance.OriginalPointCount, outcome.SimplificationDiagnostics.CandidatePointCount);
    }

    private static LocalOriginRequest LocalSouthwestOrigin() => new(LocalOriginKind.Southwest, 0, 0, 0);

    private static Task<TerrainProcessingOutcome> RunPipelineAsync(
        Fixture fixture, AreaOfInterest? aoi, LocalOriginRequest origin, CancellationToken cancellationToken) =>
        TerrainProcessingPipeline.RunAsync(
            fixture.Grid, fixture.Transform, fixture.VerticalReference,
            new ReferenceOrigins(ReferenceOrigin.Operator, ReferenceOrigin.Operator),
            new ElevationSourceMetadata("ExampleSite synthetic fixture", "example-site-synthetic"),
            aoi, origin, LengthUnit.Meter, SimplificationMethod.CurvatureAware, GenerousBudget, DefaultCoverageFloor, cancellationToken);

    private static Fixture LoadFixture()
    {
        string prjWkt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.prj"));
        WellKnownTextReference parsedPrj = WellKnownTextReferenceParser.Parse(prjWkt);
        Assert.NotNull(parsedPrj.Vertical);
        VerticalReference verticalReference = parsedPrj.Vertical!;

        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, prjWkt);
        HorizontalReference projectedReference = transform.Definition.TargetReference;

        using StringReader ascReader = new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.asc")));
        ElevationGrid grid = AaiGridParser.Parse(ascReader, projectedReference, verticalReference);

        return new Fixture(projectedReference, verticalReference, grid, transform);
    }
}
