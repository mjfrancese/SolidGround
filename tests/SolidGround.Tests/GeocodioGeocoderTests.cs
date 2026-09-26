using System.Net;
using System.Text;
using SolidGround.Core.Http;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Geocodio;

namespace SolidGround.Tests;

/// <summary>Contract tests against <see cref="FakeHttpMessageHandler"/>. Uses <see cref="StaticGeocodioApiKeyProvider"/> exclusively -- no real environment access.</summary>
public sealed class GeocodioGeocoderTests
{
    private const string FakeKey = "fixture-fake-geocodio-key-0123456789";

    [Fact]
    public async Task SendsExactlyOneRequestWithTheDocumentedQueryParametersAndBearerHeader()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("geocodio-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string query = sent.RequestUri!.Query.TrimStart('?');
        Assert.Equal($"q={Uri.EscapeDataString("100 Example Loop")}&limit=5", query);
        Assert.NotNull(sent.Headers.Authorization);
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal(FakeKey, sent.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ParsesCandidatesPreservingServerReturnedOrderWithAccuracyAsScoreAndAccuracyTypeAsPrecisionLabel()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("geocodio-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        Assert.Equal(2, acquisition.Candidates.Count);
        AddressGeocodeCandidate first = acquisition.Candidates[0];
        Assert.Equal("100 Example Loop, Testville, ZZ 00000", first.MatchedAddress);
        Assert.Equal(1d, first.Score);
        Assert.Equal("rooftop", first.PrecisionLabel);
        Assert.Contains("SYNTHETIC FIXTURE", first.Attribution, StringComparison.Ordinal);
        // The fixture's two candidates use distinct, sign-swap-sensitive lat/lng values specifically so a
        // coordinate-order regression in the lat/lng -> Latitude/Longitude mapping is caught here.
        Assert.Equal(0.0002d, first.Latitude);
        Assert.Equal(-0.0002d, first.Longitude);

        AddressGeocodeCandidate second = acquisition.Candidates[1];
        Assert.Equal("Testville, ZZ 00000", second.MatchedAddress);
        Assert.Equal(0.5d, second.Score);
        Assert.Equal("place", second.PrecisionLabel);
        Assert.Contains("SYNTHETIC FIXTURE", second.Attribution, StringComparison.Ordinal);
        Assert.Equal(0.0001d, second.Latitude);
        Assert.Equal(-0.0001d, second.Longitude);
    }

    [Fact]
    public async Task BlankFormattedAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"results":[{"formatted_address":"","location":{"lat":0.0001,"lng":-0.0001},"accuracy":1,"accuracy_type":"rooftop","source":"SYNTHETIC FIXTURE"}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task BlankAccuracyTypeFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"results":[{"formatted_address":"100 Example Loop","location":{"lat":0.0001,"lng":-0.0001},"accuracy":1,"accuracy_type":"","source":"SYNTHETIC FIXTURE"}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WhitespaceOnlyFormattedAddressFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"results":[{"formatted_address":"   ","location":{"lat":0.0001,"lng":-0.0001},"accuracy":1,"accuracy_type":"rooftop","source":"SYNTHETIC FIXTURE"}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WhitespaceOnlySourceFailsAsAnUnexpectedResponseExceptionRatherThanARawArgumentException()
    {
        const string body = """{"results":[{"formatted_address":"100 Example Loop","location":{"lat":0.0001,"lng":-0.0001},"accuracy":1,"accuracy_type":"rooftop","source":"   "}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task OutOfRangeCoordinatesFailAsAnUnexpectedResponseExceptionRatherThanARawArgumentOutOfRangeException()
    {
        const string body = """{"results":[{"formatted_address":"100 Example Loop","location":{"lat":950.0,"lng":-0.0001},"accuracy":1,"accuracy_type":"rooftop","source":"SYNTHETIC FIXTURE"}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task MissingApiKeyThrowsAnAuthorizationExceptionBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        var geocoder = new GeocodioGeocoder(httpClient, new StaticGeocodioApiKeyProvider(null));

        GeocodioGeocoderAuthorizationException error = await Assert.ThrowsAsync<GeocodioGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Equal(GeocodioGeocoderAuthorizationFailure.ApiKeyMissing, error.Failure);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task EmptyResultsThrowsANoCandidatesException()
    {
        const string body = """{"results":[]}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderNoCandidatesException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Returns403AsAnAuthorizationExceptionAfterSendingTheRequest()
    {
        const string body = """{"error":"Invalid API key, or other reason why access is forbidden."}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.Forbidden, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderAuthorizationException error = await Assert.ThrowsAsync<GeocodioGeocoderAuthorizationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(GeocodioGeocoderAuthorizationFailure.Rejected, error.Failure);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Returns422AsARequestValidationException()
    {
        const string body = """{"error":"The address given is ambiguous."}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.UnprocessableEntity, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderRequestValidationException error = await Assert.ThrowsAsync<GeocodioGeocoderRequestValidationException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
    }

    [Fact]
    public async Task Returns429AsAQuotaExceptionCapturingRateLimitHeaders()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"You have exceeded the rate limit."}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Limit", "2500");
            response.Headers.Add("X-RateLimit-Period", "day");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderQuotaException error = await Assert.ThrowsAsync<GeocodioGeocoderQuotaException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("0", error.RateLimitRemaining);
        Assert.Equal("2500", error.RateLimitLimit);
        Assert.Equal("day", error.RateLimitPeriod);
    }

    [Fact]
    public async Task Returns500AsAServerException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderServerException error = await Assert.ThrowsAsync<GeocodioGeocoderServerException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task MalformedJsonBodyFailsAsAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "not json"));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await Assert.ThrowsAsync<GeocodioGeocoderUnexpectedResponseException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task NeverIncludesTheApiKeyInTheRequestUriQueryString()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("geocodio-example-site-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        await geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.DoesNotContain(FakeKey, sent.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactsTheApiKeyFromAnExceptionMessageEvenIfEchoedInAServerBody()
    {
        string body = $$"""{"error":"Invalid API key: {{FakeKey}}"}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.Forbidden, body));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderAuthorizationException error = await Assert.ThrowsAsync<GeocodioGeocoderAuthorizationException>(
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
        // own Message embeds the Authorization header value; GeocodioGeocoder.SendGetAsync must never attach
        // such an exception verbatim as InnerException (see that method's own remarks).
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) =>
            throw new HttpRequestException($"Connection dropped while sending Authorization: Bearer {FakeKey}"));
        using var httpClient = new HttpClient(handler);
        GeocodioGeocoder geocoder = CreateGeocoder(httpClient, FakeKey);

        GeocodioGeocoderNetworkException error = await Assert.ThrowsAsync<GeocodioGeocoderNetworkException>(
            () => geocoder.GeocodeAsync(new AddressGeocodeRequest("100 Example Loop"), TestContext.Current.CancellationToken).AsTask());

        AssertExceptionChainDoesNotContainTheKey(error);
    }

    private static GeocodioGeocoder CreateGeocoder(HttpClient httpClient, string apiKeyValue, GeocodioGeocoderOptions? options = null) =>
        new(httpClient, new StaticGeocodioApiKeyProvider(new ApiKey(apiKeyValue)), options);

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
