using SolidGround.Core.Provenance;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ExistingTerrainDecisionTests
{
    private static readonly TerrainIdentity Proposed = new(
        TerrainIdentity.CurrentVersion, TerrainIdentityKind.StableParcel, "STEM", TerrainIdentity.CurrentContentSignatureAlgorithm, "CONTENT", "POINTS");

    [Fact]
    public void ExactRepeatReusesOnlyTheOriginalElementInTheSameDocumentHistory()
    {
        ExistingTerrainDecision decision = ExistingTerrainDecision.Decide(Proposed, "DOC", [Record()], []);

        Assert.Equal(ExistingTerrainDecisionKind.Reuse, decision.Kind);
        Assert.Equal(["42"], decision.ElementIds);
    }

    [Fact]
    public void CopiedElementAndChangedPhysicalContentRefuse()
    {
        ExistingTerrainDecision copied = ExistingTerrainDecision.Decide(Proposed, "NEW-DOC", [Record()], []);
        ExistingTerrainDecision changed = ExistingTerrainDecision.Decide(Proposed, "DOC", [Record(identity: Proposed with { PointFrameHash = "CHANGED" })], []);

        Assert.Equal(ExistingTerrainDecisionKind.Refuse, copied.Kind);
        Assert.Contains("different creation GUID", copied.Detail, StringComparison.Ordinal);
        Assert.Equal(ExistingTerrainDecisionKind.Refuse, changed.Kind);
    }

    [Fact]
    public void CollisionsAndLegacyEntitiesNeverAutoCreate()
    {
        ExistingTerrainDecision collision = ExistingTerrainDecision.Decide(Proposed, "DOC", [Record("42"), Record("43")], []);
        ExistingTerrainDecision legacy = ExistingTerrainDecision.Decide(Proposed, "DOC", [], ["7"]);

        Assert.Equal(ExistingTerrainDecisionKind.Refuse, collision.Kind);
        Assert.Equal(["42", "43"], collision.ElementIds);
        Assert.Equal(ExistingTerrainDecisionKind.RequiresLegacyAcknowledgement, legacy.Kind);
    }

    [Fact]
    public void MissingIdentityCreatesWithAnExplicitFallbackOutcome()
    {
        ExistingTerrainDecision decision = ExistingTerrainDecision.Decide(Proposed with { Kind = TerrainIdentityKind.BoundingBox }, "DOC", [], []);

        Assert.Equal(ExistingTerrainDecisionKind.Create, decision.Kind);
        Assert.Contains("No existing", decision.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityUsesStableParcelAndFinalPointsButNotLookupDate()
    {
        TerrainIdentity first = TerrainIdentity.Create(Payload(new DateOnly(2026, 9, 1)), TerrainIdentityKind.Polygon, "ignored-fallback", "POLYGON((0 0,1 0,1 1,0 0))", 0d);
        TerrainIdentity sameRunDifferentLookupDate = TerrainIdentity.Create(Payload(new DateOnly(2026, 9, 2)), TerrainIdentityKind.Polygon, "ignored-fallback", "POLYGON((0 0,1 0,1 1,0 0))", 0d);
        TerrainIdentity changedPoints = TerrainIdentity.Create(Payload(new DateOnly(2026, 9, 1), secondPointX: 3d), TerrainIdentityKind.Polygon, "ignored-fallback", "POLYGON((0 0,1 0,1 1,0 0))", 0d);

        Assert.Equal(TerrainIdentityKind.StableParcel, first.Kind);
        Assert.Equal(first.Stem, sameRunDifferentLookupDate.Stem);
        Assert.Equal(first.ContentSignature, sameRunDifferentLookupDate.ContentSignature);
        Assert.NotEqual(first.PointFrameHash, changedPoints.PointFrameHash);
        Assert.NotEqual(first.ContentSignature, changedPoints.ContentSignature);
    }

    private static ExistingTerrainRecord Record(string elementId = "42", TerrainIdentity? identity = null) =>
        new(elementId, identity ?? Proposed, "UID", "UID", "DOC");

    private static TerrainExportPayload Payload(DateOnly retrievalDate, double secondPointX = 1d)
    {
        HorizontalReference geographic = new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        HorizontalTransformationDefinition transform = new(geographic, projected, new("WKT1", "forward"), new("WKT1", "inverse"), "test", "1");
        LocalCoordinateFrame frame = new(new Coordinate3D(100d, 200d, 10d), projected, vertical, LengthUnit.Meter);
        ParcelProvenance parcel = new(ParcelBoundarySourceKind.CountyRegistry, "Synthetic source", "parcel-1", "stable-1", null, "Synthetic license.");
        AddressParcelProvenance addressParcel = new(retrievalDate, null, parcel);
        TerrainProvenance provenance = new(4, new ElevationSourceMetadata("Synthetic", "synthetic"), transform, vertical,
            ReferenceOrigin.Operator, ReferenceOrigin.Operator, frame, new SimplificationRequest(2, SimplificationMethod.CurvatureAware), 2, 2,
            new ElevationRange(10d, 11d, LengthUnit.Meter), addressParcel);
        return new TerrainExportPayload([new(new LocalCoordinate(0d, 0d, 0d)), new(new LocalCoordinate(secondPointX, 1d, 1d))], provenance);
    }
}
