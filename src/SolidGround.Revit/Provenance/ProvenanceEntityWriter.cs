using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Provenance;

/// <summary>
/// SolidGround Issue #16's <c>ToposolidCreatedHook</c> implementation: attaches the Extensible Storage
/// provenance entity to a created toposolid and verifies the round trip before the enclosing transaction
/// commits (design record §3). Matches <c>CreateToposolidCommand.ToposolidCreatedHook</c>'s signature exactly
/// so the command's <c>postCreationHook</c> assignment can reference <see cref="Attach"/> as a bare method group. Every
/// exception this raises is a <see cref="ProvenanceAttachmentException"/> or
/// <see cref="ProvenanceFieldValueException"/>, both caught by <c>RunTransaction</c>'s existing general catch,
/// which rolls back the whole toposolid creation (design record D1: provenance attachment is fatal).
/// </summary>
internal static class ProvenanceEntityWriter
{
    /// <exception cref="ProvenanceFieldValueException">A computed Length-spec field is not a finite number.</exception>
    /// <exception cref="ProvenanceAttachmentException">
    /// Schema publish/validation failed, the immediate read-back entity is missing/invalid/unreadable/
    /// version-mismatched, a read-back field did not match what was written, or reconstructing a retained
    /// sample's source coordinate from the read-back entity exceeded
    /// <see cref="ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance"/>.
    /// </exception>
    internal static void Attach(Document document, Toposolid toposolid, TerrainExportPayload payload, PlacementRecordDraft placementDraft)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(toposolid);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(placementDraft);

        ExtensibleStorageProvenanceValues expected = ExtensibleStorageProvenanceValues.From(
            payload.Provenance,
            BuildIdentity.Current.InformationalVersion,
            BuildIdentity.Current.ModuleVersionId,
            BuildIdentity.Current.Sha256);

        Schema schema = ProvenanceSchemaAdapter.EnsurePublishedSchema();

        Entity entity = new(schema);
        entity.Set<int>("schemaVersion", expected.SchemaVersion);
        entity.Set<string>("sourceDatasetName", expected.SourceDatasetName);
        entity.Set<string>("sourceDatasetIdentifier", expected.SourceDatasetIdentifier);
        entity.Set<bool>("hasCollectionPeriod", expected.HasCollectionPeriod);
        entity.Set<string>("collectionPeriodStartIso", expected.CollectionPeriodStartIso);
        entity.Set<string>("collectionPeriodEndIso", expected.CollectionPeriodEndIso);
        entity.Set<bool>("hasQualityLevel", expected.HasQualityLevel);
        entity.Set<string>("qualityLevel", expected.QualityLevel);
        entity.Set<string>("horizontalDatum", expected.HorizontalDatum);
        entity.Set<string>("horizontalCrsIdentifier", expected.HorizontalCrsIdentifier);
        entity.Set<string>("sourceHorizontalReferenceOrigin", expected.SourceHorizontalReferenceOrigin);
        entity.Set<string>("verticalDatum", expected.VerticalDatum);
        entity.Set<bool>("hasVerticalGeoidModel", expected.HasVerticalGeoidModel);
        entity.Set<string>("verticalGeoidModel", expected.VerticalGeoidModel);
        entity.Set<string>("sourceVerticalReferenceOrigin", expected.SourceVerticalReferenceOrigin);
        entity.Set<int>("originalPointCount", expected.OriginalPointCount);
        entity.Set<int>("retainedPointCount", expected.RetainedPointCount);
        entity.Set<string>("simplificationMethod", expected.SimplificationMethod);
        entity.Set<int>("simplificationPointBudget", expected.SimplificationPointBudget);
        entity.Set<bool>("hasElevationRange", expected.HasElevationRange);
        entity.Set<double>("elevationMinimumMeters", expected.ElevationMinimumMeters, UnitTypeId.Meters);
        entity.Set<double>("elevationMaximumMeters", expected.ElevationMaximumMeters, UnitTypeId.Meters);
        entity.Set<string>("outputUnitToken", expected.OutputUnitToken);
        entity.Set<double>("metersPerOutputUnit", expected.MetersPerOutputUnit);
        entity.Set<double>("localOriginXMeters", expected.LocalOriginXMeters, UnitTypeId.Meters);
        entity.Set<double>("localOriginYMeters", expected.LocalOriginYMeters, UnitTypeId.Meters);
        entity.Set<double>("localOriginElevationMeters", expected.LocalOriginElevationMeters, UnitTypeId.Meters);
        entity.Set<string>("horizontalForwardOperationFormat", expected.HorizontalForwardOperationFormat);
        entity.Set<string>("horizontalForwardOperationDefinition", expected.HorizontalForwardOperationDefinition);
        entity.Set<string>("horizontalInverseOperationFormat", expected.HorizontalInverseOperationFormat);
        entity.Set<string>("horizontalInverseOperationDefinition", expected.HorizontalInverseOperationDefinition);
        entity.Set<string>("horizontalTransformEngineName", expected.HorizontalTransformEngineName);
        entity.Set<string>("horizontalTransformEngineVersion", expected.HorizontalTransformEngineVersion);
        entity.Set<string>("buildInformationalVersion", expected.BuildInformationalVersion);
        entity.Set<string>("buildModuleVersionId", expected.BuildModuleVersionId);
        entity.Set<string>("buildSha256", expected.BuildSha256);

