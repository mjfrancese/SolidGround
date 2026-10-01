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
using NetTopologySuite.Geometries;

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

        // Covers only the grid's west two columns (UTM X in [449674, 449676), Y in [4604563, 4604566]): column
        // 2's cell centers sit at X=449676.5, half a metre past this box's east edge, so they -- including the
        // one NODATA cell, itself in column 2 -- are excluded before the grid even reaches GridClipper's own
        // NODATA accounting. 6 of the remaining 6 cells (both retained columns, all three rows) are valid.
        Coordinate2D sw = fixture.Transform.Inverse(new Coordinate2D(449674d, 4604563d));
        Coordinate2D ne = fixture.Transform.Inverse(new Coordinate2D(449676d, 4604566d));
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

        LocalOriginRequest originA = new(LocalOriginKind.Explicit, 449674d, 4604563d, 183d);
        LocalOriginRequest originB = new(LocalOriginKind.Explicit, 449667d, 4604558d, 180d);

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

    [Fact]
    public async Task RunAsyncThreadsAddressParcelProvenanceIntoTheAssembledPayloadWhenSupplied()
    {
        Fixture fixture = LoadFixture();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        AddressParcelProvenance addressParcel = new(
            new DateOnly(2026, 9, 27),
            new GeocodeProvenance(
                AddressGeocoderProvider.Census,
                "100 Example Loop",
                "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau."),
            parcel: null);

        TerrainProcessingOutcome withAddressParcel = await RunPipelineAsync(
            fixture, aoi: null, LocalSouthwestOrigin(), cancellationToken, addressParcel);

        Assert.Same(addressParcel, withAddressParcel.Payload.Provenance.AddressParcel);

        // Omitting the new argument must keep relying on RunAsync's own null default -- the exact default every
        // existing process/run/Revit call site already relies on. (A byte-for-byte comparison between an
        // omitted-argument call and an explicit-addressParcel:-null call would be tautological: C# compiles an
        // omitted optional argument to the same literal default, so the two calls are argument-for-argument
        // identical before either reaches RunAsync, and such a comparison could never fail regardless of
        // correctness. The genuine "output stays byte-identical to before this change" claim is independently
        // anchored by this file's own four Facts above, which call RunAsync -- via RunPipelineAsync -- without
        // the new argument and keep their pre-existing assertions unchanged, and by
        // TerrainExportGoldenFileTests's committed golden-file comparison, which calls
        // TerrainExportPayloadAssembler.Assemble directly with no addressParcel argument.)
        TerrainProcessingOutcome omittedArgument = await RunPipelineAsync(fixture, aoi: null, LocalSouthwestOrigin(), cancellationToken);
        Assert.Null(omittedArgument.Payload.Provenance.AddressParcel);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentKeepsTheLegalParcelAsTheOriginAndBoundaryWhenTerrainHasAMargin()
    {
        Fixture fixture = LoadFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (449675d, 4604564d), (449676d, 4604564d), (449676d, 4604565d), (449675d, 4604565d));
        ParcelExtentGeometry parcelExtent = new(legalParcel, LinearDistance.Meters(0.25d));

        TerrainProcessingOutcome outcome = await RunPipelineAsync(
            fixture, aoi: null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken, parcelExtentGeometry: parcelExtent);

        TerrainExtentPlan plan = Assert.IsType<TerrainExtentPlan>(outcome.TerrainExtentPlan);
        Assert.Equal(new Coordinate3D(449675d, 4604564d, 0d), outcome.Payload.Provenance.LocalFrame.Origin);
        Assert.Equal(new LocalCoordinate2D(0d, 0d), Assert.Single(plan.LegalLocalBoundary.Polygons).Shell.Vertices[0]);
        Assert.Equal(parcelExtent.FetchEnvelope, plan.FetchEnvelope);
        Assert.True(plan.TerrainClipRegion.Envelope.MinX < plan.LegalParcelRegion.Envelope.MinX);
        Assert.True(plan.TerrainClipRegion.Envelope.MinY < plan.LegalParcelRegion.Envelope.MinY);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentKeepsActualLegalCoordinatesAndPlaneZStableAcrossMarginAndBudget()
    {
        Fixture fixture = LoadFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (449675d, 4604564d), (449676d, 4604564d), (449676d, 4604565d), (449675d, 4604565d));

        TerrainProcessingOutcome zeroMargin = await RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Zero));
        TerrainProcessingOutcome marginWithFixedBudget = await RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            pointBudget: 1,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Meters(0.25d)));

        TerrainExtentPlan zeroPlan = Assert.IsType<TerrainExtentPlan>(zeroMargin.TerrainExtentPlan);
        TerrainExtentPlan marginPlan = Assert.IsType<TerrainExtentPlan>(marginWithFixedBudget.TerrainExtentPlan);
        Assert.Equal(zeroMargin.Payload.Provenance.LocalFrame.Origin, marginWithFixedBudget.Payload.Provenance.LocalFrame.Origin);
        Assert.Equal(zeroPlan.LegalPlaneZ, marginPlan.LegalPlaneZ);
        Assert.Equal(
            Assert.Single(zeroPlan.LegalLocalBoundary.Polygons).Shell.Vertices,
            Assert.Single(marginPlan.LegalLocalBoundary.Polygons).Shell.Vertices);

        LocalCoordinate2D legalLocalVertex = Assert.Single(marginPlan.LegalLocalBoundary.Polygons).Shell.Vertices[0];
        Coordinate3D restored = marginWithFixedBudget.Payload.Provenance.LocalFrame.ToSource(
            new LocalCoordinate(legalLocalVertex.X, legalLocalVertex.Y, marginPlan.LegalPlaneZ));
        Assert.Equal(449675d, restored.X, 9);
        Assert.Equal(4604564d, restored.Y, 9);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentRejectsAnUncoveredPositiveTerrainMargin()
    {
        Fixture fixture = LoadFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (449674d, 4604563d), (449675d, 4604563d), (449675d, 4604564d), (449674d, 4604564d));

        ParcelExtentPlanningException error = await Assert.ThrowsAsync<ParcelExtentPlanningException>(() => RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Meters(0.25d))));

        Assert.Contains("not fully covered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentRejectsAPartiallyUncoveredLegalParcelAtZeroMargin()
    {
        Fixture fixture = LoadFixture();
        // The east-side cell center remains valid, so the old guard (which applied only when the margin was
        // positive) accepted this clipped legal parcel despite its western half lying beyond the source grid.
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (449673.5d, 4604563d), (449674.5d, 4604563d), (449674.5d, 4604564d), (449673.5d, 4604564d));

        ParcelExtentPlanningException error = await Assert.ThrowsAsync<ParcelExtentPlanningException>(() => RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Zero)));

        Assert.Contains("not fully covered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentRejectsWhenEveryLegalSampleIsNoData()
    {
        Fixture fixture = LoadFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (449676d, 4604565d), (449677d, 4604565d), (449677d, 4604566d), (449676d, 4604566d));

        ParcelExtentPlanningException error = await Assert.ThrowsAsync<ParcelExtentPlanningException>(() => RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Zero)));

        Assert.Contains("no valid elevation samples", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentReportsLegalMultipartHoleAndNoDataWarningsAsTypedCounts()
    {
        Fixture fixture = LoadFixture();
        PolygonalRegion legalParcel = MultipartRegionWithHole(fixture.ProjectedReference);

        TerrainProcessingOutcome outcome = await RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Zero));

        TerrainExtentPlan plan = Assert.IsType<TerrainExtentPlan>(outcome.TerrainExtentPlan);
        Assert.Contains(plan.Warnings, warning => warning == new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelHasMultiplePolygons, 2));
        Assert.Contains(plan.Warnings, warning => warning == new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelHasHoles, 1));
        Assert.Contains(plan.Warnings, warning => warning == new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelContainsNoData, 1));
        Assert.Contains(plan.Warnings, warning => warning == new TerrainExtentWarning(TerrainExtentWarningKind.TerrainExtentContainsNoData, 1));
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentReportsATerrainOnlyTopologyChangeWhenBufferClosesALegalHole()
    {
        Fixture fixture = LoadFixture();
        GeometryFactory factory = new();
        Polygon legalGeometry = factory.CreatePolygon(
            factory.CreateLinearRing(
            [
                new Coordinate(449674.5d, 4604563.5d), new Coordinate(449676.5d, 4604563.5d), new Coordinate(449676.5d, 4604565.5d),
                new Coordinate(449674.5d, 4604565.5d), new Coordinate(449674.5d, 4604563.5d),
            ]),
            [factory.CreateLinearRing(
            [
                new Coordinate(449675.4d, 4604564.4d), new Coordinate(449675.6d, 4604564.4d), new Coordinate(449675.6d, 4604564.6d),
                new Coordinate(449675.4d, 4604564.6d), new Coordinate(449675.4d, 4604564.4d),
            ])]);
        PolygonalRegion legalParcel = PolygonalRegion.FromGeometry(legalGeometry, fixture.ProjectedReference);

        TerrainProcessingOutcome outcome = await RunPipelineAsync(
            fixture, null, LocalSouthwestOrigin(), TestContext.Current.CancellationToken,
            parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Meters(0.2d)));

        TerrainExtentPlan plan = Assert.IsType<TerrainExtentPlan>(outcome.TerrainExtentPlan);
        Assert.Equal(1, plan.LegalParcelRegion.HoleCount);
        Assert.Equal(0, plan.TerrainClipRegion.HoleCount);
        Assert.Contains(plan.Warnings, warning => warning == new TerrainExtentWarning(TerrainExtentWarningKind.TerrainMarginChangesTopology, 1));
    }

    [Fact]
    public async Task RunAsyncWithParcelExtentKeepsLegalWorldCoordinatesAndPlaneAcrossOriginMarginAndBudgetMatrix()
    {
        Fixture fixture = BuildSyntheticEdgeFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (8d, 8d), (12d, 8d), (12d, 12d), (8d, 12d));
        Coordinate2D[] expectedLegalVertices =
        [
            new Coordinate2D(8d, 8d), new Coordinate2D(12d, 8d), new Coordinate2D(12d, 12d), new Coordinate2D(8d, 12d),
        ];
        (string Name, LocalOriginRequest Request, Coordinate3D ExpectedOrigin)[] origins =
        [
            ("southwest", new LocalOriginRequest(LocalOriginKind.Southwest, 0d, 0d, 0d), new Coordinate3D(8d, 8d, 0d)),
            ("centroid", new LocalOriginRequest(LocalOriginKind.Centroid, 0d, 0d, 0d), new Coordinate3D(10d, 10d, 0d)),
            ("explicit", new LocalOriginRequest(LocalOriginKind.Explicit, 8.25d, 8.75d, 150d), new Coordinate3D(8.25d, 8.75d, 150d)),
        ];

        foreach ((string _, LocalOriginRequest origin, Coordinate3D expectedOrigin) in origins)
        {
            double? expectedLocalPlaneZ = null;
            foreach (double marginCells in new[] { 0d, 1d, 3d, 6d })
            {
                foreach (int budget in new[] { 1000, 8 })
                {
                    TerrainProcessingOutcome outcome = await RunPipelineAsync(
                        fixture, null, origin, TestContext.Current.CancellationToken,
                        pointBudget: budget,
                        parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Meters(marginCells)));
                    TerrainExtentPlan plan = Assert.IsType<TerrainExtentPlan>(outcome.TerrainExtentPlan);
                    LocalCoordinateFrame frame = outcome.Payload.Provenance.LocalFrame;

                    expectedLocalPlaneZ ??= 168d - expectedOrigin.Elevation;
                    Assert.Equal(expectedOrigin, frame.Origin);
                    Assert.Equal(expectedLocalPlaneZ.Value, plan.LegalPlaneZ);
                    Assert.Equal(
                        origin.Kind == LocalOriginKind.Explicit ? 18d : 168d,
                        plan.LegalPlaneZ);
                    Assert.True(outcome.Payload.Samples.Count <= budget);

                    IReadOnlyList<LocalCoordinate2D> localVertices = Assert.Single(plan.LegalLocalBoundary.Polygons).Shell.Vertices;
                    Assert.Equal(expectedLegalVertices.Length, localVertices.Count);
                    for (int index = 0; index < localVertices.Count; index++)
                    {
                        Coordinate3D restored = frame.ToSource(new LocalCoordinate(
                            localVertices[index].X, localVertices[index].Y, plan.LegalPlaneZ));
                        Assert.Equal(expectedLegalVertices[index].X, restored.X, 9);
                        Assert.Equal(expectedLegalVertices[index].Y, restored.Y, 9);
                        Assert.Equal(168d, restored.Elevation, 9);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task RunAsyncWithParcelAreaCentroidKeepsTheLegalFrameReversibleAcrossTerrainExtensions()
    {
        Fixture fixture = BuildSyntheticEdgeFixture();
        PolygonalRegion legalParcel = Region(fixture.ProjectedReference,
            (8d, 8d), (14d, 8d), (14d, 10d), (10d, 10d), (10d, 14d), (8d, 14d));
        LocalOriginRequest origin = new(LocalOriginKind.AreaCentroid, 0d, 0d, 0d);

        foreach (double margin in new[] { 0d, 1d })
        {
            TerrainProcessingOutcome outcome = await RunPipelineAsync(
                fixture, null, origin, TestContext.Current.CancellationToken,
                pointBudget: 8,
                parcelExtentGeometry: new ParcelExtentGeometry(legalParcel, LinearDistance.Meters(margin)));
            TerrainExtentPlan plan = Assert.IsType<TerrainExtentPlan>(outcome.TerrainExtentPlan);
            LocalCoordinateFrame frame = outcome.Payload.Provenance.LocalFrame;

            Assert.Equal(new Coordinate3D(10.2d, 10.2d, 0d), frame.Origin);
            Assert.Equal(168d, plan.LegalPlaneZ);
            LocalCoordinate2D firstLocalVertex = Assert.Single(plan.LegalLocalBoundary.Polygons).Shell.Vertices[0];
            Assert.Equal(-2.2d, firstLocalVertex.X, 9);
            Assert.Equal(-2.2d, firstLocalVertex.Y, 9);

            Coordinate3D restored = frame.ToSource(new LocalCoordinate(-2.2d, -2.2d, plan.LegalPlaneZ));
            Assert.Equal(8d, restored.X, 9);
            Assert.Equal(8d, restored.Y, 9);
            Assert.Equal(168d, restored.Elevation, 9);
        }
    }

    private static LocalOriginRequest LocalSouthwestOrigin() => new(LocalOriginKind.Southwest, 0, 0, 0);

    private static Task<TerrainProcessingOutcome> RunPipelineAsync(
        Fixture fixture, AreaOfInterest? aoi, LocalOriginRequest origin, CancellationToken cancellationToken,
        AddressParcelProvenance? addressParcel = null,
        ParcelExtentGeometry? parcelExtentGeometry = null,
        int pointBudget = GenerousBudget) =>
        TerrainProcessingPipeline.RunAsync(
            fixture.Grid, fixture.Transform, fixture.VerticalReference,
            new ReferenceOrigins(ReferenceOrigin.Operator, ReferenceOrigin.Operator),
            new ElevationSourceMetadata("Example-site synthetic fixture", "example-site-synthetic"),
            aoi, origin, LengthUnit.Meter, SimplificationMethod.CurvatureAware, pointBudget, DefaultCoverageFloor, cancellationToken,
            addressParcel, parcelExtentGeometry);

    private static PolygonalRegion Region(HorizontalReference reference, params (double X, double Y)[] vertices)
    {
        Coordinate[] coordinates = new Coordinate[vertices.Length + 1];
        for (int index = 0; index < vertices.Length; index++)
        {
            coordinates[index] = new Coordinate(vertices[index].X, vertices[index].Y);
        }

        coordinates[^1] = coordinates[0];
        return PolygonalRegion.FromGeometry(new GeometryFactory().CreatePolygon(coordinates), reference);
    }

    private static PolygonalRegion MultipartRegionWithHole(HorizontalReference reference)
    {
        GeometryFactory factory = new();
        LinearRing shell = factory.CreateLinearRing(
        [
            new Coordinate(449674d, 4604563d), new Coordinate(449676d, 4604563d), new Coordinate(449676d, 4604566d),
            new Coordinate(449674d, 4604566d), new Coordinate(449674d, 4604563d),
        ]);
        LinearRing hole = factory.CreateLinearRing(
        [
            new Coordinate(449674.1d, 4604563.1d), new Coordinate(449674.2d, 4604563.1d), new Coordinate(449674.2d, 4604563.2d),
            new Coordinate(449674.1d, 4604563.2d), new Coordinate(449674.1d, 4604563.1d),
        ]);
        Polygon first = factory.CreatePolygon(shell, [hole]);
        Polygon second = factory.CreatePolygon(
        [
            new Coordinate(449676.1d, 4604565.1d), new Coordinate(449676.9d, 4604565.1d), new Coordinate(449676.9d, 4604565.9d),
            new Coordinate(449676.1d, 4604565.9d), new Coordinate(449676.1d, 4604565.1d),
        ]);
        return PolygonalRegion.FromGeometry(factory.CreateMultiPolygon([first, second]), reference);
    }

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

    private static Fixture BuildSyntheticEdgeFixture()
    {
        HorizontalReference reference = new(
            "Synthetic projected metres", "Synthetic datum", HorizontalReferenceKind.Projected,
            HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        VerticalReference verticalReference = new("Synthetic vertical", LengthUnit.Meter);
        double?[,] elevations = new double?[20, 20];
        for (int row = 0; row < elevations.GetLength(0); row++)
        {
            for (int column = 0; column < elevations.GetLength(1); column++)
            {
                elevations[row, column] = (row * 20d) + column;
            }
        }

        ElevationGrid grid = new(
            reference, verticalReference, new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.SouthToNorth, elevations);
        return new Fixture(reference, verticalReference, grid, new IdentityTransform(reference));
    }

    private sealed class IdentityTransform : IHorizontalCoordinateTransform
    {
        public IdentityTransform(HorizontalReference reference)
        {
            Definition = new HorizontalTransformationDefinition(
                reference,
                reference,
                new CoordinateOperationDefinition("synthetic", "identity"),
                new CoordinateOperationDefinition("synthetic", "identity"),
                "Synthetic", "1");
        }

        public HorizontalTransformationDefinition Definition { get; }
        public Coordinate2D Forward(Coordinate2D source) => source;
        public Coordinate2D Inverse(Coordinate2D target) => target;
    }
}
