using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class TerrainExportPayloadAssemblerTests
{
    [Fact]
    public async Task OriginalPointCountEqualsTheCandidateGridsValidCellCount()
    {
        double?[,] elevations =
        {
            { 10d, 11d, 12d },
            { 13d, null, 15d },
            { 16d, 17d, 18d },
        };
        ElevationGrid grid = Grid(elevations);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification);

        Assert.Equal(8, payload.Provenance.OriginalPointCount);
    }

    [Fact]
    public async Task NoDataCellsAreExcludedFromTheAssembledSamplesCountsAndElevationRange()
    {
        double?[,] elevations =
        {
            { 100d, null, 300d },
            { null, 50d, null },
            { 400d, null, 25d },
        };
        ElevationGrid grid = Grid(elevations);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);
        LocalCoordinateFrame localFrame = LocalFrame();

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), localFrame, grid, simplification);

        Assert.Equal(5, payload.Provenance.OriginalPointCount);
        Assert.Equal(5, payload.Samples.Count);
        Assert.Equal(25d, payload.Provenance.ElevationRange!.Minimum);
        Assert.Equal(400d, payload.Provenance.ElevationRange.Maximum);

        Coordinate2D[] holeCenters =
        [
            grid.GetCellCenter(0, 1), grid.GetCellCenter(1, 0), grid.GetCellCenter(1, 2), grid.GetCellCenter(2, 1),
        ];
        foreach (LocalTerrainSample sample in payload.Samples)
        {
            Coordinate3D source = localFrame.ToSource(sample.Position);
            Assert.DoesNotContain(holeCenters, hole => hole.X == source.X && hole.Y == source.Y);
        }
    }

    [Fact]
    public async Task AnEmptyCandidateSetThrowsTerrainProvenanceException()
    {
        double?[,] elevations = { { null, null }, { null, null } };
        ElevationGrid grid = Grid(elevations);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains("no valid elevation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EachAssembledSampleIsTheLocalFrameProjectionOfItsRetainedSourcePositionAndReversesExactly()
    {
        double?[,] elevations =
        {
            { 12.5d, 13.25d },
            { 14.125d, 15.0625d },
        };
        ElevationGrid grid = Grid(elevations);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);
        LocalCoordinateFrame localFrame = LocalFrame(origin: new Coordinate3D(0.5d, 0.5d, 1d), outputUnit: LengthUnit.UsSurveyFoot);

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), localFrame, grid, simplification);

        Assert.Equal(simplification.RetainedSamples.Count, payload.Samples.Count);
        for (int index = 0; index < payload.Samples.Count; index++)
        {
            Coordinate3D retainedPosition = simplification.RetainedSamples[index].Position;
            Assert.Equal(localFrame.ToLocal(retainedPosition), payload.Samples[index].Position);
            Assert.Equal(retainedPosition, localFrame.ToSource(payload.Samples[index].Position));
        }
    }

    [Fact]
    public async Task RetainedSampleOrderIsPreservedAndIsYDescendingThenXAscendingInLocalCoordinatesForAGridDerivedResult()
    {
        ElevationGrid grid = Grid(SequentialElevations(4, 5));
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);
        LocalCoordinateFrame localFrame = LocalFrame();

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), localFrame, grid, simplification);

        List<LocalCoordinate> expected = [.. simplification.RetainedSamples.Select(sample => localFrame.ToLocal(sample.Position))];
        List<LocalCoordinate> actual = [.. payload.Samples.Select(sample => sample.Position)];
        Assert.Equal(expected, actual);

        for (int index = 1; index < actual.Count; index++)
        {
            LocalCoordinate previous = actual[index - 1];
            LocalCoordinate current = actual[index];
            Assert.True(
                previous.Y > current.Y || (previous.Y == current.Y && previous.X < current.X),
                $"Sample {index} is not ordered Y descending then X ascending relative to sample {index - 1}.");
        }
    }

    [Fact]
    public void DiagnosticsCandidatePointCountMismatchThrowsNamingTheField()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2)); // 4 genuinely valid cells.
        SimplificationResult simplification = HandBuiltResult(
            originalPointCount: 5, retainedPointCount: 1, candidatePointCount: 5,
            minElevation: 0d, maxElevation: 3d, elevationUnit: LengthUnit.Meter);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains(nameof(SimplificationDiagnostics.CandidatePointCount), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsMinElevationMismatchThrowsNamingTheField()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2)); // valid elevations 0, 1, 2, 3: real minimum is 0.
        SimplificationResult simplification = HandBuiltResult(
            originalPointCount: 4, retainedPointCount: 1, candidatePointCount: 4,
            minElevation: -50d, maxElevation: 3d, elevationUnit: LengthUnit.Meter);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains(nameof(SimplificationDiagnostics.MinElevation), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsMaxElevationMismatchThrowsNamingTheField()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2)); // valid elevations 0, 1, 2, 3: real maximum is 3.
        SimplificationResult simplification = HandBuiltResult(
            originalPointCount: 4, retainedPointCount: 1, candidatePointCount: 4,
            minElevation: 0d, maxElevation: 999d, elevationUnit: LengthUnit.Meter);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains(nameof(SimplificationDiagnostics.MaxElevation), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsElevationUnitMismatchThrowsNamingTheField()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2), verticalUnit: LengthUnit.Meter);
        SimplificationResult simplification = HandBuiltResult(
            originalPointCount: 4, retainedPointCount: 1, candidatePointCount: 4,
            minElevation: 0d, maxElevation: 3d, elevationUnit: LengthUnit.InternationalFoot);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains(nameof(SimplificationDiagnostics.ElevationUnit), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssembleSucceedsWhenSimplificationDiagnosticsIsOmitted()
    {
        // SimplificationResult's diagnostics parameter defaults to null: any ITerrainSimplifier may omit it.
        // Assemble must still succeed, computing OriginalPointCount/ElevationRange straight from the grid,
        // rather than skipping a check it should still run or throwing on the null diagnostics.
        ElevationGrid grid = Grid(SequentialElevations(2, 2)); // valid elevations 0, 1, 2, 3.
        SimplificationResult simplification = new(
            1, [new TerrainSample(new Coordinate3D(0.5d, 0.5d, 0d))], new SimplificationRequest(1));

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification);

        Assert.Null(simplification.Diagnostics);
        Assert.Equal(4, payload.Provenance.OriginalPointCount);
        Assert.Equal(0d, payload.Provenance.ElevationRange!.Minimum);
        Assert.Equal(3d, payload.Provenance.ElevationRange.Maximum);
    }

    [Fact]
    public async Task ElevationUnitMismatchBetweenTheGridAndTheSourceVerticalReferenceThrows()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2), verticalUnit: LengthUnit.Meter);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(
                Source(), Transformation(), VerticalReference(LengthUnit.UsSurveyFoot), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains(nameof(LengthUnit.Meter), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LengthUnit.UsSurveyFoot), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HorizontalReferenceMismatchBetweenTheGridAndTheLocalFrameThrows()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2), horizontal: AlternateProjectedReference());
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));

        Assert.Contains("horizontal reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerticalReferenceMismatchBetweenTheGridAndTheSourceVerticalReferenceThrowsEvenWhenUnitsAgree()
    {
        // Same Unit (Meter) on both sides, so the elevationRange.Unit check above cannot see this: only Datum differs.
        ElevationGrid grid = Grid(SequentialElevations(2, 2), verticalUnit: LengthUnit.Meter);
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);
        VerticalReference differentDatum = new("NGVD29", LengthUnit.Meter, "Geoid12B");

        TerrainProvenanceException exception = Assert.Throws<TerrainProvenanceException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), differentDatum, Origins(), LocalFrame(), grid, simplification));

        Assert.Contains("vertical reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SchemaVersionIsAlwaysTheCurrentSchemaVersion()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2));
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification);

        Assert.Equal(TerrainProvenance.CurrentSchemaVersion, payload.Provenance.SchemaVersion);
    }

    [Fact]
    public async Task AssembleCarriesANonNullAddressParcelIntoTheResultingProvenanceUnchanged()
    {
        ElevationGrid grid = Grid(SequentialElevations(2, 2));
        GridTerrainSimplifier simplifier = new();
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            grid, new SimplificationRequest(pointBudget: 1000), TestContext.Current.CancellationToken);
        AddressParcelProvenance addressParcel = new(
            new DateOnly(2026, 9, 21),
            new GeocodeProvenance(
                AddressGeocoderProvider.Census,
                "100 Example Loop",
                "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau."),
            null);

        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification, addressParcel);

        Assert.Equal(addressParcel, payload.Provenance.AddressParcel);
    }

    [Fact]
    public void AssembleRejectsNullArguments()
    {
        ElevationGrid grid = Grid(SequentialElevations(1, 1));
        SimplificationResult simplification = new(1, [new TerrainSample(new Coordinate3D(0.5d, 0.5d, 0d))], new SimplificationRequest(1));

        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(null!, Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), null!, VerticalReference(), Origins(), LocalFrame(), grid, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), null!, Origins(), LocalFrame(), grid, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), null!, LocalFrame(), grid, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), null!, grid, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), null!, simplification));
        Assert.Throws<ArgumentNullException>(() =>
            TerrainExportPayloadAssembler.Assemble(Source(), Transformation(), VerticalReference(), Origins(), LocalFrame(), grid, null!));
    }

    private static SimplificationResult HandBuiltResult(
        int originalPointCount,
        int retainedPointCount,
        int candidatePointCount,
        double? minElevation,
        double? maxElevation,
        LengthUnit elevationUnit)
    {
        TerrainSample[] retained =
            [.. Enumerable.Range(0, retainedPointCount).Select(index => new TerrainSample(new Coordinate3D(index + 1d, 1d, 1d)))];
        SimplificationDiagnostics diagnostics = new(
            candidatePointCount, 0, retainedPointCount, retainedPointCount, 0, 0, 0, true,
            minElevation, maxElevation, elevationUnit, 0d, 0d, 0d, 0d);

        return new SimplificationResult(
            originalPointCount, retained, new SimplificationRequest(pointBudget: Math.Max(retainedPointCount, 1)), diagnostics);
    }

    private static double?[,] SequentialElevations(int rowCount, int columnCount)
    {
        double?[,] values = new double?[rowCount, columnCount];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                values[row, column] = (row * columnCount) + column;
            }
        }

        return values;
    }

    private static ElevationGrid Grid(double?[,] elevations, LengthUnit verticalUnit = LengthUnit.Meter, HorizontalReference? horizontal = null) =>
        new(
            horizontal ?? ProjectedReference(), VerticalReference(verticalUnit), new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, elevations);

    private static LocalCoordinateFrame LocalFrame(
        VerticalReference? vertical = null, LengthUnit outputUnit = LengthUnit.UsSurveyFoot, Coordinate3D? origin = null) =>
        new(origin ?? new Coordinate3D(0d, 0d, 0d), ProjectedReference(), vertical ?? VerticalReference(), outputUnit);

    private static ElevationSourceMetadata Source() => new("OpenTopography", "USGS1m");

    private static ReferenceOrigins Origins() => new(ReferenceOrigin.Operator, ReferenceOrigin.Operator);

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static HorizontalReference AlternateProjectedReference() => new(
        "EPSG:26916", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.Meter) => new("NAVD88", unit, "Geoid12B");

    private static HorizontalTransformationDefinition Transformation() => new(
        GeographicReference(),
        ProjectedReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "candidate-engine",
        "1.0");
}