        toposolid.SetEntity(entity);

        string elementIdText = toposolid.Id.Value.ToString(CultureInfo.InvariantCulture);

        // Four-part read discipline (design record §3 step 4), applied to the host element immediately after
        // SetEntity: Schema.Lookup(guid) == null only proves the schema is unregistered in *this* session
        // (the owner's other add-in's documented lesson), so (a) checks the element's own recognized schema GUIDs first.
        if (!toposolid.GetEntitySchemaGuids().Contains(schema.GUID))
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but " +
                "GetEntitySchemaGuids() does not list SolidGround's schema immediately afterward.");
        }

        Entity readBack = toposolid.GetEntity(schema);
        if (!readBack.IsValid())
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but the " +
                "immediate read-back entity is not valid.");
        }

        if (!readBack.ReadAccessGranted())
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but read " +
                "access was not granted on the immediate read-back entity.");
        }

        int actualSchemaVersion = readBack.Get<int>("schemaVersion");
        if (actualSchemaVersion != ExtensibleStorageProvenanceSchema.CurrentVersion)
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but its " +
                $"schemaVersion read back as {actualSchemaVersion.ToString(CultureInfo.InvariantCulture)}, expected " +
                $"{ExtensibleStorageProvenanceSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        // Read the remaining 35 fields into a second value, mirroring SolidGround.Tests's own
        // RawConstructorMatchesFromForAnEquivalentInstance coverage of this raw constructor.
        ExtensibleStorageProvenanceValues actual = new(
            SchemaVersion: actualSchemaVersion,
            SourceDatasetName: readBack.Get<string>("sourceDatasetName"),
            SourceDatasetIdentifier: readBack.Get<string>("sourceDatasetIdentifier"),
            HasCollectionPeriod: readBack.Get<bool>("hasCollectionPeriod"),
            CollectionPeriodStartIso: readBack.Get<string>("collectionPeriodStartIso"),
            CollectionPeriodEndIso: readBack.Get<string>("collectionPeriodEndIso"),
            HasQualityLevel: readBack.Get<bool>("hasQualityLevel"),
            QualityLevel: readBack.Get<string>("qualityLevel"),
            HorizontalDatum: readBack.Get<string>("horizontalDatum"),
            HorizontalCrsIdentifier: readBack.Get<string>("horizontalCrsIdentifier"),
            SourceHorizontalReferenceOrigin: readBack.Get<string>("sourceHorizontalReferenceOrigin"),
            VerticalDatum: readBack.Get<string>("verticalDatum"),
            HasVerticalGeoidModel: readBack.Get<bool>("hasVerticalGeoidModel"),
            VerticalGeoidModel: readBack.Get<string>("verticalGeoidModel"),
            SourceVerticalReferenceOrigin: readBack.Get<string>("sourceVerticalReferenceOrigin"),
            OriginalPointCount: readBack.Get<int>("originalPointCount"),
            RetainedPointCount: readBack.Get<int>("retainedPointCount"),
            SimplificationMethod: readBack.Get<string>("simplificationMethod"),
            SimplificationPointBudget: readBack.Get<int>("simplificationPointBudget"),
            HasElevationRange: readBack.Get<bool>("hasElevationRange"),
            ElevationMinimumMeters: readBack.Get<double>("elevationMinimumMeters", UnitTypeId.Meters),
            ElevationMaximumMeters: readBack.Get<double>("elevationMaximumMeters", UnitTypeId.Meters),
            OutputUnitToken: readBack.Get<string>("outputUnitToken"),
            MetersPerOutputUnit: readBack.Get<double>("metersPerOutputUnit"),
            LocalOriginXMeters: readBack.Get<double>("localOriginXMeters", UnitTypeId.Meters),
            LocalOriginYMeters: readBack.Get<double>("localOriginYMeters", UnitTypeId.Meters),
            LocalOriginElevationMeters: readBack.Get<double>("localOriginElevationMeters", UnitTypeId.Meters),
            HorizontalForwardOperationFormat: readBack.Get<string>("horizontalForwardOperationFormat"),
            HorizontalForwardOperationDefinition: readBack.Get<string>("horizontalForwardOperationDefinition"),
            HorizontalInverseOperationFormat: readBack.Get<string>("horizontalInverseOperationFormat"),
            HorizontalInverseOperationDefinition: readBack.Get<string>("horizontalInverseOperationDefinition"),
            HorizontalTransformEngineName: readBack.Get<string>("horizontalTransformEngineName"),
            HorizontalTransformEngineVersion: readBack.Get<string>("horizontalTransformEngineVersion"),
            BuildInformationalVersion: readBack.Get<string>("buildInformationalVersion"),
            BuildModuleVersionId: readBack.Get<string>("buildModuleVersionId"),
            BuildSha256: readBack.Get<string>("buildSha256"));

        // Computed once, before either throwing check below, and always logged -- a failure in Diff or the
        // reconstruction compare must not suppress this number (design record §3 step 6, round 2 finding 8).
        IReadOnlyList<(string Field, double Delta)> deltas = ExtensibleStorageProvenanceValues.ComputeDeltas(expected, actual);
        AddInLog.Info($"Extensible Storage round-trip deltas (meters) for element {elementIdText}: {FormatDeltas(deltas)}.");

        IReadOnlyList<string> mismatches = ExtensibleStorageProvenanceValues.Diff(expected, actual);
        if (mismatches.Count > 0)
        {
            // Round 2 review (Repository compliance): Diff never short-circuits and can return up to 35
            // mismatched field names, some holding long transform-definition strings. AGENTS.md's "Settings
            // and logging" rule -- "a long list is capped in the dialog and written in full to the log
            // folder" -- applies here exactly as it already does to ProblemReportDialog.BuildRejectionBody,
            // which exists specifically so an unbounded list can never grow a TaskDialog past the screen and
            // take its own close button with it (that dialog's own remarks cite the owner's other add-in's 2026-08-04
            // incident). Log the complete, uncapped list here first: RunTransaction's general catch also
            // logs this exception's Message via AddInLog.Error(headline, ex), but that copy is capped below.
            AddInLog.Error(
                $"Extensible Storage round-trip mismatches for element {elementIdText}: {string.Join(" | ", mismatches)} " +
                $"(deltas: {FormatDeltas(deltas)}).");
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but the " +
                $"immediate read-back did not match what was written: {FormatBoundedMismatches(mismatches)} " +
                $"(deltas: {FormatDeltas(deltas)}).");
        }

        LocalCoordinate sampleLocal = payload.Samples[0].Position;
        Coordinate3D reconstructed = ExtensibleStorageProvenanceValues.ReconstructSourceCoordinate(actual, sampleLocal);
        Coordinate3D expectedSource = ExtensibleStorageProvenanceValues.ToSourceMeters(payload.Provenance.LocalFrame, sampleLocal);
        double toleranceMeters = ExtensibleStorageProvenanceSchema.ExtensibleStorageRoundTripTolerance.ToMeters();
        double deltaX = reconstructed.X - expectedSource.X;
        double deltaY = reconstructed.Y - expectedSource.Y;
        double deltaElevation = reconstructed.Elevation - expectedSource.Elevation;

        if (Math.Abs(deltaX) > toleranceMeters || Math.Abs(deltaY) > toleranceMeters || Math.Abs(deltaElevation) > toleranceMeters)
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround attached its Extensible Storage entity to element {elementIdText}, but " +
                "reconstructing the first retained sample's source coordinate from the read-back entity " +
                $"produced a delta of ({FormatMeters(deltaX)}, {FormatMeters(deltaY)}, {FormatMeters(deltaElevation)}) " +
                $"meters against a {FormatMeters(toleranceMeters)} m tolerance " +
                $"(deltas: {FormatDeltas(deltas)}).");
        }

        AddInLog.Info(
            $"Attached Extensible Storage provenance (schema {schema.GUID.ToString("D", CultureInfo.InvariantCulture)} " +
            $"v{ExtensibleStorageProvenanceSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture)}) to element {elementIdText}.");
    }

    /// <summary>
    /// Bounds <paramref name="mismatches"/> to <see cref="ProblemReportDialog.MaxInlineProblems"/> entries
    /// for the thrown message -- the same cap that dialog already applies to its own inline problem lists,
    /// and for the same reason (see the call site). The full, uncapped list is logged separately before
    /// this runs.
    /// </summary>
    private static string FormatBoundedMismatches(IReadOnlyList<string> mismatches)
    {
        int shown = Math.Min(ProblemReportDialog.MaxInlineProblems, mismatches.Count);
        string joined = string.Join(" | ", mismatches.Take(shown));
        int omitted = mismatches.Count - shown;
        return omitted > 0
            ? $"{joined} ... and {omitted.ToString(CultureInfo.InvariantCulture)} more; see the log for the complete list."
            : joined;
    }

    private static string FormatDeltas(IReadOnlyList<(string Field, double Delta)> deltas) =>
        string.Join(", ", deltas.Select(delta => $"{delta.Field}={FormatMeters(delta.Delta)}"));

    private static string FormatMeters(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
