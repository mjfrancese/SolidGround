using System.Net;
using System.Text;
using SolidGround.Core.Sources.Census;

namespace SolidGround.Tests;

/// <summary>
/// Contract tests for <see cref="CensusCountyLookup"/> against <see cref="FakeHttpMessageHandler"/>, mirroring
/// <see cref="CensusGeocoderTests"/>'s own request-shape/parsing/classification style. See
/// docs/architecture/census-county-lookup.md.
/// </summary>
public sealed class CensusCountyLookupTests
{
    [Fact]
    public async Task SendsExactlyOneRequestWithTheDocumentedQueryParameters()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-point-hit-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string query = sent.RequestUri!.Query.TrimStart('?');
        Assert.Equal("x=-93.603806&y=41.591194&benchmark=Public_AR_Current&vintage=Current_Current&layers=Counties&format=json", query);
    }

    [Fact]
    public async Task NeverSendsAnAuthorizationHeader()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-point-hit-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Null(sent.Headers.Authorization);
    }

    [Fact]
    public async Task ParsesTheGeoidFromTheCountiesLayerFixture()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-point-hit-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        string geoid = await lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken);

        Assert.Equal("99999", geoid);
    }

    [Fact]
    public async Task EmptyGeographiesThrowsANoCountyException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-county-lookup-nomatch-synthetic.json")));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupNoCountyException>(
            () => lookup.FindCountyGeoidAsync(0d, 0d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task MissingCountiesKeyWithNonEmptyGeographiesThrowsANoCountyException()
    {
        const string body = """{"result":{"geographies":{"States":[{"GEOID":"99"}]}}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupNoCountyException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"result":null}""")]
    [InlineData("""{"result":{}}""")]
    [InlineData("""{"result":{"geographies":"not-an-object"}}""")]
    public async Task MissingResultOrGeographiesThrowsAnUnexpectedResponseException(string body)
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupUnexpectedResponseException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task FiveXxStatusThrowsAServerException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.InternalServerError, string.Empty));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        CensusCountyLookupServerException error = await Assert.ThrowsAsync<CensusCountyLookupServerException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task TransportFailureThrowsANetworkException()
    {
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) => throw new HttpRequestException("boom"));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupNetworkException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task MalformedJsonThrowsAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "not json"));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupUnexpectedResponseException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task GeoidNotExactlyFiveDigitsThrowsAnUnexpectedResponseException()
    {
        const string body = """{"result":{"geographies":{"Counties":[{"GEOID":"123"}]}}}""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<CensusCountyLookupUnexpectedResponseException>(
            () => lookup.FindCountyGeoidAsync(41.591194d, -93.603806d, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task RejectsInvalidLatitudeOrLongitudeBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        var lookup = new CensusCountyLookup(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => lookup.FindCountyGeoidAsync(999d, -93.603806d, TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
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
