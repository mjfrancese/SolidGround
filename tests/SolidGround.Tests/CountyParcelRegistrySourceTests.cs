using System.Net;
using System.Text;
using System.Text.Json;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

/// <summary>
/// Contract tests against <see cref="FakeHttpMessageHandler"/>. The registry document is never a committed
/// fixture (its <c>serviceBaseUrl</c> is inherently <c>https://</c>, which <c>FixtureSecurityTests</c> bans
/// under <c>Fixtures/</c>): every test here builds it as an inline JSON literal and writes it to a uniquely
/// named temporary file, deleted in a <c>finally</c> block.
/// </summary>
public sealed class CountyParcelRegistrySourceTests
{
    private const string ExpectedDisclaimer =
        "SYNTHETIC-FIXTURE-DISCLAIMER: this data is provided \"as is\" for testing only, with no warranty of any kind, express or implied, and is not an official record of any government entity.";

    private const string RegistryJson = """
        {
          "schemaVersion": 1,
          "counties": [
            {
              "geoid": "99999",
              "displayName": "Synthetic County (fixture only)",
              "serviceBaseUrl": "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer",
              "layerIndex": 0,
              "fieldMap": {
                "parcelId": "PARCEL_ID",
                "situsAddress": "SITUS_ADDR",
                "subdivision": "SUBDIVISION",
                "lot": "LOT_NUMBER",
                "block": "BLOCK",
                "plat": "PLAT_NUMBER",
                "book": "DEED_BOOK_PAGE",
                "page": "DEED_BOOK_PAGE",
                "legalDescription": "LEGAL",
                "reportedAcres": "ACRES",
                "zoning": "ZONING",
                "stableParcelId": null
              },
              "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: this data is provided \"as is\" for testing only, with no warranty of any kind, express or implied, and is not an official record of any government entity."
            }
          ]
        }
        """;

