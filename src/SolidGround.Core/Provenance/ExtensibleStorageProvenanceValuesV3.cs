using SolidGround.Core.Exports;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>V3 values compose the immutable v2 value set with canonical floor and outline context.</summary>
public sealed record ExtensibleStorageProvenanceValuesV3(
    ExtensibleStorageProvenanceValuesV2 BaseValues,
    string FloorReferenceJson,
    string BuildingOutlineJson)
{
    public static ExtensibleStorageProvenanceValuesV3 From(
        TerrainExportPayload payload,
        TerrainIdentity identity,
        double coverageFloorFraction,
        string buildInformationalVersion,
        string buildModuleVersionId,
        string buildSha256)
    {
        ArgumentNullException.ThrowIfNull(payload);
        BuildingFloorReference? floorReference = payload.Provenance.FloorReference;
        BuildingOutlineProvenance? buildingOutline = payload.Provenance.BuildingOutline;
        if (floorReference is null && buildingOutline is null)
        {
            throw new ProvenanceFieldValueException("V3 provenance requires a building-floor reference or building-outline context.");
        }

        string floorReferenceJson = BuildingFloorReferenceJson.Serialize(floorReference);
        string buildingOutlineJson = BuildingOutlineProvenanceJson.Serialize(buildingOutline);
        BuildingFloorReference? roundTrippedFloor = BuildingFloorReferenceJson.Deserialize(floorReferenceJson);
        BuildingOutlineProvenance? roundTrippedOutline = BuildingOutlineProvenanceJson.Deserialize(buildingOutlineJson);
        if (!string.Equals(BuildingFloorReferenceJson.Serialize(roundTrippedFloor), floorReferenceJson, StringComparison.Ordinal) ||
            !string.Equals(BuildingOutlineProvenanceJson.Serialize(roundTrippedOutline), buildingOutlineJson, StringComparison.Ordinal))
        {
            throw new ProvenanceFieldValueException("V3 provenance context did not round-trip through its canonical JSON representation.");
        }

        if (roundTrippedFloor?.BuildingOutline is { } embeddedOutline &&
            (roundTrippedOutline is null || !BuildingOutlineProvenanceJson.HasSameValue(embeddedOutline, roundTrippedOutline)))
        {
            throw new ProvenanceFieldValueException("V3 provenance floor and standalone building-outline context conflict.");
        }

        if (roundTrippedFloor is not null)
        {
            if (roundTrippedFloor.SourceReference != payload.Provenance.SourceVerticalReference)
            {
                throw new ProvenanceFieldValueException("V3 provenance building-floor reference uses a vertical reference different from the terrain payload.");
            }

            double targetLevelInSourceUnit = LengthConverter.Convert(
                roundTrippedFloor.TargetLevel.ProjectElevationInternal,
                LengthUnit.InternationalFoot,
                roundTrippedFloor.SourceReference.Unit);
            double frameOriginElevation = roundTrippedFloor.ResolveFrameReferenceElevation() - targetLevelInSourceUnit;
            if (!double.IsFinite(frameOriginElevation) || frameOriginElevation != payload.Provenance.LocalFrame.Origin.Elevation)
            {
                throw new ProvenanceFieldValueException("V3 provenance building-floor reference does not reproduce the terrain payload's local-frame elevation origin.");
            }
        }

        return new(
            ExtensibleStorageProvenanceValuesV2.From(
                payload, identity, coverageFloorFraction, buildInformationalVersion, buildModuleVersionId, buildSha256),
            floorReferenceJson,
            buildingOutlineJson);
    }

    public ExtensibleStorageProvenanceValuesV3 WithStoredOriginalElementIdentity(string uniqueId, string documentCreationGuid) => new(
        BaseValues.WithStoredOriginalElementIdentity(uniqueId, documentCreationGuid), FloorReferenceJson, BuildingOutlineJson);

    public ExtensibleStorageProvenanceValuesV3 WithNativeVertexFingerprint(string fingerprint) => new(
        BaseValues.WithNativeVertexFingerprint(fingerprint), FloorReferenceJson, BuildingOutlineJson);
}
