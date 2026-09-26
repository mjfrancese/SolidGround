using SolidGround.Core.Sources;
using SolidGround.Core.Sources.LocalParcelFile;

namespace SolidGround.Tests;

public sealed class LocalParcelFileSourceTests
{
    private const string OwnerFieldGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-002",
                "address": "100 Example Loop",
                "owner": "SYNTHETIC OWNER",
                "mailadd": "SYNTHETIC OWNER MAILING ADDRESS"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Feature 0 is a complete, valid record matching the point/address queries used below; feature 1 is
    /// "elsewhere" (its ring is shifted ~550 m north, well outside feature 0's ~40 m parcel and the query
    /// point, but still within <c>PersonalInformationGuardTests</c>' 0.01-degree tolerance of the public
    /// example site) and has a blank mapped <c>address</c> -- a realistic shape for a county-wide export's
    /// administrative/water/right-of-way row. Proves a query never aborts on an unrelated feature's own
    /// incomplete data.
    /// </summary>
    private const string TwoFeatureOneBlankRequiredFieldElsewhereGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-001",
                "address": "100 Example Loop"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            },
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-004",
                "address": ""
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.596012685],[-93.603564371, 41.596015206],[-93.603567727, 41.596375478],[-93.604047631, 41.596372958],[-93.604044272, 41.596012685]]]
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Feature 0 is the complete, matching parcel; feature 1 is "elsewhere" (shifted ~550 m north, same shift
    /// as <see cref="TwoFeatureOneBlankRequiredFieldElsewhereGeoJson"/>'s own unrelated feature) with
    /// <c>"properties": null</c> -- GeoJSON's own spec-legal shape (RFC 7946 section 3.2: "the value of the
    /// properties member is an object or a JSON null value"), a realistic shape for a county-wide export's
    /// administrative/water/right-of-way row. Proves a query never aborts merely because an unrelated
    /// feature's properties object is entirely missing, rather than merely incomplete.
    /// </summary>
    private const string TwoFeatureOneNullPropertiesElsewhereGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-001",
                "address": "100 Example Loop"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            },
            {
              "type": "Feature",
              "properties": null,
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.596012685],[-93.603564371, 41.596015206],[-93.603567727, 41.596375478],[-93.604047631, 41.596372958],[-93.604044272, 41.596012685]]]
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Feature 0 is the complete, matching parcel; feature 1 is "elsewhere" (same shift as above) with a
    /// <c>"Point"</c> geometry -- a type <see cref="SolidGround.Core.Aois.ParcelGeometryParser"/> never accepts
    /// as polygonal, so parsing it always throws. A realistic shape for a corrupted or partially exported
    /// county-wide row. Proves a point query never aborts merely because an unrelated feature's geometry
    /// cannot be parsed at all.
    /// </summary>
    private const string TwoFeatureOneUnparsableGeometryElsewhereGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-001",
                "address": "100 Example Loop"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            },
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-005",
                "address": "200 Example Loop"
              },
              "geometry": {
                "type": "Point",
                "coordinates": [-93.604044272, 41.596012685]
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Feature 0 is the complete, matching parcel; feature 1 is "elsewhere" (same shift as the fixtures above)
    /// with its own per-feature <c>crs</c> member naming a non-WGS-84 CRS, and a mapped <c>address</c> that
    /// does not itself contain the "Example Loop"/"example loop" search text used below -- unlike
    /// <see cref="FeatureLevelLegacyCrsGeoJson"/>'s single feature, which is itself the query's own match and
    /// so cannot tell a deferred crs check apart from an eager one. Proves
    /// <c>LocalParcelFileSource.TryBuildCandidate</c>'s per-feature <c>ValidateCrsIfPresent</c> call stays
    /// deferred behind both relevance filters: an unrelated feature's own disagreeing <c>crs</c> must never
    /// abort resolution of a different, matching feature.
    /// </summary>
    private const string TwoFeatureOneDisagreeingCrsElsewhereGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-001",
                "address": "100 Example Loop"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            },
            {
              "type": "Feature",
              "crs": { "type": "name", "properties": { "name": "urn:ogc:def:crs:EPSG::26915" } },
              "properties": {
                "parcelnumb": "SYNTHETIC-PARCELNUMB-006",
                "address": "900 Nowhere Loop"
              },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.596012685],[-93.603564371, 41.596015206],[-93.603567727, 41.596375478],[-93.604047631, 41.596372958],[-93.604044272, 41.596012685]]]
              }
            }
          ]
        }
        """;

    private const string RootLevelLegacyCrsGeoJson = """
        {
          "type": "FeatureCollection",
          "crs": { "type": "name", "properties": { "name": "urn:ogc:def:crs:EPSG::26915" } },
          "features": []
        }
        """;

    private const string FeatureLevelLegacyCrsGeoJson = """
        {
          "type": "FeatureCollection",
          "features": [
            {
              "type": "Feature",
              "crs": { "type": "name", "properties": { "name": "urn:ogc:def:crs:EPSG::26915" } },
              "properties": { "parcelnumb": "SYNTHETIC-PARCELNUMB-003", "address": "100 Example Loop" },
              "geometry": {
                "type": "Polygon",
                "coordinates": [[[-93.604044272, 41.591012685],[-93.603564371, 41.591015206],[-93.603567727, 41.591375478],[-93.604047631, 41.591372958],[-93.604044272, 41.591012685]]]
              }
            }
          ]
        }
        """;

    [Fact]
    public async Task PointQueryFindsTheFabricatedParcelWithAreaWithinTolerance()
    {
        LocalParcelFileSource source = CreateSource(FixturePath());

        ParcelBoundaryAcquisition acquisition = await source.FindAsync(
            new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

        ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        Assert.Equal("100 Example Loop", candidate.SitusAddress);
        double relativeDifference = Math.Abs(candidate.ComputedAreaSquareMeters - 1600d) / 1600d;
        Assert.True(relativeDifference < 0.02d, $"Expected approximately 1600 sq m, computed {candidate.ComputedAreaSquareMeters}.");
        Assert.Equal(ParcelBoundarySourceKind.LocalParcelFile, candidate.SourceKind);
        Assert.Equal("Test Local File", candidate.SourceIdentity);
        Assert.Equal("Test disclaimer.", candidate.LicenseDisclaimerText);
        Assert.False(candidate.BookPageAreUnconfirmedProxies);
        Assert.Equal("SYNTHETIC SUBDIVISION", candidate.Subdivision);
        Assert.Equal("R-1", candidate.Zoning);
        Assert.Equal("00000000-0000-4000-8000-000000000099", candidate.StableParcelId);
        Assert.False(acquisition.ResultSetTruncated);
    }

    [Fact]
    public async Task AddressSubstringQueryIsCaseInsensitiveAndFindsTheSameFeature()
    {
        LocalParcelFileSource source = CreateSource(FixturePath());

        ParcelBoundaryAcquisition acquisition = await source.FindAsync(
            new ParcelAddressQuery("example loop"), TestContext.Current.CancellationToken);

        ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
    }

    [Fact]
    public async Task APointOutsideEveryFeatureEnvelopeReturnsAnEmptyListWithoutThrowing()
    {
        LocalParcelFileSource source = CreateSource(FixturePath());

        ParcelBoundaryAcquisition acquisition = await source.FindAsync(
            new ParcelPointQuery(42.0d, -93.0d), TestContext.Current.CancellationToken);

        Assert.Empty(acquisition.Candidates);
    }

    [Fact]
    public async Task APointExactlyOnTheBoundaryIsStillReturned()
    {
        LocalParcelFileSource source = CreateSource(FixturePath());

        ParcelBoundaryAcquisition acquisition = await source.FindAsync(
            new ParcelPointQuery(41.591012685d, -93.604044272d), TestContext.Current.CancellationToken);

        Assert.Single(acquisition.Candidates);
    }

    [Fact]
    public async Task APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasABlankRequiredField()
    {
        string path = WriteTempFile(TwoFeatureOneBlankRequiredFieldElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnAddressQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasABlankRequiredField()
    {
        string path = WriteTempFile(TwoFeatureOneBlankRequiredFieldElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelAddressQuery("Example Loop"), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasNullProperties()
    {
        string path = WriteTempFile(TwoFeatureOneNullPropertiesElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnAddressQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasNullProperties()
    {
        string path = WriteTempFile(TwoFeatureOneNullPropertiesElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelAddressQuery("Example Loop"), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasUnparsableGeometry()
    {
        string path = WriteTempFile(TwoFeatureOneUnparsableGeometryElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasADisagreeingCrs()
    {
        string path = WriteTempFile(TwoFeatureOneDisagreeingCrsElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnAddressQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasADisagreeingCrs()
    {
        string path = WriteTempFile(TwoFeatureOneDisagreeingCrsElsewhereGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelAddressQuery("Example Loop"), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCELNUMB-001", candidate.ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OwnerFieldsArePhysicallyPresentInTheFileButNeverReachACandidate()
    {
        string path = WriteTempFile(OwnerFieldGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            AssertNoPropertyContainsTheOwnerValue(candidate, "SYNTHETIC OWNER");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ARootLevelLegacyNonWgs84CrsMemberIsRejected()
    {
        string path = WriteTempFile(RootLevelLegacyCrsGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            await Assert.ThrowsAsync<LocalParcelFileFormatException>(
                async () => await source.FindAsync(new ParcelAddressQuery("Example"), TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AFeatureLevelLegacyNonWgs84CrsMemberIsRejected()
    {
        string path = WriteTempFile(FeatureLevelLegacyCrsGeoJson);
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            await Assert.ThrowsAsync<LocalParcelFileFormatException>(
                async () => await source.FindAsync(new ParcelAddressQuery("Example"), TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AMissingFileMapsToNotFoundException()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"solidground-local-parcel-missing-{Guid.NewGuid():N}.geojson");
        LocalParcelFileSource source = CreateSource(missingPath);

        await Assert.ThrowsAsync<LocalParcelFileNotFoundException>(
            async () => await source.FindAsync(new ParcelAddressQuery("Example"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMalformedJsonFileMapsToFormatException()
    {
        string path = WriteTempFile("not json");
        try
        {
            LocalParcelFileSource source = CreateSource(path);

            await Assert.ThrowsAsync<LocalParcelFileFormatException>(
                async () => await source.FindAsync(new ParcelAddressQuery("Example"), TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AFileLockedByAnotherHandleMapsToAccessException()
    {
        string path = WriteTempFile(OwnerFieldGeoJson);
        try
        {
            using FileStream exclusiveLock = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            LocalParcelFileSource source = CreateSource(path);

            await Assert.ThrowsAsync<LocalParcelFileAccessException>(
                async () => await source.FindAsync(new ParcelAddressQuery("Example"), TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConstructorRejectsAnOwnerLikeFieldMap()
    {
        Assert.Throws<ArgumentException>(() => new LocalParcelFileSource(new LocalParcelFileOptions
        {
            Path = FixturePath(),
            FieldMap = new() { LegalDescription = "owner" },
            SourceLabel = "Test Local File",
            LicenseDisclaimerText = "Test disclaimer.",
        }));
    }

    private static void AssertNoPropertyContainsTheOwnerValue(ParcelBoundaryCandidate candidate, string ownerValue)
    {
        string?[] stringProperties =
        [
            candidate.ParcelId, candidate.SourceIdentity, candidate.LicenseDisclaimerText, candidate.SitusAddress,
            candidate.Subdivision, candidate.Lot, candidate.Block, candidate.Plat, candidate.Book, candidate.Page,
            candidate.LegalDescription, candidate.Zoning, candidate.StableParcelId, candidate.AccuracyLabel,
        ];

        foreach (string? value in stringProperties)
        {
            if (value is not null)
            {
                Assert.DoesNotContain(ownerValue, value, StringComparison.Ordinal);
            }
        }
    }

    private static LocalParcelFileSource CreateSource(string path) => new(new LocalParcelFileOptions
    {
        Path = path,
        SourceLabel = "Test Local File",
        LicenseDisclaimerText = "Test disclaimer.",
    });

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "local-parcel-file-standard-schema-synthetic.geojson");

    private static string WriteTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-local-parcel-test-{Guid.NewGuid():N}.geojson");
        File.WriteAllText(path, content);
        return path;
    }
}