    [Fact]
    public async Task UnregisteredGeoidThrowsBeforeAnyHttpRequest()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "00000");

            CountyParcelRegistryUnregisteredGeoidException error = await Assert.ThrowsAsync<CountyParcelRegistryUnregisteredGeoidException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.Empty(handler.Requests);
            Assert.Equal("00000", error.Geoid);
            Assert.Equal(path, error.RegistryPath);
            Assert.Null(error.RedactedRequestUri);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PointQueryBuildsTheDocumentedQueryStringAndParsesTheFixtureIntoOneCandidate()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            string fixtureBody = ReadFixture("county-parcel-registry-point-hit-synthetic.json");
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, fixtureBody));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            HttpRequestMessage sent = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, sent.Method);
            string expectedUri =
                "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer/0/query" +
                "?f=json&outFields=PARCEL_ID,SITUS_ADDR,SUBDIVISION,LOT_NUMBER,BLOCK,PLAT_NUMBER,DEED_BOOK_PAGE,LEGAL,ACRES,ZONING" +
                "&returnGeometry=true&outSR=4326&spatialRel=esriSpatialRelIntersects" +
                "&geometryType=esriGeometryPoint&geometry=-93.603806,41.591194&inSR=4326&where=1%3D1";
            Assert.Equal(expectedUri, sent.RequestUri!.AbsoluteUri);
            Assert.DoesNotContain('*', sent.RequestUri.Query);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            Assert.Equal("SYNTHETIC-PARCEL-001", candidate.ParcelId);
            Assert.Equal("100 Example Loop", candidate.SitusAddress);
            Assert.Equal(ParcelBoundarySourceKind.CountyRegistry, candidate.SourceKind);
            Assert.Equal("Synthetic County (fixture only) (GEOID 99999)", candidate.SourceIdentity);
            Assert.True(candidate.BookPageAreUnconfirmedProxies);
            double relativeDifference = Math.Abs(candidate.ComputedAreaSquareMeters - 1600d) / 1600d;
            Assert.True(relativeDifference < 0.02d, $"Expected approximately 1600 sq m, computed {candidate.ComputedAreaSquareMeters}.");

            // Proves "verbatim" end-to-end: the registry entry's own disclaimer, the fixture's own embedded
            // property, and the resulting candidate's disclaimer all agree exactly.
            Assert.Equal(ExpectedDisclaimer, candidate.LicenseDisclaimerText);
            using JsonDocument fixtureDocument = JsonDocument.Parse(fixtureBody);
            Assert.Equal(ExpectedDisclaimer, fixtureDocument.RootElement.GetProperty("licenseDisclaimerText").GetString());
            Assert.False(acquisition.ResultSetTruncated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddressQueryBuildsTheDocumentedWhereClauseAndParsesTwoCandidatesIndependently()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            string fixtureBody = ReadFixture("county-parcel-registry-address-hit-synthetic.json");
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, fixtureBody));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            // A space and an embedded single quote prove escaping order: '\' doubled first, then '%'/'_'
            // escaped, then "'" doubled last, so an escaped \'-shaped sequence is never re-mangled by the
            // quote-doubling step.
            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelAddressQuery("O'Malley Loop"), TestContext.Current.CancellationToken);

            HttpRequestMessage sent = Assert.Single(handler.Requests);
            const string expectedWhereClause = "UPPER(SITUS_ADDR) LIKE UPPER('%O''Malley Loop%') ESCAPE '\\'";
            string expectedUri =
                "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer/0/query" +
                "?f=json&outFields=PARCEL_ID,SITUS_ADDR,SUBDIVISION,LOT_NUMBER,BLOCK,PLAT_NUMBER,DEED_BOOK_PAGE,LEGAL,ACRES,ZONING" +
                "&returnGeometry=true&outSR=4326&spatialRel=esriSpatialRelIntersects" +
                $"&resultRecordCount=25&where={Uri.EscapeDataString(expectedWhereClause)}";
            Assert.Equal(expectedUri, sent.RequestUri!.AbsoluteUri);

            Assert.Equal(2, acquisition.Candidates.Count);
            Assert.Equal("SYNTHETIC-PARCEL-001", acquisition.Candidates[0].ParcelId);
            Assert.Equal("SYNTHETIC-PARCEL-002", acquisition.Candidates[1].ParcelId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ZeroResultsFixtureYieldsNoCandidatesWithoutThrowing()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("county-parcel-registry-zero-results-synthetic.json")));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            Assert.Empty(acquisition.Candidates);
            Assert.False(acquisition.ResultSetTruncated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExceededTransferLimitFixtureSetsResultSetTruncatedWhileStillReturningTheCandidate()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("county-parcel-registry-exceeded-transfer-limit-synthetic.json")));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            Assert.True(acquisition.ResultSetTruncated);
            Assert.Single(acquisition.Candidates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A400ErrorObjectMapsToARequestValidationException()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            const string body = """{"error":{"code":400,"message":"Invalid query parameters."}}""";
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryRequestValidationException error = await Assert.ThrowsAsync<CountyParcelRegistryRequestValidationException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(400, error.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A500ErrorObjectMapsToAServerException()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            const string body = """{"error":{"code":500,"message":"Internal error."}}""";
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryServerException error = await Assert.ThrowsAsync<CountyParcelRegistryServerException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.Equal(500, error.ErrorCode);
            Assert.Null(error.TransportStatusCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AMalformedBodyMapsToAnUnexpectedResponseException()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "not json"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            await Assert.ThrowsAsync<CountyParcelRegistryUnexpectedResponseException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnAddressSearchTextOver200CharactersIsRejectedBeforeAnyRequest()
    {
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");
            string tooLong = new('A', CountyParcelRegistrySource.MaximumAddressSearchTextLength + 1);

            CountyParcelRegistryRequestValidationException error = await Assert.ThrowsAsync<CountyParcelRegistryRequestValidationException>(
                () => source.FindAsync(new ParcelAddressQuery(tooLong), TestContext.Current.CancellationToken).AsTask());

            Assert.Empty(handler.Requests);
            Assert.Null(error.RedactedRequestUri);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AServiceBaseUrlContainingAFakeTokenNeverLeaksItIntoARedactedUriOrExceptionMessage()
    {
        const string fakeToken = "fixture-fake-token-0123456789";
        string json = RegistryJson.Replace(
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
            $"\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer?token={fakeToken}\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.InternalServerError, "boom"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryServerException error = await Assert.ThrowsAsync<CountyParcelRegistryServerException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.DoesNotContain(fakeToken, error.Message, StringComparison.Ordinal);
            Assert.NotNull(error.RedactedRequestUri);
            Assert.DoesNotContain(fakeToken, error.RedactedRequestUri, StringComparison.Ordinal);
            Assert.Contains("REDACTED", error.RedactedRequestUri, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A <c>serviceBaseUrl</c> carrying its own query string is not rejected by <see cref="CountyParcelRegistry.Load"/>
    /// today (see the fake-token leak test above, which depends on that). This proves the request built from
    /// such an entry is still well-formed: the <c>/{layerIndex}/query</c> path segment is preserved (never
    /// swallowed into the query string), the entry's own parameter survives as a distinct leading parameter,
    /// and every parameter this source appends remains its own separate, correctly named pair -- guarding
    /// against a naive string concatenation silently merging the two into one corrupted value.
    /// </summary>
    [Fact]
    public async Task AServiceBaseUrlContainingItsOwnQueryStringStillComposesAWellFormedLayerQueryRequest()
    {
        const string fakeAgencyId = "fixture-fake-agency-id-0123456789";
        string json = RegistryJson.Replace(
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
            $"\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer?agency={fakeAgencyId}\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            string fixtureBody = ReadFixture("county-parcel-registry-point-hit-synthetic.json");
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, fixtureBody));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            HttpRequestMessage sent = Assert.Single(handler.Requests);
            string expectedUri =
                "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer/0/query" +
                $"?agency={fakeAgencyId}" +
                "&f=json&outFields=PARCEL_ID,SITUS_ADDR,SUBDIVISION,LOT_NUMBER,BLOCK,PLAT_NUMBER,DEED_BOOK_PAGE,LEGAL,ACRES,ZONING" +
                "&returnGeometry=true&outSR=4326&spatialRel=esriSpatialRelIntersects" +
                "&geometryType=esriGeometryPoint&geometry=-93.603806,41.591194&inSR=4326&where=1%3D1";
            Assert.Equal(expectedUri, sent.RequestUri!.AbsoluteUri);
            Assert.Single(acquisition.Candidates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Regression coverage for <c>SendGetAsync</c>'s three transport-failure catch blocks, which
    /// <see cref="AServiceBaseUrlContainingAFakeTokenNeverLeaksItIntoARedactedUriOrExceptionMessage"/> above
    /// does not exercise (that test's fake handler returns an ordinary HTTP 500 response, never throws, so it
    /// only reaches <c>ParseBody</c>'s own redaction path). A registered county's own <c>serviceBaseUrl</c> may
    /// legitimately carry a leading <c>?token=...</c> (see
    /// <see cref="AServiceBaseUrlContainingItsOwnQueryStringStillComposesAWellFormedLayerQueryRequest"/> above),
    /// and this source's own thrown exceptions must never let that token escape through a transport
    /// exception's own <see cref="Exception.Message"/>, mirroring <c>EsriGeocoderTests</c>' and
    /// <c>OpenTopographyUsgs1mSourceTests</c>' equivalent coverage for their own HTTP sources.
    /// </summary>
    [Fact]
    public async Task WrapsAnHttpRequestExceptionAsANetworkExceptionWithoutLeakingTheTokenThroughTheMessageOrInnerException()
    {
        const string fakeToken = "fixture-fake-token-transport-failure-0123456789";
        string path = WriteTempRegistry(RegistryJsonWithLeadingToken(fakeToken));
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
                throw new HttpRequestException($"Connection dropped while sending request to {request.RequestUri}"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryNetworkException error = await Assert.ThrowsAsync<CountyParcelRegistryNetworkException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.IsType<HttpRequestException>(error.InnerException);
            AssertNetworkExceptionNeverLeaksTheToken(error, fakeToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WrapsAnIOExceptionAsANetworkExceptionWithoutLeakingTheTokenThroughTheMessageOrInnerException()
    {
        const string fakeToken = "fixture-fake-token-transport-failure-0123456789";
        string path = WriteTempRegistry(RegistryJsonWithLeadingToken(fakeToken));
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
                throw new IOException($"Connection dropped while sending request to {request.RequestUri}"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryNetworkException error = await Assert.ThrowsAsync<CountyParcelRegistryNetworkException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.IsType<IOException>(error.InnerException);
            AssertNetworkExceptionNeverLeaksTheToken(error, fakeToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Exercises the general <c>catch (OperationCanceledException ex)</c> branch (a simulated timeout), not the
    /// caller-cancellation passthrough immediately above it: <see cref="TestContext.Current"/>'s own
    /// <see cref="CancellationToken"/> is never itself cancelled here, matching
    /// <c>OpenTopographyUsgs1mSourceTests.WrapsATimeoutStyleCancellationAsANetworkExceptionWhenTheCallerTokenIsNotCancelled</c>'s
    /// identical distinction.
    /// </summary>
    [Fact]
    public async Task WrapsATimeoutStyleCancellationAsANetworkExceptionWithoutLeakingTheTokenWhenTheCallerTokenIsNotCancelled()
    {
        const string fakeToken = "fixture-fake-token-transport-failure-0123456789";
        string path = WriteTempRegistry(RegistryJsonWithLeadingToken(fakeToken));
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
                throw new TaskCanceledException($"Simulated timeout while requesting {request.RequestUri}"));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            CountyParcelRegistryNetworkException error = await Assert.ThrowsAsync<CountyParcelRegistryNetworkException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.IsType<TaskCanceledException>(error.InnerException);
            AssertNetworkExceptionNeverLeaksTheToken(error, fakeToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string RegistryJsonWithLeadingToken(string fakeToken) => RegistryJson.Replace(
        "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
        $"\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer?token={fakeToken}\",");

    private static void AssertNetworkExceptionNeverLeaksTheToken(CountyParcelRegistryNetworkException error, string fakeToken)
    {
        Assert.DoesNotContain(fakeToken, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeToken, error.InnerException!.Message, StringComparison.Ordinal);
        Assert.NotNull(error.RedactedRequestUri);
        Assert.Contains("REDACTED", error.RedactedRequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeToken, error.RedactedRequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARingWhoseSourcePointsCarryAThirdZOrdinateHasItDroppedXyPreserved()
    {
        const string bodyWithZOrdinates = """
            {
              "features": [
                {
                  "attributes": { "PARCEL_ID": "SYNTHETIC-PARCEL-Z", "SITUS_ADDR": "100 Example Loop" },
                  "geometry": {
                    "rings": [
                      [
                        [-93.604044272, 41.591012685, 183.0],
                        [-93.604047631, 41.591372958, 183.1],
                        [-93.603567727, 41.591375478, 183.2],
                        [-93.603564371, 41.591015206, 183.3],
                        [-93.604044272, 41.591012685, 183.0]
                      ]
                    ]
                  }
                }
              ]
            }
            """;
        string path = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);
            var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, bodyWithZOrdinates));
            using var httpClient = new HttpClient(handler);
            var source = new CountyParcelRegistrySource(httpClient, registry, "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            ParcelBoundaryCandidate candidate = Assert.Single(acquisition.Candidates);
            double relativeDifference = Math.Abs(candidate.ComputedAreaSquareMeters - 1600d) / 1600d;
            Assert.True(relativeDifference < 0.02d, $"Expected approximately 1600 sq m, computed {candidate.ComputedAreaSquareMeters}.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static string WriteTempRegistry(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-county-registry-source-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static Task<HttpResponseMessage> TextResponse(HttpStatusCode statusCode, string body)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
