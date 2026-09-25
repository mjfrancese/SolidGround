using System.Globalization;
using System.Reflection;
using System.Text.Json;
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
/// Tests for <see cref="ExtensibleStorageProvenanceValues"/> (SolidGround Issue #16): the write path
/// (<see cref="ExtensibleStorageProvenanceValues.From"/>), the read-back compare path
/// (<see cref="ExtensibleStorageProvenanceValues.Diff"/>/<see cref="ExtensibleStorageProvenanceValues.ComputeDeltas"/>),
/// and the source-coordinate reconstruction path. Most tests build a minimal, hand-constructed
/// <see cref="TerrainProvenance"/> directly (mirroring <c>ContractModelTests.CreateProvenance</c>'s own
/// pattern); the two tests marked "graft" in the design record instead run the real
/// <see cref="TerrainProcessingPipeline"/> against the committed <c>example-site-synthetic.*</c> fixture
/// (mirroring <c>TerrainProcessingPipelineTests.LoadFixture</c>'s own pattern), so this type is exercised at
/// least once against a real pipeline output, not only hand-built objects.
/// </summary>
public sealed class ExtensibleStorageProvenanceValuesTests
{
    private const string BuildInformationalVersion = "1.2.3+abcdef";
    private const string BuildModuleVersionId = "11111111-1111-1111-1111-111111111111";
    private const string BuildSha256 = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

