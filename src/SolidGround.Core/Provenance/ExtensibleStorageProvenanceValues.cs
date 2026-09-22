using System.Globalization;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>
/// The 36 values <see cref="ExtensibleStorageProvenanceSchema"/>'s fields hold for one created toposolid, in
/// the schema's own canonical field order (SolidGround Issue #16 design record §2). This is a plain data
/// record with no cross-field validation of its own -- unlike <see cref="TerrainProvenance"/>, its primary
/// constructor is deliberately "plain": <c>SolidGround.Revit.Provenance.ProvenanceEntityWriter</c> (Stage 2)
/// calls it directly, once per read-back, after 36 individual <c>Entity.Get&lt;T&gt;</c> calls, with no
/// opportunity to re-run <see cref="From"/>'s own derivation logic. Because this constructor does not
/// validate, and because a record `with`-expression only ever invokes the compiler-generated copy
/// constructor -- never a factory method's own guard clauses -- an already-valid instance can still be
/// turned into one carrying a non-finite Length field after the fact (for example <c>values with { ElevationMinimumMeters
/// = double.NaN }</c>, which compiles and succeeds precisely because this type's properties are plain
/// <see langword="init"/> properties, not the hand-validated get-only properties every other Core provenance
/// record uses). <see cref="From"/> is therefore the only real guard: it defensively re-checks every
/// Length-spec field's finiteness itself, immediately before returning, rather than trusting that whatever
/// produced its inputs already did so.
/// </summary>
public sealed record ExtensibleStorageProvenanceValues(
    int SchemaVersion,
    string SourceDatasetName,
    string SourceDatasetIdentifier,
    bool HasCollectionPeriod,
    string CollectionPeriodStartIso,
    string CollectionPeriodEndIso,
    bool HasQualityLevel,
    string QualityLevel,
    string HorizontalDatum,
    string HorizontalCrsIdentifier,
    string SourceHorizontalReferenceOrigin,
    string VerticalDatum,
    bool HasVerticalGeoidModel,
    string VerticalGeoidModel,
    string SourceVerticalReferenceOrigin,
    int OriginalPointCount,
    int RetainedPointCount,
    string SimplificationMethod,
    int SimplificationPointBudget,
    bool HasElevationRange,
    double ElevationMinimumMeters,
    double ElevationMaximumMeters,
    string OutputUnitToken,
    double MetersPerOutputUnit,
    double LocalOriginXMeters,
    double LocalOriginYMeters,
    double LocalOriginElevationMeters,
    string HorizontalForwardOperationFormat,
    string HorizontalForwardOperationDefinition,
    string HorizontalInverseOperationFormat,
    string HorizontalInverseOperationDefinition,
    string HorizontalTransformEngineName,
    string HorizontalTransformEngineVersion,
    string BuildInformationalVersion,
    string BuildModuleVersionId,
    string BuildSha256)
{
    /// <summary>
    /// Computes every field from <paramref name="provenance"/> plus the calling add-in's own build identity
    /// (SolidGround Issue #16 design record §2's "write path"): unit conversion (every Length field is
    /// converted once, in Core, to meters, using the correct per-axis source factor -- see
    /// <see cref="ToSourceMeters"/> for why), ISO-8601 date formatting, has-flag computation for the three
    /// optional source values, and a defensive <see cref="double.IsFinite(double)"/> guard on every one of
    /// the five Length-spec fields.
    /// </summary>
    /// <param name="provenance">The provenance to derive Extensible Storage field values from.</param>
    /// <param name="buildInformationalVersion">The calling <c>SolidGround.Revit</c> build's informational version.</param>
    /// <param name="buildModuleVersionId">The calling <c>SolidGround.Revit</c> build's module version ID (MVID).</param>
    /// <param name="buildSha256">The calling <c>SolidGround.Revit</c> assembly file's SHA-256 hash.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ProvenanceFieldValueException">A computed Length-spec field is not a finite number.</exception>
    public static ExtensibleStorageProvenanceValues From(
        TerrainProvenance provenance,
        string buildInformationalVersion,
        string buildModuleVersionId,
        string buildSha256)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(buildInformationalVersion);
        ArgumentNullException.ThrowIfNull(buildModuleVersionId);
        ArgumentNullException.ThrowIfNull(buildSha256);

        ElevationSourceMetadata sourceMetadata = provenance.Source;
        LocalCoordinateFrame localFrame = provenance.LocalFrame;
        VerticalReference verticalReference = provenance.SourceVerticalReference;
        HorizontalReference projectedReference = localFrame.ProjectedHorizontalReference;
        HorizontalTransformationDefinition transformation = provenance.HorizontalTransformation;

        double horizontalMetersPerUnit = LengthConverter.MetersPerUnit(projectedReference.Unit.LinearUnit!.Value);
        double verticalMetersPerUnit = LengthConverter.MetersPerUnit(verticalReference.Unit);

        bool hasCollectionPeriod = sourceMetadata.CollectionPeriod is not null;
        string collectionPeriodStartIso = hasCollectionPeriod
            ? sourceMetadata.CollectionPeriod!.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
        string collectionPeriodEndIso = hasCollectionPeriod
            ? sourceMetadata.CollectionPeriod!.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
        bool hasQualityLevel = sourceMetadata.QualityLevel is not null;
        string qualityLevel = hasQualityLevel ? sourceMetadata.QualityLevel! : string.Empty;

        bool hasElevationRange = provenance.ElevationRange is not null;
        double elevationMinimumMeters = hasElevationRange ? provenance.ElevationRange!.Minimum * verticalMetersPerUnit : 0d;
        double elevationMaximumMeters = hasElevationRange ? provenance.ElevationRange!.Maximum * verticalMetersPerUnit : 0d;
        double localOriginXMeters = localFrame.Origin.X * horizontalMetersPerUnit;
        double localOriginYMeters = localFrame.Origin.Y * horizontalMetersPerUnit;
        double localOriginElevationMeters = localFrame.Origin.Elevation * verticalMetersPerUnit;

        RequireFinite(elevationMinimumMeters, "elevationMinimumMeters");
        RequireFinite(elevationMaximumMeters, "elevationMaximumMeters");
        RequireFinite(localOriginXMeters, "localOriginXMeters");
        RequireFinite(localOriginYMeters, "localOriginYMeters");
        RequireFinite(localOriginElevationMeters, "localOriginElevationMeters");

        return new ExtensibleStorageProvenanceValues(
            SchemaVersion: ExtensibleStorageProvenanceSchema.CurrentVersion,
            SourceDatasetName: sourceMetadata.SourceName,
            SourceDatasetIdentifier: sourceMetadata.DatasetIdentifier,
            HasCollectionPeriod: hasCollectionPeriod,
            CollectionPeriodStartIso: collectionPeriodStartIso,
            CollectionPeriodEndIso: collectionPeriodEndIso,
            HasQualityLevel: hasQualityLevel,
            QualityLevel: qualityLevel,
            HorizontalDatum: transformation.TargetReference.Datum,
            HorizontalCrsIdentifier: transformation.TargetReference.CoordinateReferenceSystem,
            SourceHorizontalReferenceOrigin: provenance.SourceHorizontalReferenceOrigin.ToString(),
            VerticalDatum: verticalReference.Datum,
            HasVerticalGeoidModel: verticalReference.GeoidModel is not null,
            VerticalGeoidModel: verticalReference.GeoidModel ?? string.Empty,
            SourceVerticalReferenceOrigin: provenance.SourceVerticalReferenceOrigin.ToString(),
            OriginalPointCount: provenance.OriginalPointCount,
            RetainedPointCount: provenance.RetainedPointCount,
            SimplificationMethod: provenance.SimplificationRequest.Method.ToString(),
            SimplificationPointBudget: provenance.SimplificationRequest.PointBudget,
            HasElevationRange: hasElevationRange,
            ElevationMinimumMeters: elevationMinimumMeters,
            ElevationMaximumMeters: elevationMaximumMeters,
            OutputUnitToken: LengthUnitTokens.SettingsToken(localFrame.OutputUnit),
            MetersPerOutputUnit: LengthConverter.MetersPerUnit(localFrame.OutputUnit),
            LocalOriginXMeters: localOriginXMeters,
            LocalOriginYMeters: localOriginYMeters,
            LocalOriginElevationMeters: localOriginElevationMeters,
            HorizontalForwardOperationFormat: transformation.ForwardOperation.Format,
            HorizontalForwardOperationDefinition: transformation.ForwardOperation.Definition,
            HorizontalInverseOperationFormat: transformation.InverseOperation.Format,
            HorizontalInverseOperationDefinition: transformation.InverseOperation.Definition,
            HorizontalTransformEngineName: transformation.EngineName,
            HorizontalTransformEngineVersion: transformation.EngineVersion,
            BuildInformationalVersion: buildInformationalVersion,
            BuildModuleVersionId: buildModuleVersionId,
            BuildSha256: buildSha256);
    }

    /// <summary>
    /// Compares every field of <paramref name="expected"/> against <paramref name="actual"/>, never
    /// short-circuiting: string/int/bool fields must match exactly (ordinal for strings), the five
    /// Length-spec fields must match within <see cref="ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance"/>,
    /// and <see cref="MetersPerOutputUnit"/> must match bit-for-bit (it is a fixed ratio, never a measured
    /// length). Every numeric value in a returned message is formatted with <see cref="CultureInfo.InvariantCulture"/>,
    /// matching <c>CreateToposolidCommand.cs</c>'s own <c>"R"</c>-format convention.
    /// </summary>
    /// <returns>One message per mismatched field, in canonical field order; empty when every field matches.</returns>
    public static IReadOnlyList<string> Diff(ExtensibleStorageProvenanceValues expected, ExtensibleStorageProvenanceValues actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        double toleranceMeters = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();
        List<string> mismatches = [];

        CompareInt(mismatches, "schemaVersion", expected.SchemaVersion, actual.SchemaVersion);
        CompareString(mismatches, "sourceDatasetName", expected.SourceDatasetName, actual.SourceDatasetName);
        CompareString(mismatches, "sourceDatasetIdentifier", expected.SourceDatasetIdentifier, actual.SourceDatasetIdentifier);
        CompareBool(mismatches, "hasCollectionPeriod", expected.HasCollectionPeriod, actual.HasCollectionPeriod);
        CompareString(mismatches, "collectionPeriodStartIso", expected.CollectionPeriodStartIso, actual.CollectionPeriodStartIso);
        CompareString(mismatches, "collectionPeriodEndIso", expected.CollectionPeriodEndIso, actual.CollectionPeriodEndIso);
        CompareBool(mismatches, "hasQualityLevel", expected.HasQualityLevel, actual.HasQualityLevel);
        CompareString(mismatches, "qualityLevel", expected.QualityLevel, actual.QualityLevel);
        CompareString(mismatches, "horizontalDatum", expected.HorizontalDatum, actual.HorizontalDatum);
        CompareString(mismatches, "horizontalCrsIdentifier", expected.HorizontalCrsIdentifier, actual.HorizontalCrsIdentifier);
        CompareString(mismatches, "sourceHorizontalReferenceOrigin", expected.SourceHorizontalReferenceOrigin, actual.SourceHorizontalReferenceOrigin);
        CompareString(mismatches, "verticalDatum", expected.VerticalDatum, actual.VerticalDatum);
        CompareBool(mismatches, "hasVerticalGeoidModel", expected.HasVerticalGeoidModel, actual.HasVerticalGeoidModel);
        CompareString(mismatches, "verticalGeoidModel", expected.VerticalGeoidModel, actual.VerticalGeoidModel);
        CompareString(mismatches, "sourceVerticalReferenceOrigin", expected.SourceVerticalReferenceOrigin, actual.SourceVerticalReferenceOrigin);
        CompareInt(mismatches, "originalPointCount", expected.OriginalPointCount, actual.OriginalPointCount);
        CompareInt(mismatches, "retainedPointCount", expected.RetainedPointCount, actual.RetainedPointCount);
        CompareString(mismatches, "simplificationMethod", expected.SimplificationMethod, actual.SimplificationMethod);
        CompareInt(mismatches, "simplificationPointBudget", expected.SimplificationPointBudget, actual.SimplificationPointBudget);
        CompareBool(mismatches, "hasElevationRange", expected.HasElevationRange, actual.HasElevationRange);
        CompareLength(mismatches, "elevationMinimumMeters", expected.ElevationMinimumMeters, actual.ElevationMinimumMeters, toleranceMeters);
        CompareLength(mismatches, "elevationMaximumMeters", expected.ElevationMaximumMeters, actual.ElevationMaximumMeters, toleranceMeters);
        CompareString(mismatches, "outputUnitToken", expected.OutputUnitToken, actual.OutputUnitToken);
        CompareExactDouble(mismatches, "metersPerOutputUnit", expected.MetersPerOutputUnit, actual.MetersPerOutputUnit);
        CompareLength(mismatches, "localOriginXMeters", expected.LocalOriginXMeters, actual.LocalOriginXMeters, toleranceMeters);
        CompareLength(mismatches, "localOriginYMeters", expected.LocalOriginYMeters, actual.LocalOriginYMeters, toleranceMeters);
        CompareLength(mismatches, "localOriginElevationMeters", expected.LocalOriginElevationMeters, actual.LocalOriginElevationMeters, toleranceMeters);
        CompareString(mismatches, "horizontalForwardOperationFormat", expected.HorizontalForwardOperationFormat, actual.HorizontalForwardOperationFormat);
        CompareString(mismatches, "horizontalForwardOperationDefinition", expected.HorizontalForwardOperationDefinition, actual.HorizontalForwardOperationDefinition);
        CompareString(mismatches, "horizontalInverseOperationFormat", expected.HorizontalInverseOperationFormat, actual.HorizontalInverseOperationFormat);
        CompareString(mismatches, "horizontalInverseOperationDefinition", expected.HorizontalInverseOperationDefinition, actual.HorizontalInverseOperationDefinition);
        CompareString(mismatches, "horizontalTransformEngineName", expected.HorizontalTransformEngineName, actual.HorizontalTransformEngineName);
        CompareString(mismatches, "horizontalTransformEngineVersion", expected.HorizontalTransformEngineVersion, actual.HorizontalTransformEngineVersion);
        CompareString(mismatches, "buildInformationalVersion", expected.BuildInformationalVersion, actual.BuildInformationalVersion);
        CompareString(mismatches, "buildModuleVersionId", expected.BuildModuleVersionId, actual.BuildModuleVersionId);
        CompareString(mismatches, "buildSha256", expected.BuildSha256, actual.BuildSha256);

        return mismatches;
    }

    /// <summary>
    /// Reports the five Length-spec fields' actual delta (<c>actual - expected</c>, in meters),
    /// unconditionally -- whether or not each is within
    /// <see cref="ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance"/> -- so a caller
    /// always has a number to log, matching the field order those fields appear in within
    /// <see cref="ExtensibleStorageProvenanceSchema.Fields"/>. Never throws on a mismatch; sibling to
    /// <see cref="Diff"/>, which does.
    /// </summary>
    public static IReadOnlyList<(string Field, double Delta)> ComputeDeltas(ExtensibleStorageProvenanceValues expected, ExtensibleStorageProvenanceValues actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        return
        [
            ("elevationMinimumMeters", actual.ElevationMinimumMeters - expected.ElevationMinimumMeters),
            ("elevationMaximumMeters", actual.ElevationMaximumMeters - expected.ElevationMaximumMeters),
            ("localOriginXMeters", actual.LocalOriginXMeters - expected.LocalOriginXMeters),
            ("localOriginYMeters", actual.LocalOriginYMeters - expected.LocalOriginYMeters),
            ("localOriginElevationMeters", actual.LocalOriginElevationMeters - expected.LocalOriginElevationMeters),
        ];
    }

    /// <summary>
    /// Reimplements <see cref="LocalCoordinateFrame.ToSource"/>'s componentwise arithmetic from
    /// <paramref name="values"/>'s own flattened, meters-normalized primitive fields, never building a real
    /// <see cref="HorizontalReference"/>/<see cref="LocalCoordinateFrame"/>. This does not need
    /// <c>HorizontalReferenceKind</c>/<c>HorizontalAxisOrder</c> -- neither is stored on this type -- because
    /// <see cref="LocalCoordinateFrame.ToSource"/> never consults axis order either; do not "fix" this by
    /// reconstructing a typed reference object (SolidGround Issue #16 design record §2). Because every input
    /// field is already meters-normalized, the result is directly comparable to <see cref="ToSourceMeters"/>'s
    /// result with no further conversion, regardless of the original source horizontal/vertical unit.
    /// </summary>
    /// <param name="values">The Extensible Storage field values to reconstruct from.</param>
    /// <param name="local">A local coordinate, in the local frame's own output unit.</param>
    /// <returns>The reconstructed source coordinate, in meters.</returns>
    public static Coordinate3D ReconstructSourceCoordinate(ExtensibleStorageProvenanceValues values, LocalCoordinate local)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new Coordinate3D(
            values.LocalOriginXMeters + (local.X * values.MetersPerOutputUnit),
            values.LocalOriginYMeters + (local.Y * values.MetersPerOutputUnit),
            values.LocalOriginElevationMeters + (local.Elevation * values.MetersPerOutputUnit));
    }

    /// <summary>
    /// Converts <paramref name="frame"/>.<see cref="LocalCoordinateFrame.ToSource"/>(<paramref name="local"/>)
    /// into meters, using <see cref="LengthConverter.MetersPerUnit"/> for the frame's own horizontal and
    /// vertical units. <see cref="LocalCoordinateFrame.ToSource"/>'s native result is expressed in the frame's own source units
    /// (which may not be meters -- reachable today via <c>process.verticalUnit</c>/a non-metric <c>.prj</c>);
    /// this keeps that result unit-consistent with <see cref="ReconstructSourceCoordinate"/>'s
    /// always-meters result before the two are compared (SolidGround Issue #16 design record §2, round 3
    /// finding 3).
    /// </summary>
    public static Coordinate3D ToSourceMeters(LocalCoordinateFrame frame, LocalCoordinate local)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Coordinate3D source = frame.ToSource(local);
        double horizontalMetersPerUnit = LengthConverter.MetersPerUnit(frame.ProjectedHorizontalReference.Unit.LinearUnit!.Value);
        double verticalMetersPerUnit = LengthConverter.MetersPerUnit(frame.VerticalReference.Unit);

        return new Coordinate3D(
            source.X * horizontalMetersPerUnit,
            source.Y * horizontalMetersPerUnit,
            source.Elevation * verticalMetersPerUnit);
    }

    private static void RequireFinite(double value, string fieldName)
    {
        if (!double.IsFinite(value))
        {
            // Message text matches the design record's error-catalogue row 21b verbatim: "field '<name>' is
            // not a finite number." -- SolidGround.Revit's own transaction-wide catch surfaces ex.Message
            // directly, so this exact casing and wording is load-bearing for Stage 2's row 21b.
            throw new ProvenanceFieldValueException($"field '{fieldName}' is not a finite number.");
        }
    }

    private static void CompareString(List<string> mismatches, string field, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            mismatches.Add($"{field}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void CompareInt(List<string> mismatches, string field, int expected, int actual)
    {
        if (expected != actual)
        {
            mismatches.Add($"{field}: expected '{FormatInt(expected)}', actual '{FormatInt(actual)}'.");
        }
    }

    private static void CompareBool(List<string> mismatches, string field, bool expected, bool actual)
    {
        if (expected != actual)
        {
            mismatches.Add($"{field}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void CompareExactDouble(List<string> mismatches, string field, double expected, double actual)
    {
        if (!expected.Equals(actual))
        {
            mismatches.Add($"{field}: expected '{FormatDouble(expected)}', actual '{FormatDouble(actual)}'.");
        }
    }

    private static void CompareLength(List<string> mismatches, string field, double expected, double actual, double toleranceMeters)
    {
        bool exactMatch = expected.Equals(actual);
        bool withinTolerance = double.IsFinite(expected) && double.IsFinite(actual) && Math.Abs(expected - actual) <= toleranceMeters;
        if (!exactMatch && !withinTolerance)
        {
            mismatches.Add($"{field}: expected '{FormatDouble(expected)}', actual '{FormatDouble(actual)}' (tolerance {FormatDouble(toleranceMeters)} m).");
        }
    }

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
