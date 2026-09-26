using System.Net;
using System.Text;
using System.Text.Json;
using SolidGround.Cli;

namespace SolidGround.Tests;

/// <summary>
/// The CLI's `geocode` verb: deterministic JSON output (AC1), missing-key/no-candidate/validation/server
/// classification before or after the one HTTP call each provider makes (AC2), and help text. See
/// docs/architecture/cli-workflow.md's "### geocode" subsection.
/// </summary>
public sealed class GeocodeCommandTests
{
    private const string FakeKey = "fixture-fake-key-0123456789";

    [Fact]
    public async Task DefaultProviderIsCensusAndProducesTheDocumentedJsonShape()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, string stdout, string stderr) = await RunAsync(host, ["geocode", "--address", "100 Example Loop"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);

        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement root = document.RootElement;
        Assert.Equal("Census", root.GetProperty("provider").GetString());
        Assert.Equal("100 Example Loop", root.GetProperty("requestedAddress").GetString());
        Assert.Equal(1, root.GetProperty("candidateCount").GetInt32());

        JsonElement candidate = Assert.Single(root.GetProperty("candidates").EnumerateArray());
        Assert.Equal(1, candidate.GetProperty("index").GetInt32());
        Assert.Equal(41.591194, candidate.GetProperty("latitude").GetDouble());
        Assert.Equal(-93.603806, candidate.GetProperty("longitude").GetDouble());
        Assert.Equal("100 EXAMPLE LOOP, TESTSITE, ZZ, 00000", candidate.GetProperty("matchedAddress").GetString());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("precisionLabel").ValueKind);
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("score").ValueKind);
        Assert.Contains("Census Bureau", candidate.GetProperty("attribution").GetString(), StringComparison.Ordinal);
        Assert.Contains("not a survey-grade measurement", candidate.GetProperty("accuracyLabel").GetString(), StringComparison.Ordinal);

        // Fixed top-level and per-candidate property order (docs/architecture/cli-workflow.md's "### geocode"
        // subsection), checked positionally on the raw text rather than by embedding a hand-written literal
        // (which would risk a spurious CRLF/LF mismatch).
        AssertAppearsBefore(stdout, "\"provider\"", "\"requestedAddress\"");
        AssertAppearsBefore(stdout, "\"requestedAddress\"", "\"candidateCount\"");
        AssertAppearsBefore(stdout, "\"candidateCount\"", "\"candidates\"");
        AssertAppearsBefore(stdout, "\"index\"", "\"latitude\"");
        AssertAppearsBefore(stdout, "\"latitude\"", "\"longitude\"");
        AssertAppearsBefore(stdout, "\"longitude\"", "\"matchedAddress\"");
        AssertAppearsBefore(stdout, "\"matchedAddress\"", "\"precisionLabel\"");
        AssertAppearsBefore(stdout, "\"precisionLabel\"", "\"score\"");
        AssertAppearsBefore(stdout, "\"score\"", "\"attribution\"");
        AssertAppearsBefore(stdout, "\"attribution\"", "\"accuracyLabel\"");

        Assert.EndsWith("\n", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningTwiceProducesByteIdenticalStdout()
    {
        string[] args = ["geocode", "--address", "100 Example Loop"];
        FakeHttpMessageHandler firstHandler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        FakeHttpMessageHandler secondHandler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));

        (int firstExitCode, string firstStdout, _) = await RunAsync(CreateHost(firstHandler, _ => null), args, TestContext.Current.CancellationToken);
        (int secondExitCode, string secondStdout, _) = await RunAsync(CreateHost(secondHandler, _ => null), args, TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, firstExitCode);
        Assert.Equal(CliExitCodes.Success, secondExitCode);
        Assert.Equal(firstStdout, secondStdout);
    }

    [Fact]
    public async Task OutputFileIsByteIdenticalToStdout()
    {
        string outputFile = Path.Combine(Path.GetTempPath(), $"solidground-geocode-output-{Guid.NewGuid():N}.json");
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host, ["geocode", "--address", "100 Example Loop", "--output-file", outputFile], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            byte[] fileBytes = File.ReadAllBytes(outputFile);
            Assert.Equal(Encoding.UTF8.GetBytes(stdout), fileBytes);

            // No BOM: the first three bytes are not EF BB BF.
            Assert.False(fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF);
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task OutputFileInAMissingDirectoryExitsProcessing()
    {
        // File.WriteAllTextAsync's parent directory is never created for --output-file (unlike parcel
        // --output, which calls Directory.CreateDirectory first) -- a missing parent throws
        // DirectoryNotFoundException (an IOException subclass), which must be wrapped as
        // CliProcessingException (exit 5, "processing") rather than falling through to the generic exit-1
        // "unexpected" handler.
        string outputFile = Path.Combine(Path.GetTempPath(), $"solidground-geocode-missing-dir-{Guid.NewGuid():N}", "output.json");
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-example-site-synthetic.json")));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--output-file", outputFile], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Processing, exitCode);
        Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
        Assert.Contains(outputFile, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiCandidateFixturePreservesWireOrderAndIndexesFromOne()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("census-multi-candidate-synthetic.json")));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, string stdout, _) = await RunAsync(host, ["geocode", "--address", "100 Example Loop"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        JsonElement[] candidates = [.. document.RootElement.GetProperty("candidates").EnumerateArray()];
        Assert.Equal(3, candidates.Length);
        Assert.Equal(1, candidates[0].GetProperty("index").GetInt32());
        Assert.Equal(2, candidates[1].GetProperty("index").GetInt32());
        Assert.Equal(3, candidates[2].GetProperty("index").GetInt32());
        Assert.Equal("100 EXAMPLE LOOP, TESTSITE, ZZ, 00000", candidates[0].GetProperty("matchedAddress").GetString());
        Assert.Equal("100 EXAMPLE LOOP, OTHERSITE, YY, 00000", candidates[1].GetProperty("matchedAddress").GetString());
        Assert.Equal("100 EXAMPLE LOOP, THIRDSITE, XX, 00000", candidates[2].GetProperty("matchedAddress").GetString());
    }

    [Fact]
    public async Task GeocodioWithNoKeyConfiguredExitsAuthorizationWithZeroRequests()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, string.Empty));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", "geocodio"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Authorization, exitCode);
        Assert.Empty(handler.Requests);
        Assert.Contains("GEOCODIO_API_KEY", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EsriWithNoKeyConfiguredExitsAuthorizationWithZeroRequests()
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, string.Empty));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", "esri"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Authorization, exitCode);
        Assert.Empty(handler.Requests);
        Assert.Contains("ARCGIS_API_KEY", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeocodioWithKeyConfiguredSendsExactlyOneRequest()
    {
        const string body = """{"results":[{"formatted_address":"100 EXAMPLE LOOP, TESTSITE, ZZ, 00000","location":{"lat":41.591194,"lng":-93.603806},"accuracy":1,"accuracy_type":"rooftop","source":"SYNTHETIC FIXTURE"}]}""";
        FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.OK, body));
        CliHost host = CreateHost(handler, name => name == "GEOCODIO_API_KEY" ? FakeKey : null);

        (int exitCode, string stdout, _) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", "geocodio"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(FakeKey, stdout, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal("Geocodio", document.RootElement.GetProperty("provider").GetString());
    }

    [Theory]
    [InlineData("census", null, """{"result":{"addressMatches":[]}}""", (int)HttpStatusCode.OK)]
    [InlineData("geocodio", "GEOCODIO_API_KEY", """{"results":[]}""", (int)HttpStatusCode.OK)]
    [InlineData("esri", "ARCGIS_API_KEY", """{"spatialReference":{"wkid":4326},"candidates":[]}""", (int)HttpStatusCode.OK)]
    public async Task EachProviderNoCandidatesExitsNotFound(string provider, string? keyEnvironmentVariableName, string body, int statusCode)
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse((HttpStatusCode)statusCode, body));
        CliHost host = CreateHost(handler, name => keyEnvironmentVariableName is not null && name == keyEnvironmentVariableName ? FakeKey : null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", provider], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.NotFound, exitCode);
        Assert.Contains("error (not-found):", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("census", null, """{"errors":["Invalid benchmark in request"],"status":"400"}""", (int)HttpStatusCode.BadRequest)]
    [InlineData("geocodio", "GEOCODIO_API_KEY", """{"error":"The address given is ambiguous."}""", (int)HttpStatusCode.UnprocessableEntity)]
    [InlineData("esri", "ARCGIS_API_KEY", """{"error":{"code":400,"message":"Unable to complete operation.","details":["'singleLine' parameter is missing."]}}""", (int)HttpStatusCode.OK)]
    public async Task EachProviderRequestValidationExitsUsage(string provider, string? keyEnvironmentVariableName, string body, int statusCode)
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse((HttpStatusCode)statusCode, body));
        CliHost host = CreateHost(handler, name => keyEnvironmentVariableName is not null && name == keyEnvironmentVariableName ? FakeKey : null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", provider], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("census", null, "", (int)HttpStatusCode.InternalServerError)]
    [InlineData("geocodio", "GEOCODIO_API_KEY", "", (int)HttpStatusCode.InternalServerError)]
    [InlineData("esri", "ARCGIS_API_KEY", """{"error":{"code":500,"message":"Internal server error.","details":[]}}""", (int)HttpStatusCode.OK)]
    public async Task EachProviderServerErrorExitsSourceQuality(string provider, string? keyEnvironmentVariableName, string body, int statusCode)
    {
        FakeHttpMessageHandler handler = new((_, _) => TextResponse((HttpStatusCode)statusCode, body));
        CliHost host = CreateHost(handler, name => keyEnvironmentVariableName is not null && name == keyEnvironmentVariableName ? FakeKey : null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", provider], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.SourceQuality, exitCode);
        Assert.Contains("error (source-quality):", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpTextListsAllOptionsAndTheApproximateNotSurveyWording()
    {
        CliHost host = CreateHost(new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("help must never perform an HTTP call.")), _ => null);

        (int exitCode, string stdout, _) = await RunAsync(host, ["help", "geocode"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("--address", stdout, StringComparison.Ordinal);
        Assert.Contains("--provider", stdout, StringComparison.Ordinal);
        Assert.Contains("--output-file", stdout, StringComparison.Ordinal);
        Assert.Contains("--timeout", stdout, StringComparison.Ordinal);
        Assert.Contains("not survey-grade", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutHelpTextDoesNotMentionOpenTopography()
    {
        CliHost host = CreateHost(new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("help must never perform an HTTP call.")), _ => null);

        (int exitCode, string stdout, _) = await RunAsync(host, ["help", "geocode"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.DoesNotContain("OpenTopography", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAddressExitsUsage()
    {
        FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("must not send a request"));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(host, ["geocode"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--address", stderr, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnknownProviderTokenExitsUsage()
    {
        FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("must not send a request"));
        CliHost host = CreateHost(handler, _ => null);

        (int exitCode, _, string stderr) = await RunAsync(
            host, ["geocode", "--address", "100 Example Loop", "--provider", "bogus"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("--provider", stderr, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static void AssertAppearsBefore(string text, string first, string second)
    {
        int firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        int secondIndex = text.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"'{first}' was not found.");
        Assert.True(secondIndex >= 0, $"'{second}' was not found.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' (at {firstIndex}) to appear before '{second}' (at {secondIndex}).");
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

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

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(CliHost host, string[] args, CancellationToken cancellationToken)
    {
        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, host.StandardOutput.ToString() ?? string.Empty, host.StandardError.ToString() ?? string.Empty);
    }
}
