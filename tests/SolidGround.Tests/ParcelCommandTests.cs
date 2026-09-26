using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SolidGround.Cli;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Sources.LocalParcelFile;
using SolidGround.Core.Transformations;

namespace SolidGround.Tests;

/// <summary>
/// The CLI's `parcel` verb: fully-offline point-against-local-file resolution, the geocode-then-point chain,
/// the explicit-GEOID-skips-Census-lookup rule, the `--offline` flag's own definition, zero-candidate/usage
/// exit-code classification, the settings write and round-trip (AC3), and help text. See
/// docs/architecture/cli-workflow.md's "### parcel" subsection.
/// </summary>
public sealed class ParcelCommandTests
{
    private const string ExamplePoint = "41.591194,-93.603806";

    // Mirrors CountyParcelRegistrySourceTests.RegistryJson exactly (never a committed fixture: its
    // serviceBaseUrl is inherently https://, which FixtureSecurityTests bans under Fixtures/).
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

    // ---- fixtures for SelectWritesTheRequestedCandidateNotTheFirst --------------------------------------
    // Two rings with genuinely different areas -- unlike county-parcel-registry-address-hit-synthetic.json's
    // own two candidates, whose rings are byte-identical, so reading back a written file could never tell
    // them apart. The first ring is the exact boundary already committed as
    // county-parcel-registry-point-hit-synthetic.json's own feature; the second is a distinctly larger box in
    // the same corner-traversal (and so winding) order, so both are accepted by EsriJsonPolygonReader as a
    // single clockwise shell with no holes.
    private static readonly (double X, double Y)[] FirstDistinctCandidateRing =
    [
        (-93.604044272, 41.591012685),
        (-93.604047631, 41.591372958),
        (-93.603567727, 41.591375478),
        (-93.603564371, 41.591015206),
        (-93.604044272, 41.591012685),
    ];

    private static readonly (double X, double Y)[] SecondDistinctCandidateRing =
    [
        (-93.604000000, 41.590000000),
        (-93.604000000, 41.593000000),
        (-93.601000000, 41.593000000),
        (-93.601000000, 41.590000000),
        (-93.604000000, 41.590000000),
    ];

    /// <summary>An ArcGIS <c>FeatureServer</c> query response carrying two candidates built from the two rings above, so the response body and the values used to compute each candidate's expected area can never drift apart.</summary>
    private static string TwoDistinctCandidatesRegistryJson() => $$"""
        {
          "objectIdFieldName": "OBJECTID",
          "geometryType": "esriGeometryPolygon",
          "spatialReference": { "wkid": 4326, "latestWkid": 4326 },
          "features": [
            { "attributes": { "PARCEL_ID": "SYNTHETIC-PARCEL-001", "SITUS_ADDR": "100 Example Loop" }, "geometry": { "rings": [{{RingToJsonArray(FirstDistinctCandidateRing)}}] } },
            { "attributes": { "PARCEL_ID": "SYNTHETIC-PARCEL-002", "SITUS_ADDR": "202 Example Loop" }, "geometry": { "rings": [{{RingToJsonArray(SecondDistinctCandidateRing)}}] } }
          ]
        }
        """;

    private static string RingToJsonArray((double X, double Y)[] ring)
    {
        List<string> points = new(ring.Length);
        foreach ((double x, double y) in ring)
        {
            points.Add($"[{x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}]");
        }

        return "[" + string.Join(",", points) + "]";
    }

