using System.Net;
using System.Text;
using SolidGround.Core.Http;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;

namespace SolidGround.Tests;

public sealed class CensusGeocoderTests
{
    [Fact]
    public async Task SendsExactlyOneRequestWithTheDocumentedQueryParameters()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string query = sent.RequestUri!.Query.TrimStart('?');
        string expectedAddress = Uri.EscapeDataString("100 Example Loop");
        Assert.Equal($"address={expectedAddress}&benchmark=Public_AR_Current&format=json", query);
    }

    [Fact]
    public async Task NeverSendsAnAuthorizationHeader()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Null(sent.Headers.Authorization);
    }

    [Fact]
    public async Task ParsesASingleCandidateFromTheExampleSiteFixtureWithCoordinatesMatchedAddressAndAttribution()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        AddressGeocodeCandidate candidate = Assert.Single(acquisition.Candidates);
        Assert.Equal(41.591194, candidate.Latitude);
        Assert.Equal(-93.603806, candidate.Longitude);
        Assert.Equal("100 EXAMPLE LOOP, TESTSITE, ZZ, 00000", candidate.MatchedAddress);
        Assert.Equal(CensusGeocoder.AttributionNotice, candidate.Attribution);
        Assert.Null(candidate.Score);
        Assert.Null(candidate.PrecisionLabel);
    }

    [Fact]
    public async Task ParsesMultipleCandidatesPreservingWireOrderAsTheRankingSignal()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-multi-candidate-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        Assert.Equal(3, acquisition.Candidates.Count);
        Assert.Equal("100 EXAMPLE LOOP, TESTSITE, ZZ, 00000", acquisition.Candidates[0].MatchedAddress);
        Assert.Equal("100 EXAMPLE LOOP, OTHERSITE, YY, 00000", acquisition.Candidates[1].MatchedAddress);
        Assert.Equal("100 EXAMPLE LOOP, THIRDSITE, XX, 00000", acquisition.Candidates[2].MatchedAddress);
        Assert.All(acquisition.Candidates, candidate =>
        {
            Assert.Null(candidate.Score);
            Assert.Null(candidate.PrecisionLabel);
        });
    }

    [Fact]
    public async Task EmptyAddressMatchesThrowsANoCandidatesException()
    {
        const string body = """{"result":{"addressMatches":[]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderNoCandidatesException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Returns400ForAnInvalidBenchmarkAsARequestValidationException()
    {
        const string body = """{"errors":["Invalid benchmark in request"],"status":"400"}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.BadRequest, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        CensusGeocoderRequestValidationException error = await Assert.ThrowsAsync<CensusGeocoderRequestValidationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.NotNull(error.ServerMessage);
        Assert.Contains("Invalid benchmark in request", error.ServerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnAddressLongerThanTheConfiguredMaximumBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);
        string longAddress = new('A', 101);

        CensusGeocoderRequestValidationException error = await Assert.ThrowsAsync<CensusGeocoderRequestValidationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest(longAddress), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Null(error.StatusCode);
        Assert.Null(error.ServerMessage);
    }

    [Fact]
    public async Task BlankMatchedAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"result":{"addressMatches":[{"matchedAddress":"","coordinates":{"x":-0.0001,"y":0.0001}}]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WhitespaceOnlyMatchedAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"result":{"addressMatches":[{"matchedAddress":"   ","coordinates":{"x":-0.0001,"y":0.0001}}]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task OutOfRangeCoordinatesFailAsAnUnexpectedResponseExceptionRatherThanARawArgumentOutOfRangeException()
    {
        const string body = """{"result":{"addressMatches":[{"matchedAddress":"100 EXAMPLE LOOP, TESTSITE, ZZ, 00000","coordinates":{"x":-0.0001,"y":950.0}}]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task RedactsATokenShapedValueFromAnExceptionMessageEvenThoughCensusSendsNoApiKeyOfItsOwn()
    {
        const string body = """{"errors":["Invalid request: token=super-secret-fake-value-0123456789 was rejected"],"status":"400"}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.BadRequest, body));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        CensusGeocoderRequestValidationException error = await Assert.ThrowsAsync<CensusGeocoderRequestValidationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(error.ServerMessage);
        Assert.DoesNotContain("super-secret-fake-value-0123456789", error.ServerMessage, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-fake-value-0123456789", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJsonBodyFailsAsAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "not json"));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task AnUndocumentedServerErrorStatusFailsAsAServerException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        CensusGeocoderServerException error = await Assert.ThrowsAsync<CensusGeocoderServerException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task WrapsATransportFailureAsANetworkException()
    {
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) => throw new HttpRequestException("boom"));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);

        await Assert.ThrowsAsync<CensusGeocoderNetworkException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ComputesRedactedRequestUriByDelegatingToTheSharedHelperRatherThanReimplementingRedaction()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
        using var httpClient = new HttpClient(handler);
        var geocoder = new CensusGeocoder(httpClient);
        const string address = "100 Example Loop";

        CensusGeocoderServerException error = await Assert.ThrowsAsync<CensusGeocoderServerException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest(address), TestContext.Current.CancellationToken).AsTask());

        var expectedUri = new Uri(
            $"{CensusGeocoderOptions.DefaultEndpointUri}?address={Uri.EscapeDataString(address)}&benchmark={CensusGeocoderOptions.DefaultBenchmark}&format=json",
            UriKind.Absolute);
        string expectedRedacted = SensitiveQueryRedactor.RedactUri(expectedUri, SensitiveQueryParameterNames.KnownFamilies);
        Assert.Equal(expectedRedacted, error.RedactedRequestUri);
    }

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static Task<HttpResponseMessage> TextResponse(HttpStatusCode statusCode, string body)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
