using SolidGround.Core.Provenance;

namespace SolidGround.Tests;

public sealed class ExistingTerrainRecordValidatorTests
{
    private const string DocumentGuid = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void AcceptsLegacyV1IdentityWithoutContextAndV2IdentityWithFloorOrStandaloneContext()
    {
        ExistingTerrainRecordValidator.Validate(Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm), false, "opaque-unique-id", DocumentGuid);
        ExistingTerrainRecordValidator.Validate(Identity(TerrainIdentity.FloorReferenceVersion, TerrainIdentity.FloorReferenceContentSignatureAlgorithm), true, "opaque-unique-id", DocumentGuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("polygon")]
    public void RejectsNonCanonicalOrUndefinedIdentityKindTokens(string token) =>
        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.ParseKind(token));

    [Fact]
    public void RejectsAnUndefinedIdentityKindEvenWhenItWasConstructedOutsideTheNativeBoundary()
    {
        TerrainIdentity identity = Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm) with { Kind = (TerrainIdentityKind)999 };

        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.Validate(identity, false, "opaque-unique-id", DocumentGuid));
    }

    [Theory]
    [InlineData("terrain-identity-v2", "sha256-canonical-run-v2", false)]
    [InlineData("terrain-identity-v1", "sha256-canonical-run-v1", true)]
    [InlineData("unsupported", "sha256-canonical-run-v1", false)]
    [InlineData("terrain-identity-v1", "unsupported", false)]
    public void RejectsUnsupportedOrMismatchedIdentityVersionAndAlgorithm(string version, string algorithm, bool hasFloorOrContext)
    {
        TerrainIdentity identity = Identity(version, algorithm);

        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.Validate(identity, hasFloorOrContext, "opaque-unique-id", DocumentGuid));
    }

    [Theory]
    [InlineData("Stem", "")]
    [InlineData("ContentSignature", "")]
    [InlineData("PointFrameHash", "")]
    [InlineData("Stem", "0123456789ABCDEF")]
    [InlineData("ContentSignature", "0123456789ABCDEF")]
    [InlineData("PointFrameHash", "0123456789ABCDEF")]
    [InlineData("Stem", "GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("ContentSignature", "GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("PointFrameHash", "GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void RejectsBlankShortOrNonHexIdentityHashes(string field, string invalid)
    {
        TerrainIdentity identity = field switch
        {
            "Stem" => Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm) with { Stem = invalid },
            "ContentSignature" => Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm) with { ContentSignature = invalid },
            "PointFrameHash" => Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm) with { PointFrameHash = invalid },
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.Validate(identity, false, "opaque-unique-id", DocumentGuid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankStoredOriginalUniqueId(string uniqueId) =>
        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.Validate(Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm), false, uniqueId, DocumentGuid));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{11111111-2222-3333-4444-555555555555}")]
    public void RejectsMissingOrNonCanonicalStoredOriginalDocumentGuid(string documentGuid) =>
        Assert.Throws<ArgumentException>(() => ExistingTerrainRecordValidator.Validate(Identity(TerrainIdentity.CurrentVersion, TerrainIdentity.CurrentContentSignatureAlgorithm), false, "opaque-unique-id", documentGuid));

    private static TerrainIdentity Identity(string version, string algorithm) => new(
        version,
        TerrainIdentityKind.Polygon,
        Hash('A'),
        algorithm,
        Hash('B'),
        Hash('C'));

    private static string Hash(char character) => new(character, 64);
}
