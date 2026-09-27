using System.Net;
using System.Text;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

/// <summary>
/// Contract tests for <see cref="AutoGeoidCountyParcelSource"/> (SolidGround Issue #31, PH3-4), mirroring
/// <see cref="CountyParcelRegistrySourceTests"/>'s and <see cref="CensusCountyLookupTests"/>'s own
/// <see cref="FakeHttpMessageHandler"/> style. The registry document is never a committed fixture (its
/// <c>serviceBaseUrl</c> is inherently <c>https://</c>, which <c>FixtureSecurityTests</c> bans under
/// <c>Fixtures/</c>): every test here builds it as an inline JSON literal and writes it to a uniquely named
/// temporary file, deleted in a <c>finally</c> block.
/// </summary>
public sealed class AutoGeoidCountyParcelSourceTests
{
    private const string CensusEndpointHost = "geocoding.geo.census.gov";
    private const string CountyEndpointHost = "parcels.example-county.invalid";

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
                "subdivision": null, "lot": null, "block": null, "plat": null, "book": null, "page": null,
                "legalDescription": null, "reportedAcres": null, "zoning": null, "stableParcelId": null
              },
              "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: this data is provided \"as is\" for testing only, with no warranty of any kind, express or implied, and is not an official record of any government entity."
            }
          ]
        }
        """;

    // Ring coordinates and winding copied verbatim from the committed
    // county-parcel-registry-point-hit-synthetic.json fixture (CountyParcelRegistrySourceTests' own reference):
    // EsriJsonPolygonReader requires a clockwise-wound outer ring, and this ring is already confirmed to parse.
    private const string CountyFeaturesBody = """
        {
          "features": [
            {
              "attributes": { "PARCEL_ID": "99-999-000", "SITUS_ADDR": "100 Example Loop" },
              "geometry": {
                "rings": [
                  [
                    [-93.604044272, 41.591012685],
                    [-93.604047631, 41.591372958],
                    [-93.603567727, 41.591375478],
                    [-93.603564371, 41.591015206],
                    [-93.604044272, 41.591012685]
                  ]
                ]
              }
            }
          ]
        }
        """;

    private const string CensusGeoidHitBody = """{"result":{"geographies":{"Counties":[{"GEOID":"99999"}]}}}""";
    private const string CensusNoCountyBody = """{"result":{"geographies":{"Counties":[]}}}""";

    [Fact]
    public async Task ConfiguredOverrideSkipsTheCensusLookupAndQueriesTheCountyRegistryDirectly()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => TextResponse(HttpStatusCode.OK, CountyFeaturesBody));
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: "99999");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            HttpRequestMessage sent = Assert.Single(handler.Requests);
            Assert.Equal(CountyEndpointHost, sent.RequestUri!.Host);
            Assert.Single(acquisition.Candidates);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task BlankOverrideResolvesTheGeoidFromTheCensusCountyLookupThenQueriesTheRegistry()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host switch
            {
                CensusEndpointHost => TextResponse(HttpStatusCode.OK, CensusGeoidHitBody),
                CountyEndpointHost => TextResponse(HttpStatusCode.OK, CountyFeaturesBody),
                _ => throw new InvalidOperationException($"Unexpected request host '{request.RequestUri!.Host}'."),
            });
            using var httpClient = new HttpClient(handler);
            // Blank, not null: RevitAddressAndParcelSettings.CountyGeoidOverride's own documented contract
            // treats a blank string the same as an absent override.
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: "  ");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(CensusEndpointHost, handler.Requests[0].RequestUri!.Host);
            Assert.Equal(CountyEndpointHost, handler.Requests[1].RequestUri!.Host);
            Assert.Single(acquisition.Candidates);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task BlankOverrideResolvesTheGeoidFromTheCensusCountyLookupForANearbyQueryToo()
    {
        // Mirrors BlankOverrideResolvesTheGeoidFromTheCensusCountyLookupThenQueriesTheRegistry above, but for
        // a ParcelNearbyQuery (SolidGround Issue #31 follow-up): the Revit dialog's own IParcelBoundarySource
        // is this type, and NearbyParcelBoundaryFinder's own tier 2 must work through it exactly like tier 1.
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host switch
            {
                CensusEndpointHost => TextResponse(HttpStatusCode.OK, CensusGeoidHitBody),
                CountyEndpointHost => TextResponse(HttpStatusCode.OK, CountyFeaturesBody),
                _ => throw new InvalidOperationException($"Unexpected request host '{request.RequestUri!.Host}'."),
            });
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: "  ");

            ParcelBoundaryAcquisition acquisition = await source.FindAsync(
                new ParcelNearbyQuery(41.591194d, -93.603806d, 30d), TestContext.Current.CancellationToken);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(CensusEndpointHost, handler.Requests[0].RequestUri!.Host);
            Assert.Equal(CountyEndpointHost, handler.Requests[1].RequestUri!.Host);
            Assert.Single(acquisition.Candidates);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task BlankOverrideReusesTheResolvedGeoidForASubsequentNearbyQueryAtTheIdenticalPoint()
    {
        // Mirrors the exact sequence NearbyParcelBoundaryFinder itself performs when tier 1 (a
        // ParcelPointQuery) returns zero candidates: it calls FindAsync again on the same IParcelBoundarySource
        // instance with a ParcelNearbyQuery for the identical coordinates. A blank geoidOverride must resolve
        // the county GEOID from Census only once in total across both calls, not once per tier.
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host switch
            {
                CensusEndpointHost => TextResponse(HttpStatusCode.OK, CensusGeoidHitBody),
                CountyEndpointHost => TextResponse(HttpStatusCode.OK, CountyFeaturesBody),
                _ => throw new InvalidOperationException($"Unexpected request host '{request.RequestUri!.Host}'."),
            });
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: "  ");

            await source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken);
            await source.FindAsync(new ParcelNearbyQuery(41.591194d, -93.603806d, 30d), TestContext.Current.CancellationToken);

            Assert.Equal(1, handler.Requests.Count(request => request.RequestUri!.Host == CensusEndpointHost));
            Assert.Equal(2, handler.Requests.Count(request => request.RequestUri!.Host == CountyEndpointHost));
            Assert.Equal(3, handler.Requests.Count);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task NoOverrideAndAnAddressQueryThrowsBeforeAnyNetworkCall()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: null);

            await Assert.ThrowsAsync<AutoGeoidCountyParcelSourceException>(
                () => source.FindAsync(new ParcelAddressQuery("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

            Assert.Empty(handler.Requests);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task ACensusCountyLookupFailureIsWrappedNotRethrownRaw()
    {
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host switch
            {
                CensusEndpointHost => TextResponse(HttpStatusCode.OK, CensusNoCountyBody),
                _ => throw new InvalidOperationException($"Unexpected request host '{request.RequestUri!.Host}'."),
            });
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: null);

            AutoGeoidCountyParcelSourceException error = await Assert.ThrowsAsync<AutoGeoidCountyParcelSourceException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), TestContext.Current.CancellationToken).AsTask());

            Assert.IsType<CensusCountyLookupNoCountyException>(error.InnerException);
            Assert.Single(handler.Requests);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    [Fact]
    public async Task ACensusNetworkTimeoutIsWrappedNotRethrownRaw()
    {
        // Mirrors CountyParcelRegistrySourceTests' own "general OperationCanceledException branch (a simulated
        // timeout)" pattern: the handler itself throws TaskCanceledException unconditionally, with the caller's
        // own token never cancelled (CancellationToken.None), so CensusCountyLookup.SendGetAsync's
        // "when (cancellationToken.IsCancellationRequested)" guard does not match, falling into its own
        // unconditional catch, which wraps the timeout as CensusCountyLookupNetworkException -- a
        // CensusCountyLookupException this source's own catch clause must then wrap in turn, not rethrow raw.
        string registryPath = WriteTempRegistry(RegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath);
            var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host == CensusEndpointHost
                ? throw new TaskCanceledException($"Simulated timeout while requesting {request.RequestUri}")
                : throw new InvalidOperationException($"Unexpected request host '{request.RequestUri!.Host}'."));
            using var httpClient = new HttpClient(handler);
            var source = new AutoGeoidCountyParcelSource(httpClient, registry, geoidOverride: null);

            AutoGeoidCountyParcelSourceException error = await Assert.ThrowsAsync<AutoGeoidCountyParcelSourceException>(
                () => source.FindAsync(new ParcelPointQuery(41.591194d, -93.603806d), CancellationToken.None).AsTask());

            Assert.IsType<CensusCountyLookupNetworkException>(error.InnerException);
        }
        finally
        {
            File.Delete(registryPath);
        }
    }

    private static string WriteTempRegistry(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-auto-geoid-source-test-{Guid.NewGuid():N}.json");
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