    [Fact]
    public async Task FromProducesExpectedValuesForAnExampleSiteLikeProvenance()
    {
        Fixture fixture = LoadFixture();
        TerrainProcessingOutcome outcome = await RunPipelineAsync(fixture, TestContext.Current.CancellationToken);

        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(
            outcome.Payload.Provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        Assert.Equal(ExtensibleStorageProvenanceSchema.CurrentVersion, values.SchemaVersion);
        Assert.Equal("Example-site synthetic fixture", values.SourceDatasetName);
        Assert.Equal("example-site-synthetic", values.SourceDatasetIdentifier);
        Assert.False(values.HasCollectionPeriod);
        Assert.Equal(string.Empty, values.CollectionPeriodStartIso);
        Assert.Equal(string.Empty, values.CollectionPeriodEndIso);
        Assert.False(values.HasQualityLevel);
        Assert.Equal(string.Empty, values.QualityLevel);
        Assert.Equal("NAD83", values.HorizontalDatum);
        Assert.Equal("EPSG:26915", values.HorizontalCrsIdentifier);
        Assert.Equal("Operator", values.SourceHorizontalReferenceOrigin);
        Assert.Equal("NAVD88", values.VerticalDatum);
        Assert.False(values.HasVerticalGeoidModel);
        Assert.Equal(string.Empty, values.VerticalGeoidModel);
        Assert.Equal("Operator", values.SourceVerticalReferenceOrigin);
        Assert.Equal(8, values.OriginalPointCount);
        Assert.Equal(8, values.RetainedPointCount);
        Assert.Equal("CurvatureAware", values.SimplificationMethod);
        Assert.Equal(500, values.SimplificationPointBudget);
        Assert.True(values.HasElevationRange);
        Assert.Equal(183.1d, values.ElevationMinimumMeters, 9);
        Assert.Equal(184.2d, values.ElevationMaximumMeters, 9);
        Assert.Equal("meter", values.OutputUnitToken);
        Assert.Equal(1d, values.MetersPerOutputUnit);
        Assert.Equal(449674d, values.LocalOriginXMeters, 9);
        Assert.Equal(4604563d, values.LocalOriginYMeters, 9);
        Assert.Equal(183d, values.LocalOriginElevationMeters, 9);

        string prjWkt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.prj"));
        Assert.Equal("WKT1", values.HorizontalForwardOperationFormat);
        Assert.Equal(prjWkt, values.HorizontalForwardOperationDefinition);
        Assert.Equal("WKT1", values.HorizontalInverseOperationFormat);
        // The inverse leg's definition text is the geographic source WKT (Wgs84WellKnownText, distinct from
        // prjWkt above), so this catches a forward/inverse property swap that the shared "WKT1" format
        // string alone cannot distinguish (round 1 finding: HorizontalInverseOperationDefinition had zero
        // value coverage anywhere in this suite).
        Assert.Equal(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, values.HorizontalInverseOperationDefinition);
        Assert.Equal("ProjNET", values.HorizontalTransformEngineName);
        Assert.Equal("2.1.0", values.HorizontalTransformEngineVersion);
        Assert.Equal(BuildInformationalVersion, values.BuildInformationalVersion);
        Assert.Equal(BuildModuleVersionId, values.BuildModuleVersionId);
        Assert.Equal(BuildSha256, values.BuildSha256);
    }

    [Fact]
    public void FromThrowsWhenAWithExpressionSmugglesANonFiniteOrigin()
    {
        // A record `with`-expression only ever invokes the compiler-generated copy constructor -- it never
        // re-runs a hand-written validating constructor's own guard clauses. Every existing Core provenance
        // record (including Coordinate3D, LocalCoordinateFrame, and TerrainProvenance itself) is deliberately
        // immune to this because their properties are plain get-only auto-properties with no init accessor,
        // so `origin with { X = double.NaN }` fails to compile against them (verified directly: CS0200,
        // "cannot be assigned to -- it is read only"). Reflection therefore stands in for a with-expression
        // here, to reach exactly the same "already-valid object, now carrying a smuggled non-finite value"
        // state From's own defensive guard exists to catch.
        TerrainProvenance provenance = CreateProvenance();
        TerrainProvenance smuggledProvenance = SmuggleNonFiniteOriginComponent(provenance, "<X>k__BackingField");

        ProvenanceFieldValueException exception = Assert.Throws<ProvenanceFieldValueException>(
            () => ExtensibleStorageProvenanceValues.From(smuggledProvenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256));
        Assert.Contains("localOriginXMeters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromThrowsWhenAWithExpressionSmugglesANonFiniteOriginY()
    {
        // Sibling of FromThrowsWhenAWithExpressionSmugglesANonFiniteOrigin (round 1 finding: only the
        // localOriginXMeters RequireFinite call site was ever exercised; a swapped field-name literal on any
        // of the other four call sites -- for example RequireFinite(elevationMaximumMeters,
        // "elevationMinimumMeters") -- would compile and pass every prior test).
        TerrainProvenance provenance = CreateProvenance();
        TerrainProvenance smuggledProvenance = SmuggleNonFiniteOriginComponent(provenance, "<Y>k__BackingField");

        ProvenanceFieldValueException exception = Assert.Throws<ProvenanceFieldValueException>(
            () => ExtensibleStorageProvenanceValues.From(smuggledProvenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256));
        Assert.Contains("localOriginYMeters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromThrowsWhenAWithExpressionSmugglesANonFiniteOriginElevation()
    {
        TerrainProvenance provenance = CreateProvenance();
        TerrainProvenance smuggledProvenance = SmuggleNonFiniteOriginComponent(provenance, "<Elevation>k__BackingField");

        ProvenanceFieldValueException exception = Assert.Throws<ProvenanceFieldValueException>(
            () => ExtensibleStorageProvenanceValues.From(smuggledProvenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256));
        Assert.Contains("localOriginElevationMeters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromThrowsWhenAnElevationRangeMinimumIsSmuggledNonFinite()
    {
        // ElevationRange also hand-validates in its constructor and exposes only get-only auto-properties, so
        // it needs the same reflection-backed smuggle as Coordinate3D above -- here directly against the
        // reference-type instance already inside provenance.ElevationRange, with no boxing dance needed.
        TerrainProvenance provenance = CreateProvenance();
        SmuggleNonFiniteElevationRangeBound(provenance, "<Minimum>k__BackingField");

        ProvenanceFieldValueException exception = Assert.Throws<ProvenanceFieldValueException>(
            () => ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256));
        Assert.Contains("elevationMinimumMeters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromThrowsWhenAnElevationRangeMaximumIsSmuggledNonFinite()
    {
        TerrainProvenance provenance = CreateProvenance();
        SmuggleNonFiniteElevationRangeBound(provenance, "<Maximum>k__BackingField");

        ProvenanceFieldValueException exception = Assert.Throws<ProvenanceFieldValueException>(
            () => ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256));
        Assert.Contains("elevationMaximumMeters", exception.Message, StringComparison.Ordinal);
    }

    private static TerrainProvenance SmuggleNonFiniteOriginComponent(TerrainProvenance provenance, string backingFieldName)
    {
        FieldInfo backingField = typeof(Coordinate3D).GetField(backingFieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        object boxedOrigin = provenance.LocalFrame.Origin;
        backingField.SetValue(boxedOrigin, double.NaN);
        Coordinate3D smuggledOrigin = (Coordinate3D)boxedOrigin;

        LocalCoordinateFrame smuggledFrame = new(
            smuggledOrigin, provenance.LocalFrame.ProjectedHorizontalReference, provenance.LocalFrame.VerticalReference, provenance.LocalFrame.OutputUnit);
        return new TerrainProvenance(
            provenance.SchemaVersion,
            provenance.Source,
            provenance.HorizontalTransformation,
            provenance.SourceVerticalReference,
            provenance.SourceHorizontalReferenceOrigin,
            provenance.SourceVerticalReferenceOrigin,
            smuggledFrame,
            provenance.SimplificationRequest,
            provenance.OriginalPointCount,
            provenance.RetainedPointCount,
            provenance.ElevationRange);
    }

    private static void SmuggleNonFiniteElevationRangeBound(TerrainProvenance provenance, string backingFieldName)
    {
        FieldInfo backingField = typeof(ElevationRange).GetField(backingFieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        backingField.SetValue(provenance.ElevationRange, double.NaN);
    }

    [Fact]
    public void FromRepresentsAbsentCollectionPeriodQualityLevelAndGeoidModelExplicitly()
    {
        TerrainProvenance provenance = CreateProvenance(collectionPeriod: null, qualityLevel: null, geoidModel: null);

        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        Assert.False(values.HasCollectionPeriod);
        Assert.Equal(string.Empty, values.CollectionPeriodStartIso);
        Assert.Equal(string.Empty, values.CollectionPeriodEndIso);
        Assert.False(values.HasQualityLevel);
        Assert.Equal(string.Empty, values.QualityLevel);
        Assert.False(values.HasVerticalGeoidModel);
        Assert.Equal(string.Empty, values.VerticalGeoidModel);
    }

    [Fact]
    public void FromRepresentsPresentCollectionPeriodQualityLevelAndGeoidModelExplicitly()
    {
        CollectionPeriod period = new(new DateOnly(2017, 2, 17), new DateOnly(2017, 2, 27));
        TerrainProvenance provenance = CreateProvenance(collectionPeriod: period, qualityLevel: "QL2", geoidModel: "Geoid12B");

        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        Assert.True(values.HasCollectionPeriod);
        Assert.Equal("2017-02-17", values.CollectionPeriodStartIso);
        Assert.Equal("2017-02-27", values.CollectionPeriodEndIso);
        Assert.True(values.HasQualityLevel);
        Assert.Equal("QL2", values.QualityLevel);
        Assert.True(values.HasVerticalGeoidModel);
        Assert.Equal("Geoid12B", values.VerticalGeoidModel);
    }

    [Fact]
    public void CollectionPeriodDatesAreCultureInvariant()
    {
        CollectionPeriod period = new(new DateOnly(2017, 2, 17), new DateOnly(2017, 2, 27));
        TerrainProvenance provenance = CreateProvenance(collectionPeriod: period);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

            Assert.Equal("2017-02-17", values.CollectionPeriodStartIso);
            Assert.Equal("2017-02-27", values.CollectionPeriodEndIso);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void OutputUnitTokenMatchesThePlacementRecordsOwnTokenForEveryLengthUnit()
    {
        foreach (LengthUnit unit in Enum.GetValues<LengthUnit>())
        {
            // Independently reproduces CreateToposolidCommand's own former LengthUnitToken body (the
            // placement record's oracle), so this test would fail if LengthUnitTokens.SettingsToken ever
            // drifted from TerrainRequestSettings.JsonOptions's camelCase enum convention.
            string placementRecordToken = JsonSerializer.Serialize(unit, TerrainRequestSettings.JsonOptions).Trim('"');

            Assert.Equal(placementRecordToken, LengthUnitTokens.SettingsToken(unit));
        }

        Assert.Equal("meter", LengthUnitTokens.SettingsToken(LengthUnit.Meter));
        Assert.Equal("usSurveyFoot", LengthUnitTokens.SettingsToken(LengthUnit.UsSurveyFoot));
        Assert.Equal("internationalFoot", LengthUnitTokens.SettingsToken(LengthUnit.InternationalFoot));
    }

    [Fact]
    public void HorizontalDatumIsTheProjectedTargetDatumNotTheSourceGeographicDatum()
    {
        TerrainProvenance provenance = CreateProvenance();

        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        Assert.Equal(provenance.HorizontalTransformation.TargetReference.Datum, values.HorizontalDatum);
        Assert.NotEqual(provenance.HorizontalTransformation.SourceReference.Datum, values.HorizontalDatum);
        Assert.NotEqual(provenance.SourceHorizontalReference.Datum, values.HorizontalDatum);
    }

    [Fact]
    public void DiffReportsMismatchesAtAndJustPastTheRoundTripTolerance()
    {
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        double toleranceMeters = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();

        ExtensibleStorageProvenanceValues withinTolerance = expected with { ElevationMinimumMeters = expected.ElevationMinimumMeters + (toleranceMeters * 0.5d) };
        ExtensibleStorageProvenanceValues atTolerance = expected with { ElevationMinimumMeters = expected.ElevationMinimumMeters + toleranceMeters };
        ExtensibleStorageProvenanceValues pastTolerance = expected with { ElevationMinimumMeters = expected.ElevationMinimumMeters + (toleranceMeters * 2d) };

        Assert.Empty(ExtensibleStorageProvenanceValues.Diff(expected, withinTolerance));
        // CompareLength's boundary is inclusive (Math.Abs(expected - actual) <= toleranceMeters), so a delta
        // exactly at the tolerance must still pass -- distinct from the 0.5x case above, which would not
        // catch an accidental tightening of <= to < (round 1 finding: this test's own name promised an "at"
        // case that did not exist).
        Assert.Empty(ExtensibleStorageProvenanceValues.Diff(expected, atTolerance));

        IReadOnlyList<string> mismatches = ExtensibleStorageProvenanceValues.Diff(expected, pastTolerance);
        string mismatch = Assert.Single(mismatches);
        Assert.Contains("elevationMinimumMeters", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void DiffDetectsMismatchesFromStringIntAndBoolComparators()
    {
        // DiffReportsMismatchesAtAndJustPastTheRoundTripTolerance above exercises CompareLength (including,
        // since 2026-09-23, for metersPerOutputUnit -- see MetersPerOutputUnitIsToleranceBoundedNotExact
        // below); every other field goes through CompareString, CompareInt, or CompareBool, none of which was
        // ever driven with unequal inputs anywhere in this suite before this test (round 1 finding: a broken
        // comparator for any of the other 30 such fields -- an inverted condition, a copy-pasted wrong
        // property, or a bad field-name literal -- would previously go undetected). One case per comparator
        // kind is enough to exercise each helper's "found a mismatch" branch and its field-name literal; it is
        // not a per-field oracle for every one of the 30 call sites, an explicitly accepted, narrower scope.
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        ExtensibleStorageProvenanceValues stringMismatch = expected with { SourceDatasetName = "wrong" };
        ExtensibleStorageProvenanceValues intMismatch = expected with { SchemaVersion = expected.SchemaVersion + 1 };
        ExtensibleStorageProvenanceValues boolMismatch = expected with { HasCollectionPeriod = !expected.HasCollectionPeriod };

        AssertSingleMismatchNames(ExtensibleStorageProvenanceValues.Diff(expected, stringMismatch), "sourceDatasetName");
        AssertSingleMismatchNames(ExtensibleStorageProvenanceValues.Diff(expected, intMismatch), "schemaVersion");
        AssertSingleMismatchNames(ExtensibleStorageProvenanceValues.Diff(expected, boolMismatch), "hasCollectionPeriod");
    }

    [Fact]
    public void MetersPerOutputUnitIsToleranceBoundedNotExact()
    {
        // Round 1 review finding (2026-09-23): metersPerOutputUnit was compared bit-for-bit
        // (CompareExactDouble) until this same fix gave the field a real Extensible Storage spec
        // (SpecTypeId.Number), which broke the "no spec, so no internal-unit round trip to absorb" premise
        // the bit-exact comparison relied on. This locks the replacement behavior: a delta within
        // ExtensibleStorageRoundTripTolerance must not be flagged (so a harmless internal-unit round trip
        // cannot roll back a valid toposolid creation), but a delta safely past it still must be.
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        double toleranceMeters = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();

        ExtensibleStorageProvenanceValues withinTolerance = expected with { MetersPerOutputUnit = expected.MetersPerOutputUnit + (toleranceMeters * 0.5d) };
        ExtensibleStorageProvenanceValues pastTolerance = expected with { MetersPerOutputUnit = expected.MetersPerOutputUnit + (toleranceMeters * 2d) };

        Assert.Empty(ExtensibleStorageProvenanceValues.Diff(expected, withinTolerance));
        IReadOnlyList<string> mismatches = ExtensibleStorageProvenanceValues.Diff(expected, pastTolerance);
        AssertSingleMismatchNames(mismatches, "metersPerOutputUnit");

        // Round 2 review finding: CompareLength's shared mismatch message always appended a literal " m"
        // (meters) suffix to the tolerance value, which is misleading for metersPerOutputUnit -- a unitless
        // ratio, not a length. Locks the field-specific label instead of the generic one.
        string mismatch = Assert.Single(mismatches);
        Assert.Contains("unitless ratio", mismatch, StringComparison.Ordinal);
        Assert.DoesNotContain(" m).", mismatch, StringComparison.Ordinal);
    }

    private static void AssertSingleMismatchNames(IReadOnlyList<string> mismatches, string field)
    {
        string mismatch = Assert.Single(mismatches);
        Assert.Contains(field, mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void DiffAndComputeDeltasMessagesAreCultureInvariant()
    {
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        ExtensibleStorageProvenanceValues actual = expected with { ElevationMinimumMeters = expected.ElevationMinimumMeters + 12345.6789d };

        IReadOnlyList<string> invariantDiff = ExtensibleStorageProvenanceValues.Diff(expected, actual);
        IReadOnlyList<(string Field, double Delta)> invariantDeltas = ExtensibleStorageProvenanceValues.ComputeDeltas(expected, actual);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            IReadOnlyList<string> deDeDiff = ExtensibleStorageProvenanceValues.Diff(expected, actual);
            IReadOnlyList<(string Field, double Delta)> deDeDeltas = ExtensibleStorageProvenanceValues.ComputeDeltas(expected, actual);

            Assert.Equal(invariantDiff, deDeDiff);
            string mismatch = Assert.Single(deDeDiff);
            Assert.Contains(actual.ElevationMinimumMeters.ToString("R", CultureInfo.InvariantCulture), mismatch, StringComparison.Ordinal);
            Assert.Equal(invariantDeltas, deDeDeltas);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ComputeDeltasReportsEveryToleranceBoundedFieldRegardlessOfTolerance()
    {
        // Round 2 review finding (2026-09-23): ComputeDeltas was left reporting only the five Length-spec
        // fields' deltas even though the round 1 fix moved metersPerOutputUnit into the same tolerance-bounded
        // comparison class (Diff now runs it through CompareLength too). Renamed from
        // ComputeDeltasReportsEveryLengthFieldRegardlessOfTolerance because metersPerOutputUnit is not itself a
        // Length-spec field (schema table row 24), matching this repo's own precedent for renaming a test whose
        // name no longer described its full coverage (see DiffDetectsMismatchesFromStringIntAndBoolComparators).
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        ExtensibleStorageProvenanceValues actual = expected with
        {
            ElevationMinimumMeters = expected.ElevationMinimumMeters + 1d,
            ElevationMaximumMeters = expected.ElevationMaximumMeters + 2d,
            LocalOriginXMeters = expected.LocalOriginXMeters + 3d,
            LocalOriginYMeters = expected.LocalOriginYMeters + 4d,
            LocalOriginElevationMeters = expected.LocalOriginElevationMeters + 5d,
            MetersPerOutputUnit = expected.MetersPerOutputUnit + 6d,
        };

        IReadOnlyList<(string Field, double Delta)> deltas = ExtensibleStorageProvenanceValues.ComputeDeltas(expected, actual);

        Assert.Equal(6, deltas.Count);
        Assert.Equal(1d, DeltaFor(deltas, "elevationMinimumMeters"), 9);
        Assert.Equal(2d, DeltaFor(deltas, "elevationMaximumMeters"), 9);
        Assert.Equal(6d, DeltaFor(deltas, "metersPerOutputUnit"), 9);
        Assert.Equal(3d, DeltaFor(deltas, "localOriginXMeters"), 9);
        Assert.Equal(4d, DeltaFor(deltas, "localOriginYMeters"), 9);
        Assert.Equal(5d, DeltaFor(deltas, "localOriginElevationMeters"), 9);
    }

    [Fact]
    public async Task ReconstructSourceCoordinateMatchesLocalCoordinateFrameToSourceForARetainedExampleSiteSample()
    {
        Fixture fixture = LoadFixture();
        TerrainProcessingOutcome outcome = await RunPipelineAsync(fixture, TestContext.Current.CancellationToken);
        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(
            outcome.Payload.Provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        LocalCoordinate sampleLocal = outcome.Payload.Samples[0].Position;

        Coordinate3D reconstructed = ExtensibleStorageProvenanceValues.ReconstructSourceCoordinate(values, sampleLocal);
        Coordinate3D expected = ExtensibleStorageProvenanceValues.ToSourceMeters(outcome.Payload.Provenance.LocalFrame, sampleLocal);

        // The example-site fixture's horizontal, vertical, and output units are all meters (MetersPerUnit == 1
        // exactly for every factor involved), so both computation paths reduce to the same exact arithmetic
        // with zero rounding difference.
        Assert.Equal(expected, reconstructed);
        Assert.Equal(outcome.Payload.Provenance.LocalFrame.ToSource(sampleLocal), reconstructed);
    }

    [Fact]
    public void ReconstructSourceCoordinateMatchesToSourceMetersForSyntheticNonMeterUnits()
    {
        TerrainProvenance provenance = CreateProvenance(
            horizontalUnit: LengthUnit.UsSurveyFoot,
            verticalUnit: LengthUnit.InternationalFoot,
            outputUnit: LengthUnit.UsSurveyFoot,
            origin: new Coordinate3D(2_360_000d, 14_060_000d, 1_970d),
            elevationRange: new ElevationRange(1_950d, 2_000d, LengthUnit.InternationalFoot));
        ExtensibleStorageProvenanceValues values = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);
        LocalCoordinate local = new(125.5d, -40.25d, 12.75d);
        double toleranceMeters = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();

        Coordinate3D reconstructed = ExtensibleStorageProvenanceValues.ReconstructSourceCoordinate(values, local);
        Coordinate3D expected = ExtensibleStorageProvenanceValues.ToSourceMeters(provenance.LocalFrame, local);

        Assert.True(Math.Abs(expected.X - reconstructed.X) <= toleranceMeters, $"X delta {expected.X - reconstructed.X} exceeded tolerance.");
        Assert.True(Math.Abs(expected.Y - reconstructed.Y) <= toleranceMeters, $"Y delta {expected.Y - reconstructed.Y} exceeded tolerance.");
        Assert.True(Math.Abs(expected.Elevation - reconstructed.Elevation) <= toleranceMeters, $"Elevation delta {expected.Elevation - reconstructed.Elevation} exceeded tolerance.");

        // Round 2 finding: ReconstructSourceCoordinate never reads ElevationMinimumMeters/ElevationMaximumMeters
        // (only LocalOriginXMeters/YMeters/ElevationMeters and MetersPerOutputUnit), so the assertions above
        // leave From's elevationMinimumMeters/elevationMaximumMeters conversion (design record §2, field table
        // rows 21-22) completely unchecked whenever the vertical conversion factor isn't 1.0. This provenance's
        // ElevationRange is in InternationalFoot (non-1 factor), so these two assertions -- against an
        // independently computed expected value, the same way the fixture-anchored test above uses a real
        // UsSurveyFoot/InternationalFoot factor for the other three Length-spec fields -- would fail if that
        // multiplication were ever dropped or swapped for the wrong per-axis factor.
        double expectedElevationMinimumMeters = provenance.ElevationRange!.Minimum * LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot);
        double expectedElevationMaximumMeters = provenance.ElevationRange!.Maximum * LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot);
        Assert.Equal(expectedElevationMinimumMeters, values.ElevationMinimumMeters, 9);
        Assert.Equal(expectedElevationMaximumMeters, values.ElevationMaximumMeters, 9);
    }

    [Fact]
    public void RawConstructorMatchesFromForAnEquivalentInstance()
    {
        TerrainProvenance provenance = CreateProvenance();
        ExtensibleStorageProvenanceValues fromValues = ExtensibleStorageProvenanceValues.From(provenance, BuildInformationalVersion, BuildModuleVersionId, BuildSha256);

        // Mirrors SolidGround.Revit.Provenance.ProvenanceEntityWriter's own read-back path (Stage 2): 36
        // individually named arguments, no reflective loop, no call back into From.
        ExtensibleStorageProvenanceValues rawValues = new(
            fromValues.SchemaVersion,
            fromValues.SourceDatasetName,
            fromValues.SourceDatasetIdentifier,
            fromValues.HasCollectionPeriod,
            fromValues.CollectionPeriodStartIso,
            fromValues.CollectionPeriodEndIso,
            fromValues.HasQualityLevel,
            fromValues.QualityLevel,
            fromValues.HorizontalDatum,
            fromValues.HorizontalCrsIdentifier,
            fromValues.SourceHorizontalReferenceOrigin,
            fromValues.VerticalDatum,
            fromValues.HasVerticalGeoidModel,
            fromValues.VerticalGeoidModel,
            fromValues.SourceVerticalReferenceOrigin,
            fromValues.OriginalPointCount,
            fromValues.RetainedPointCount,
            fromValues.SimplificationMethod,
            fromValues.SimplificationPointBudget,
            fromValues.HasElevationRange,
            fromValues.ElevationMinimumMeters,
            fromValues.ElevationMaximumMeters,
            fromValues.OutputUnitToken,
            fromValues.MetersPerOutputUnit,
            fromValues.LocalOriginXMeters,
            fromValues.LocalOriginYMeters,
            fromValues.LocalOriginElevationMeters,
            fromValues.HorizontalForwardOperationFormat,
            fromValues.HorizontalForwardOperationDefinition,
            fromValues.HorizontalInverseOperationFormat,
            fromValues.HorizontalInverseOperationDefinition,
            fromValues.HorizontalTransformEngineName,
            fromValues.HorizontalTransformEngineVersion,
            fromValues.BuildInformationalVersion,
            fromValues.BuildModuleVersionId,
            fromValues.BuildSha256);

        Assert.Empty(ExtensibleStorageProvenanceValues.Diff(fromValues, rawValues));
    }

    private static double DeltaFor(IReadOnlyList<(string Field, double Delta)> deltas, string field) =>
        deltas.Single(delta => string.Equals(delta.Field, field, StringComparison.Ordinal)).Delta;

    private static TerrainProvenance CreateProvenance(
        int originalPointCount = 8,
        int retainedPointCount = 8,
        CollectionPeriod? collectionPeriod = null,
        string? qualityLevel = null,
        string? geoidModel = null,
        LengthUnit horizontalUnit = LengthUnit.Meter,
        LengthUnit verticalUnit = LengthUnit.Meter,
        LengthUnit outputUnit = LengthUnit.Meter,
        Coordinate3D? origin = null,
        ElevationRange? elevationRange = null)
    {
        HorizontalReference geographic = new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(horizontalUnit), HorizontalAxisOrder.EastingNorthing);
        VerticalReference vertical = new("NAVD88", verticalUnit, geoidModel);
        HorizontalTransformationDefinition transformation = new(
            geographic,
            projected,
            new CoordinateOperationDefinition("WKT1", "forward-definition"),
            new CoordinateOperationDefinition("WKT1", "inverse-definition"),
            "ProjNET",
            "2.1.0");
        LocalCoordinateFrame frame = new(origin ?? new Coordinate3D(449674d, 4604563d, 183d), projected, vertical, outputUnit);

        return new TerrainProvenance(
            1,
            new ElevationSourceMetadata("Example-site synthetic fixture", "example-site-synthetic", collectionPeriod, qualityLevel),
            transformation,
            vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            frame,
            new SimplificationRequest(500, SimplificationMethod.CurvatureAware),
            originalPointCount,
            retainedPointCount,
            elevationRange ?? new ElevationRange(183.1d, 184.2d, verticalUnit));
    }

    private sealed record Fixture(HorizontalReference ProjectedReference, VerticalReference VerticalReference, ElevationGrid Grid, IHorizontalCoordinateTransform Transform);

    private static Task<TerrainProcessingOutcome> RunPipelineAsync(Fixture fixture, CancellationToken cancellationToken)
    {
        LocalOriginRequest origin = new(LocalOriginKind.Explicit, 449674d, 4604563d, 183d);

        return TerrainProcessingPipeline.RunAsync(
            fixture.Grid, fixture.Transform, fixture.VerticalReference,
            new ReferenceOrigins(ReferenceOrigin.Operator, ReferenceOrigin.Operator),
            new ElevationSourceMetadata("Example-site synthetic fixture", "example-site-synthetic"),
            aoi: null, origin, LengthUnit.Meter, SimplificationMethod.CurvatureAware, 500, 0.2d, cancellationToken);
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
}