    [Fact]
    public async Task PointAgainstLocalFileIsFullyOfflineAndDeterministic()
    {
        string[] args =
        [
            "parcel", "--point", ExamplePoint, "--source", "local-file",
            "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
        ];

        (int firstExitCode, string firstStdout, _) = await RunAsync(CreateOfflineHost(), args, TestContext.Current.CancellationToken);
        (int secondExitCode, string secondStdout, _) = await RunAsync(CreateOfflineHost(), args, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, firstExitCode);
        Assert.Equal(CliExitCodes.Success, secondExitCode);
        Assert.Equal(firstStdout, secondStdout);

        using JsonDocument document = JsonDocument.Parse(firstStdout);
        JsonElement root = document.RootElement;
        Assert.Equal("point", root.GetProperty("input").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("geocodeInput").ValueKind);
        Assert.Equal(1, root.GetProperty("candidateCount").GetInt32());
        Assert.Equal("SYNTHETIC-PARCELNUMB-001", root.GetProperty("candidates")[0].GetProperty("parcelId").GetString());
    }

    [Fact]
    public async Task AddressAgainstLocalFileGeocodesThenReadsLocallyInThatOrder()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, string stdout, _) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Single(handler.Requests);
        Assert.Contains("onelineaddress", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.Equal("address", root.GetProperty("input").GetProperty("kind").GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("geocodeInput").ValueKind);
        Assert.Equal("Census", root.GetProperty("geocodeInput").GetProperty("provider").GetString());
        Assert.Equal(1, root.GetProperty("candidateCount").GetInt32());
    }

    [Fact]
    public async Task AddressWithGeocodioProviderAndNoKeyConfiguredExitsAuthorizationWithZeroRequests()
    {
        // Mirrors GeocodeCommandTests.GeocodioWithNoKeyConfiguredExitsAuthorizationWithZeroRequests, but
        // through parcel's own --geocode-provider option and BuildGeocoder call site (ParcelCommand.RunAsync's
        // step 2), not geocode's --provider. GeocodioGeocoder.GeocodeAsync throws
        // GeocodioGeocoderAuthorizationException from inside itself, before any HTTP request, so --source
        // local-file (never queried either) keeps this fully offline-verifiable via zero requests.
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, string.Empty));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--geocode-provider", "geocodio", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Authorization, exitCode);
        Assert.Empty(handler.Requests);
        Assert.Contains("GEOCODIO_API_KEY", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddressWithEsriProviderAndNoKeyConfiguredExitsAuthorizationWithZeroRequests()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, string.Empty));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--geocode-provider", "esri", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Authorization, exitCode);
        Assert.Empty(handler.Requests);
        Assert.Contains("ARCGIS_API_KEY", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PointAgainstCountyRegistryWithExplicitGeoidSkipsTheCensusLookup()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("county-parcel-registry-point-hit-synthetic.json")));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath, "--geoid", "99999"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Single(handler.Requests);
            Assert.Contains("FeatureServer", handler.Requests[0].RequestUri!.AbsoluteUri, StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement sourceDetail = document.RootElement.GetProperty("sourceDetail");
            Assert.Equal("99999", sourceDetail.GetProperty("geoid").GetString());
            Assert.Equal("explicit", sourceDetail.GetProperty("geoidOrigin").GetString());
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task PointAgainstCountyRegistryWithoutGeoidCallsCensusLookupFirst()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            FakeHttpMessageHandler handler = new((request, _) => RouteChainedResponse(request));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("geographies/coordinates", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("FeatureServer", handler.Requests[1].RequestUri!.AbsoluteUri, StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement sourceDetail = document.RootElement.GetProperty("sourceDetail");
            Assert.Equal("99999", sourceDetail.GetProperty("geoid").GetString());
            Assert.Equal("censusLookup", sourceDetail.GetProperty("geoidOrigin").GetString());
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task AddressAgainstCountyRegistryWithoutGeoidChainsAllThreeRequestsInOrder()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            FakeHttpMessageHandler handler = new((request, _) => RouteChainedResponse(request));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                ["parcel", "--address", "100 Example Loop", "--source", "county-registry", "--registry", registryPath],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Contains("onelineaddress", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("geographies/coordinates", handler.Requests[1].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("FeatureServer", handler.Requests[2].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.NotEmpty(stdout);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task CensusLookupServerErrorExitsSourceQualityWithoutQueryingTheRegistry()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            // Mirrors GeocodeCommandTests.EachProviderServerErrorExitsSourceQuality's census row: an empty body
            // with a 500 status. CensusCountyLookup.FindCountyGeoidAsync throws CensusCountyLookupServerException
            // before CountyParcelRegistrySource is ever constructed (ParcelCommand.RunAsync's own step 3), so
            // exactly one request -- the Census lookup itself -- is ever sent; the registry's FeatureServer is
            // never queried.
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.SourceQuality, exitCode);
            Assert.Contains("error (source-quality):", stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
            Assert.Single(handler.Requests);
            Assert.Contains("geographies/coordinates", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task CensusLookupNoCountyExitsNotFoundWithoutQueryingTheRegistry()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            // A 200 response whose geographies object carries no Counties key (the documented "off the parcel
            // fabric" shape) makes CensusCountyLookup.FindCountyGeoidAsync throw CensusCountyLookupNoCountyException
            // before CountyParcelRegistrySource is ever constructed, so exactly one request -- the Census lookup
            // itself -- is ever sent; the registry's FeatureServer is never queried.
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-nomatch-synthetic.json")));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.NotFound, exitCode);
            Assert.Contains("error (not-found):", stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
            Assert.Single(handler.Requests);
            Assert.Contains("geographies/coordinates", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task CountyRegistryServerErrorExitsSourceQuality()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            // Explicit --geoid skips the Census lookup (ParcelCommand.RunAsync's step 3), so the scripted 500
            // is the registry's own FeatureServer request, throwing CountyParcelRegistryServerException and
            // exercising CliApplication's catch (ParcelBoundarySourceException ex) clause -- a documented exit
            // code (source-quality) distinct from CensusLookupServerErrorExitsSourceQualityWithoutQueryingTheRegistry's
            // sibling catch (CensusCountyLookupException ex) clause above.
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath, "--geoid", "99999"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.SourceQuality, exitCode);
            Assert.Contains("error (source-quality):", stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
            Assert.Single(handler.Requests);
            Assert.Contains("FeatureServer", handler.Requests[0].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task CountyRegistryRequestValidationErrorExitsUsage()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            // Same explicit-geoid, single-request shape as CountyRegistryServerErrorExitsSourceQuality above,
            // but a scripted 400-class error object (CountyParcelRegistrySource's own ClassifyErrorBody client-
            // error branch) instead of an empty-body 500, exercising CliApplication's own
            // catch (CountyParcelRegistryRequestValidationException ex) clause -- a documented exit code
            // (usage) distinct from that sibling test's (source-quality). See
            // docs/architecture/cli-workflow.md's "Exit codes and error classes" section.
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, """{"error":{"code":400,"message":"Invalid query parameters."}}"""));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath, "--geoid", "99999"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
            Assert.Single(handler.Requests);
            Assert.Contains("FeatureServer", handler.Requests[0].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task OfflineWithAddressExitsUsageWithZeroRequests()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--offline", "--address", "100 Example Loop", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--offline", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineWithCountyRegistrySourceExitsUsageWithZeroRequests()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CliHost host = CreateOfflineHost();

            (int exitCode, _, string stderr) = await RunAsync(
                host,
                ["parcel", "--offline", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("--offline", stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task OfflineWithPointAndLocalFileSucceedsWithZeroRequests()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, _) = await RunAsync(
            host,
            [
                "parcel", "--offline", "--point", ExamplePoint, "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task UnregisteredExplicitGeoidExitsUsageWithZeroHttpRequestsForTheQuery()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            // The registry is loaded and the GEOID resolved (skipping the Census lookup, since --geoid is
            // explicit) before CountyParcelRegistrySource is even constructed; that source's own contract
            // guarantees zero HTTP requests for an unregistered GEOID, but constructing the shared HttpClient
            // itself (never an outgoing request) is still expected, so this uses a handler that would fail the
            // test if a request were actually sent, not a throwing HttpMessageHandlerFactory.
            FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("must not send a request"));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, _, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath, "--geoid", "00000"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("00000", stderr, StringComparison.Ordinal);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task MalformedRegistryFileExitsUsageViaTheExistingFormatExceptionClause()
    {
        string registryPath = WriteTempRegistry("this is not valid json");
        try
        {
            CliHost host = CreateOfflineHost();

            (int exitCode, _, string stderr) = await RunAsync(
                host,
                ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", registryPath, "--geoid", "99999"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task ZeroParcelCandidatesExitsNotFound()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, string stdout, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--point", "0,0", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.NotFound, exitCode);
        Assert.Contains("error (not-found):", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task LocalFileNotFoundExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--point", ExamplePoint, "--source", "local-file",
                "--local-file", Path.Combine(Path.GetTempPath(), $"solidground-missing-{Guid.NewGuid():N}.geojson"),
                "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalFileMalformedJsonExitsUsage()
    {
        // Mirrors LocalParcelFileSourceTests.AMalformedJsonFileMapsToFormatException's own trigger (invalid
        // JSON content), but through the parcel CLI verb, exercising CliApplication's own
        // catch (LocalParcelFileFormatException ex) clause -- distinct from LocalFileNotFoundExitsUsage's
        // sibling catch (LocalParcelFileNotFoundException ex) clause above. See
        // docs/architecture/cli-workflow.md's "Exit codes and error classes" section.
        string localFilePath = WriteTempLocalParcelFile("not json");
        try
        {
            CliHost host = CreateOfflineHost();

            (int exitCode, _, string stderr) = await RunAsync(
                host,
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", localFilePath, "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(localFilePath);
        }
    }

    [Fact]
    public async Task LocalFileLockedByAnotherHandleExitsUsage()
    {
        // Mirrors LocalParcelFileSourceTests.AFileLockedByAnotherHandleMapsToAccessException's own
        // FileShare.None technique, but through the parcel CLI verb, exercising CliApplication's own
        // catch (LocalParcelFileAccessException ex) clause. File.Exists still finds the path (it needs no
        // share rights), so it is File.ReadAllText inside LocalParcelFileSource.ReadFile that then fails with a
        // sharing-violation IOException. See docs/architecture/cli-workflow.md's "Exit codes and error
        // classes" section.
        string localFilePath = WriteTempLocalParcelFile("irrelevant: never read before the lock rejects the open request");
        try
        {
            using (new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CliHost host = CreateOfflineHost();

                (int exitCode, _, string stderr) = await RunAsync(
                    host,
                    [
                        "parcel", "--point", ExamplePoint, "--source", "local-file",
                        "--local-file", localFilePath, "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    ],
                    TestContext.Current.CancellationToken);

                Assert.Equal(CliExitCodes.Usage, exitCode);
                Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(localFilePath);
        }
    }

    [Fact]
    public async Task OutOfRangeGeocodeCandidateExitsUsage()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--geocode-candidate", "2", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geocode-candidate", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutOfRangeSelectExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--point", ExamplePoint, "--select", "2", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--select", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeocodeCandidateSelectsTheRequestedCandidateNotTheFirst()
    {
        // Regression test for ParcelCommand.cs's `acquisition.Candidates[geocodeCandidateNumber - 1]`
        // indexing (an off-by-one or ignored-option regression would otherwise pass the whole suite
        // undetected). census-multi-candidate-synthetic.json's second candidate sits at a different point
        // (35, -110) than its first (41.591194, -93.603806); an explicit --geoid skips the Census county
        // lookup, so exactly two requests occur, and the second one's own query point -- plus the printed
        // geocodeInput block -- proves which candidate --geocode-candidate 2 actually selected.
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            FakeHttpMessageHandler handler = new((request, _) => RouteMultiCandidateGeocodeThenRegistry(request));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                [
                    "parcel", "--address", "100 Example Loop", "--geocode-candidate", "2", "--source", "county-registry",
                    "--registry", registryPath, "--geoid", "99999",
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("onelineaddress", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("geometry=-110,35", handler.Requests[1].RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.DoesNotContain("-93.603806", handler.Requests[1].RequestUri!.AbsoluteUri, StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement geocodeInput = document.RootElement.GetProperty("geocodeInput");
            Assert.Equal(2, geocodeInput.GetProperty("candidateIndex").GetInt32());
            Assert.Equal("100 EXAMPLE LOOP, OTHERSITE, YY, 00000", geocodeInput.GetProperty("matchedAddress").GetString());
            Assert.Equal(35d, geocodeInput.GetProperty("latitude").GetDouble());
            Assert.Equal(-110d, geocodeInput.GetProperty("longitude").GetDouble());
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task SelectWritesTheRequestedCandidateNotTheFirst()
    {
        // Regression test for ParcelCommand.cs's `parcelAcquisition.Candidates[selectNumber - 1]` indexing.
        // county-parcel-registry-address-hit-synthetic.json's own two candidates carry byte-identical rings,
        // so a written file could never distinguish them; TwoDistinctCandidatesRegistryJson instead scripts
        // two genuinely different-sized rings, and this asserts the written parcel.wkt's own area matches
        // the second candidate's, not the first's.
        string registryPath = WriteTempRegistry(RegistryJson);
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, TwoDistinctCandidatesRegistryJson()));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                [
                    "parcel", "--point", ExamplePoint, "--select", "2", "--source", "county-registry",
                    "--registry", registryPath, "--geoid", "99999", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument document = JsonDocument.Parse(stdout);
            Assert.Equal(2, document.RootElement.GetProperty("candidateCount").GetInt32());
            Assert.Equal(2, document.RootElement.GetProperty("selectedCandidateIndex").GetInt32());

            string wktText = await File.ReadAllTextAsync(Path.Combine(tempDirectory.FullName, "parcel.wkt"), TestContext.Current.CancellationToken);
            HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
            PolygonalRegion written = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wktText, wgs84Reference);

            PolygonalRegion firstCandidateBoundary = EsriJsonPolygonReader.Read([FirstDistinctCandidateRing], wgs84Reference, 0);
            PolygonalRegion secondCandidateBoundary = EsriJsonPolygonReader.Read([SecondDistinctCandidateRing], wgs84Reference, 0);

            Assert.Equal(secondCandidateBoundary.Area, written.Area, 9);
            Assert.NotEqual(firstCandidateBoundary.Area, written.Area);
        }
        finally
        {
            File.Delete(registryPath);
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- Step 1 usage-validation branches (each runs before any I/O, so every test below uses
    // ---- CreateOfflineHost -- a stray HTTP call would itself fail the test with an unhandled exception). ----

    [Fact]
    public async Task BothPointAndAddressGivenExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--address", "100 Example Loop", "--source", "local-file"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Exactly one of --point or --address is required.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeitherPointNorAddressGivenExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--source", "local-file"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("Exactly one of --point or --address is required.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeocodeProviderWithoutAddressExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--geocode-provider", "census", "--source", "local-file"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geocode-provider and --geocode-candidate only apply when --address is given.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeocodeCandidateWithoutAddressExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--geocode-candidate", "1", "--source", "local-file"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geocode-provider and --geocode-candidate only apply when --address is given.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSourceTokenExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--source", "bogus"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--source must be one of: county-registry, local-file.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeoidWithLocalFileSourceExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--source", "local-file", "--geoid", "99999"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geoid only applies when --source is county-registry.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountyRegistrySourceWithoutRegistryExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--source", "county-registry"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--registry is required when --source is county-registry.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalFileSourceMissingRequiredFieldsExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--source", "local-file", "--local-file", LocalFileFixturePath()],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains(
            "--local-file, --local-file-label, and --local-file-disclaimer are all required when --source is local-file.",
            stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedGeoidFormatExitsUsage()
    {
        // The registry path is never opened here: this check (Step 1) runs before CountyParcelRegistry.Load
        // (Step 3), so an unwritten path is enough to prove the format guard fires first.
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            ["parcel", "--point", ExamplePoint, "--source", "county-registry", "--registry", "unused-registry.json", "--geoid", "1234"],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geoid must be exactly 5 digits.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidGeocodeProviderTokenNamesTheGeocodeProviderFlagAndExitsUsage()
    {
        // Regression test for the --geocode-provider/--provider flag-naming bug: GeocodeCommand.ParseProvider
        // takes an optionName parameter precisely so this message names the flag parcel actually defines
        // (--geocode-provider), never geocode's own --provider.
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--geocode-provider", "bogus", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geocode-provider must be one of: census, geocodio, esri.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonNumericGeocodeCandidateExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--address", "100 Example Loop", "--geocode-candidate", "abc", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--geocode-candidate must be a positive integer.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonPositiveSelectExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--point", ExamplePoint, "--select", "0", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--select must be a positive integer.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NegativeBufferExitsUsage()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, _, string stderr) = await RunAsync(
            host,
            [
                "parcel", "--point", ExamplePoint, "--buffer", "-1", "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--buffer must be a finite number greater than or equal to zero.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesParcelWktAndAoiFragmentAndSettingsRoundTripsToAnEquivalentBoundary()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            CliHost host = CreateOfflineHost();

            (int exitCode, _, _) = await RunAsync(
                host,
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            string wktPath = Path.Combine(tempDirectory.FullName, "parcel.wkt");
            string aoiJsonPath = Path.Combine(tempDirectory.FullName, "parcel.aoi.json");
            Assert.True(File.Exists(wktPath));
            Assert.True(File.Exists(aoiJsonPath));

            AoiSettings settings = JsonSerializer.Deserialize<AoiSettings>(File.ReadAllText(aoiJsonPath), TerrainRequestSettings.JsonOptions)!;
            Assert.Equal(AreaOfInterestKind.Parcel, settings.Kind);
            string wktText = File.ReadAllText(Path.Combine(tempDirectory.FullName, settings.Parcel!.Path));
            HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
            AreaOfInterest reconstructed = AoiSettingsFactory.Build(settings, wgs84Reference, wktText);
            PolygonalRegion roundTripped = ParcelGeometryParser.Parse((ParcelGeometryAoi)reconstructed);

            var source = new LocalParcelFileSource(new LocalParcelFileOptions
            {
                Path = LocalFileFixturePath(),
                SourceLabel = "Test Label",
                LicenseDisclaimerText = "Test disclaimer.",
            });
            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);
            ParcelBoundaryCandidate original = Assert.Single(acquisition.Candidates);

            Assert.Equal(original.Boundary.Area, roundTripped.Area, 9);
            Assert.Equal(original.Boundary.PolygonCount, roundTripped.PolygonCount);
            Assert.Equal(original.Boundary.HoleCount, roundTripped.HoleCount);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task OverwriteGuardsTheTwoWrittenFiles()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string[] args =
            [
                "parcel", "--point", ExamplePoint, "--source", "local-file",
                "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                "--output", tempDirectory.FullName,
            ];

            (int firstExitCode, _, _) = await RunAsync(CreateOfflineHost(), args, TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, firstExitCode);

            (int secondExitCode, _, string stderr) = await RunAsync(CreateOfflineHost(), args, TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Usage, secondExitCode);
            Assert.Contains("--overwrite", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunningTwiceProducesByteIdenticalStdoutAndWrittenFiles()
    {
        DirectoryInfo firstDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo secondDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int firstExitCode, string firstStdout, _) = await RunAsync(
                CreateOfflineHost(),
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    "--output", firstDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            (int secondExitCode, string secondStdout, _) = await RunAsync(
                CreateOfflineHost(),
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    "--output", secondDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, firstExitCode);
            Assert.Equal(CliExitCodes.Success, secondExitCode);
            Assert.Equal(firstStdout, secondStdout);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory.FullName, "parcel.wkt")),
                File.ReadAllBytes(Path.Combine(secondDirectory.FullName, "parcel.wkt")));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory.FullName, "parcel.aoi.json")),
                File.ReadAllBytes(Path.Combine(secondDirectory.FullName, "parcel.aoi.json")));
        }
        finally
        {
            Directory.Delete(firstDirectory.FullName, recursive: true);
            Directory.Delete(secondDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunningTwiceThroughTheOnlineCountyRegistryPathProducesByteIdenticalStdoutAndWrittenFiles()
    {
        // AC1 companion to RunningTwiceProducesByteIdenticalStdoutAndWrittenFiles above: that test only covers
        // parcel's fully offline --source local-file path. This test covers parcel's other source instead --
        // --source county-registry with no --geoid, so the Census county lookup chains into the registry's own
        // FeatureServer query exactly as PointAgainstCountyRegistryWithoutGeoidCallsCensusLookupFirst proves --
        // with a fresh FakeHttpMessageHandler instance per run (never one handler shared across both runs), so
        // AC1's byte-identical guarantee is proven for the online path too, not just the offline one.
        string registryPath = WriteTempRegistry(RegistryJson);
        DirectoryInfo firstDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo secondDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler firstHandler = new((request, _) => RouteChainedResponse(request));
            FakeHttpMessageHandler secondHandler = new((request, _) => RouteChainedResponse(request));

            (int firstExitCode, string firstStdout, _) = await RunAsync(
                CreateHost(firstHandler, _ => null),
                [
                    "parcel", "--point", ExamplePoint, "--source", "county-registry",
                    "--registry", registryPath, "--output", firstDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            (int secondExitCode, string secondStdout, _) = await RunAsync(
                CreateHost(secondHandler, _ => null),
                [
                    "parcel", "--point", ExamplePoint, "--source", "county-registry",
                    "--registry", registryPath, "--output", secondDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, firstExitCode);
            Assert.Equal(CliExitCodes.Success, secondExitCode);
            Assert.Equal(2, firstHandler.Requests.Count);
            Assert.Equal(2, secondHandler.Requests.Count);
            Assert.Equal(firstStdout, secondStdout);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory.FullName, "parcel.wkt")),
                File.ReadAllBytes(Path.Combine(secondDirectory.FullName, "parcel.wkt")));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(firstDirectory.FullName, "parcel.aoi.json")),
                File.ReadAllBytes(Path.Combine(secondDirectory.FullName, "parcel.aoi.json")));
        }
        finally
        {
            File.Delete(registryPath);
            Directory.Delete(firstDirectory.FullName, recursive: true);
            Directory.Delete(secondDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task OutputDirectoryBlockedByAFileExitsProcessingWithoutPrintingAnything()
    {
        // Mirrors FileSystemTerrainExporterTests.ExportAsyncWrapsAFailureToCreateTheOutputDirectoryInATerrainExportException:
        // a path already occupied by a file (not a directory) makes Directory.CreateDirectory throw a raw
        // IOException. RunAsync must wrap it as CliProcessingException (exit 5, "processing"), and -- because
        // the write step now runs before the JSON print step -- stdout must stay completely empty, never a
        // "written" object naming files that were never actually created.
        string blockedPath = Path.Combine(Path.GetTempPath(), $"solidground-parcel-blocked-output-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blockedPath, "not a directory", TestContext.Current.CancellationToken);
        try
        {
            CliHost host = CreateOfflineHost();

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    "--output", blockedPath,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Processing, exitCode);
            Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
            Assert.Contains(blockedPath, stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
        }
        finally
        {
            File.Delete(blockedPath);
        }
    }

    [Fact]
    public async Task ExistingDirectoryBlockingTheWktFileExitsProcessingWithoutPrintingAnything()
    {
        // Directory.CreateDirectory(outputDirectory) succeeds here (outputDirectory is already a real,
        // writable directory); the failure instead comes from File.WriteAllTextAsync once "parcel.wkt"
        // already exists as a subdirectory rather than a file, exercising WriteFileAsync's own try/catch
        // instead of the directory-creation try/catch above it. stdout must still stay empty.
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDirectory.FullName, "parcel.wkt"));
            CliHost host = CreateOfflineHost();

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                [
                    "parcel", "--point", ExamplePoint, "--source", "local-file",
                    "--local-file", LocalFileFixturePath(), "--local-file-label", "Test Label", "--local-file-disclaimer", "Test disclaimer.",
                    "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Processing, exitCode);
            Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
            Assert.Contains("parcel.wkt", stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, stdout);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task HelpTextListsAllOptionsAndTheNotASurveyWording()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, string stdout, _) = await RunAsync(host, ["help", "parcel"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        string[] expectedOptions =
        [
            "--point", "--address", "--geocode-provider", "--geocode-candidate", "--source", "--registry", "--geoid",
            "--local-file", "--local-file-label", "--local-file-disclaimer", "--select", "--buffer", "--output",
            "--name", "--overwrite", "--offline", "--timeout",
        ];
        foreach (string option in expectedOptions)
        {
            Assert.Contains(option, stdout, StringComparison.Ordinal);
        }

        Assert.Contains("not a survey", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutHelpTextDoesNotMentionOpenTopography()
    {
        CliHost host = CreateOfflineHost();

        (int exitCode, string stdout, _) = await RunAsync(host, ["help", "parcel"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain("OpenTopography", stdout, StringComparison.Ordinal);
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> RouteChainedResponse(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;
        if (path.Contains("onelineaddress", StringComparison.Ordinal))
        {
            return TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json"));
        }

        if (path.Contains("geographies/coordinates", StringComparison.Ordinal))
        {
            return TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-point-hit-synthetic.json"));
        }

        return TextResponse(HttpStatusCode.OK, ReadFixture("county-parcel-registry-point-hit-synthetic.json"));
    }

    /// <summary>Routes the address geocode request to the multi-candidate fixture and every other request (the FeatureServer query, since an explicit --geoid skips the Census county lookup) to a canned single-candidate registry response.</summary>
    private static Task<HttpResponseMessage> RouteMultiCandidateGeocodeThenRegistry(HttpRequestMessage request)
    {
        string path = request.RequestUri!.AbsolutePath;
        return path.Contains("onelineaddress", StringComparison.Ordinal)
            ? TextResponse(HttpStatusCode.OK, ReadFixture("census-multi-candidate-synthetic.json"))
            : TextResponse(HttpStatusCode.OK, ReadFixture("county-parcel-registry-point-hit-synthetic.json"));
    }

    private static string LocalFileFixturePath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "local-parcel-file-standard-schema-synthetic.geojson");

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static string WriteTempRegistry(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-parcel-command-test-registry-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string WriteTempLocalParcelFile(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-parcel-command-test-local-file-{Guid.NewGuid():N}.geojson");
        File.WriteAllText(path, contents);
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

    private static CliHost CreateHost(FakeHttpMessageHandler handler, Func<string, string?> getEnvironmentVariable) => new(
        getEnvironmentVariable,
        () => handler,
        new StringWriter(),
        new StringWriter());

    private static CliHost CreateOfflineHost() => new(
        _ => null,
        () => throw new InvalidOperationException("This test must never perform an HTTP call."),
        new StringWriter(),
        new StringWriter());

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(CliHost host, string[] args, CancellationToken cancellationToken)
    {
        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, host.StandardOutput.ToString() ?? string.Empty, host.StandardError.ToString() ?? string.Empty);
    }
}
