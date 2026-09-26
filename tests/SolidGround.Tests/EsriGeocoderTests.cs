using System.Net;
using System.Text;
using SolidGround.Core.Http;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Esri;

namespace SolidGround.Tests;

/// <summary>Contract tests against <see cref="FakeHttpMessageHandler"/>. Uses <see cref="StaticEsriApiKeyProvider"/> exclusively -- no real environment access.</summary>
public sealed class EsriGeocoderTests
{
    private const string FakeKey = "fixture-fake-esri-key-0123456789";

    [Fact]
    public async Task SendsExactlyOneRequestWithTheDocumentedQueryParametersForStorageTrueAndBearerHeader()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("esri-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string query = sent.RequestUri!.Query.TrimStart('?');
        Assert.Equal(
            $"f=json&SingleLine={Uri.EscapeDataString("100 Example Loop")}&forStorage=true&outFields=Addr_type&maxLocations=5",
            query);
        Assert.NotNull(sent.Headers.Authorization);
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(FakeKey, sent.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ParsesCandidatesSortingByScoreDescendingRegardlessOfServerOrder()
    {
        // The fixture deliberately lists score 86.4 before 100 (wire order); this proves the sort, not just
        // that the parser preserves whatever order it is given.
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("esri-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        Assert.Equal(2, acquisition.Candidates.Count);
        Assert.Equal(100d, acquisition.Candidates[0].Score);
        Assert.Equal("PointAddress", acquisition.Candidates[0].PrecisionLabel);
        Assert.Equal(EsriGeocoder.AttributionNotice, acquisition.Candidates[0].Attribution);
        // Esri's own field names are the reversed-sounding x=longitude/y=latitude; these assertions catch a
        // coordinate-order swap that Score/PrecisionLabel/Attribution assertions alone cannot.
        Assert.Equal(0.0001d, acquisition.Candidates[0].Latitude);
        Assert.Equal(-0.0001d, acquisition.Candidates[0].Longitude);
        Assert.Equal("100 Example Loop, Fixtonville, New Sandbox, 00000", acquisition.Candidates[0].MatchedAddress);
        Assert.Equal(86.4d, acquisition.Candidates[1].Score);
        Assert.Equal("StreetAddress", acquisition.Candidates[1].PrecisionLabel);
        Assert.Equal(EsriGeocoder.AttributionNotice, acquisition.Candidates[1].Attribution);
        Assert.Equal(0.0002d, acquisition.Candidates[1].Latitude);
        Assert.Equal(-0.0002d, acquisition.Candidates[1].Longitude);
        Assert.Equal("202 Example Loop, Fixtonville, New Sandbox, 00000", acquisition.Candidates[1].MatchedAddress);
    }

    [Fact]
    public async Task BlankAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"candidates":[{"address":"","location":{"x":-0.0001,"y":0.0001},"score":100,"attributes":{"Addr_type":"PointAddress"}}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task BlankAddrTypeFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"candidates":[{"address":"100 Example Loop","location":{"x":-0.0001,"y":0.0001},"score":100,"attributes":{"Addr_type":""}}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WhitespaceOnlyAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"candidates":[{"address":"   ","location":{"x":-0.0001,"y":0.0001},"score":100,"attributes":{"Addr_type":"PointAddress"}}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task OutOfRangeCoordinatesFailAsAnUnexpectedResponseExceptionRatherThanARawArgumentOutOfRangeException()
    {
        const string body = """{"candidates":[{"address":"100 Example Loop","location":{"x":-0.0001,"y":950.0},"score":100,"attributes":{"Addr_type":"PointAddress"}}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task MissingApiKeyThrowsAnAuthorizationExceptionBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        var geocoder = new EsriGeocoder(httpClient, new StaticEsriApiKeyProvider(null));

        EsriGeocoderAuthorizationException error = await Assert.ThrowsAsync<EsriGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Equal(EsriGeocoderAuthorizationFailure.ApiKeyMissing, error.Failure);
        Assert.Null(error.ErrorCode);
    }

    [Fact]
    public async Task EmptyCandidatesThrowsANoCandidatesException()
    {
        const string body = """{"spatialReference":{"wkid":4326},"candidates":[]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderNoCandidatesException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task AnErrorObjectOnHttp200Returns403AsAnAuthorizationException()
    {
        const string body = """{"error":{"code":403,"message":"Token lacks storage privilege.","details":[]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderAuthorizationException error = await Assert.ThrowsAsync<EsriGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(EsriGeocoderAuthorizationFailure.InsufficientPrivilege, error.Failure);
        Assert.Equal(403, error.ErrorCode);
    }

    [Fact]
    public async Task A499ErrorCodeInTheBodyIsAnAuthorizationExceptionForTokenRequired()
    {
        const string body = """{"error":{"code":499,"message":"Token Required","details":[]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderAuthorizationException error = await Assert.ThrowsAsync<EsriGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(EsriGeocoderAuthorizationFailure.TokenRequired, error.Failure);
        Assert.Equal(499, error.ErrorCode);
    }

    [Fact]
    public async Task A400ErrorCodeInTheBodyIsARequestValidationException()
    {
        const string body = """{"error":{"code":400,"message":"Unable to complete operation.","details":["'singleLine' parameter is missing."]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderRequestValidationException error = await Assert.ThrowsAsync<EsriGeocoderRequestValidationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(400, error.ErrorCode);
    }

    [Fact]
    public async Task A500ErrorCodeInTheBodyIsAServerException()
    {
        const string body = """{"error":{"code":500,"message":"Internal server error.","details":[]}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderServerException error = await Assert.ThrowsAsync<EsriGeocoderServerException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(500, error.ErrorCode);
        Assert.Null(error.TransportStatusCode);
    }

    [Fact]
    public async Task MalformedJsonBodyFailsAsAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "not json"));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<EsriGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task NeverIncludesTheApiKeyInTheRequestUriQueryString()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("esri-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.DoesNotContain(FakeKey, sent.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactsTheApiKeyFromAnExceptionMessageEvenIfEchoedInAServerBody()
    {
        string body = $"{{\"error\":{{\"code\":403,\"message\":\"Invalid token: {FakeKey}\",\"details\":[]}}}}";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderAuthorizationException error = await Assert.ThrowsAsync<EsriGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.ServerMessage);
        Assert.DoesNotContain(FakeKey, error.ServerMessage, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", error.ServerMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrapsATransportFailureAsANetworkExceptionWithoutLeakingTheKeyThroughTheInnerExceptionChain()
    {
        // Regression test: a caller-attached DelegatingHandler could realistically build an exception whose
        // own Message embeds the Authorization header value; EsriGeocoder.SendGetAsync must never attach such
        // an exception verbatim as InnerException (see that method's own remarks).
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) =>
            throw new HttpRequestException($"Connection dropped while sending Authorization: Bearer {FakeKey}"));
        using var httpClient = new HttpClient(handler);
        EsriGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        EsriGeocoderNetworkException error = await Assert.ThrowsAsync<EsriGeocoderNetworkException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        AssertExceptionChainDoesNotContainTheKey(error);
    }

    private static EsriGeocoder CreateGeocoder(HttpClient httpClient, string apiKeyValue, EsriGeocoderOptions? options = null) =>
        new(httpClient, new StaticEsriApiKeyProvider(new ApiKey(apiKeyValue)), options);

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

    private static void AssertExceptionChainDoesNotContainTheKey(Exception exception, string key = FakeKey)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(key, current.ToString(), StringComparison.Ordinal);
        }
    }
}
