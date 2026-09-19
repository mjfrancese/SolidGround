using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class OpenTopographyUsgs1mSourceTests
{
    private const string FakeKey = "fixture-fake-key-0123456789";

    [Fact]
    public async Task SendsExactlyOneRequestWithTheDocumentedQueryParameters()
    {
        var handler = new FakeHttpMessageHandler((_, _) => EmptyResponse(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, "fake-test-key-value");
        const double west = -90.5;
        const double south = 38.5;
        const double east = [withheld];
        const double north = 38.6;
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(west, south, east, north));

        await Assert.ThrowsAsync<OpenTopographyNoDataException>(() => source.AcquireAsync(request, TestContext.Current.CancellationToken).AsTask());

        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string query = sent.RequestUri!.Query.TrimStart('?');
        string expectedKey = Uri.EscapeDataString("fake-test-key-value");
        static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        Assert.Equal(
            $"datasetName=USGS1m&south={Format(south)}&north={Format(north)}&west={Format(west)}&east={Format(east)}&outputFormat=AAIGrid&API_Key={expectedKey}",
            query);
    }

    [Fact]
    public async Task RejectsNonBoundingBoxAoiBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);
        var request = new ElevationSourceRequest(new Wgs84RadiusAoi(38.7, [withheld], LinearDistance.Meters(100d)));

        OpenTopographyRequestValidationException error = await Assert.ThrowsAsync<OpenTopographyRequestValidationException>(
            () => source.AcquireAsync(request, TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Contains("bounding box", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsAnAreaAboveTheConfiguredLimitBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(-91d, 38d, -90d, 39d));

        OpenTopographyRequestValidationException error = await Assert.ThrowsAsync<OpenTopographyRequestValidationException>(
            () => source.AcquireAsync(request, TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Contains("square kilometers", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsAMissingApiKeyBeforeAnyRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("must not send a request"));
        using var httpClient = new HttpClient(handler);
        var source = new OpenTopographyUsgs1mSource(httpClient, new StaticOpenTopographyApiKeyProvider(null));

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(handler.Requests);
        Assert.Equal(OpenTopographyAuthorizationFailure.ApiKeyMissing, error.Failure);
        Assert.Null(error.StatusCode);
        Assert.Null(error.ServerMessage);
    }

    [Fact]
    public async Task Returns401WithAnEchoedKeyAsARedactedApiKeyRejectedException()
    {
        string body = $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><error>Error: Not a valid format API Key: {FakeKey}. Please register for an API key at www.opentopography.org</error>""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.Unauthorized, body, "application/xml"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(OpenTopographyAuthorizationFailure.ApiKeyRejected, error.Failure);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.NotNull(error.ServerMessage);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.RedactedRequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.ServerMessage, StringComparison.Ordinal);
        Assert.Contains("Not a valid format API Key", error.ServerMessage, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns401WithAKeyContainingAnAngleBracketStillFullyRedactsIt()
    {
        // Regression test: BuildServerMessage used to strip XML/HTML tags and collapse whitespace BEFORE
        // redacting the key. A key containing '<' let the tag-stripping regex ("<[^>]*>") treat everything
        // from that '<' through the response's own closing "</error>" as one tag, deleting the key's suffix
        // (and surrounding text) while leaving its prefix as ordinary text no longer contiguous with the
        // full raw key value RedactText looks for, so the prefix leaked through unredacted.
        const string keyWithAngleBracket = "KEYPREFIXVISIBLE<KEYSUFFIXHIDDEN";
        string body = $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><error>Error: Not a valid format API Key: {keyWithAngleBracket}. Please register for an API key at www.opentopography.org</error>""";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.Unauthorized, body, "application/xml"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, keyWithAngleBracket);

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(error.ServerMessage);
        Assert.DoesNotContain("KEYPREFIXVISIBLE", error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYSUFFIXHIDDEN", error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYPREFIXVISIBLE", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYSUFFIXHIDDEN", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(keyWithAngleBracket, error.RedactedRequestUri, StringComparison.Ordinal);
        Assert.Contains("Not a valid format API Key", error.ServerMessage, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error, keyWithAngleBracket);
        AssertExceptionChainDoesNotContainTheKey(error, "KEYPREFIXVISIBLE");
    }

    [Fact]
    public async Task Returns403AsADatasetAccessDeniedException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.Forbidden, "Forbidden"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(OpenTopographyAuthorizationFailure.DatasetAccessDenied, error.Failure);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns429AsAQuotaExceptionEvenWithAnEmptyBody()
    {
        var handler = new FakeHttpMessageHandler((_, _) => EmptyResponse(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyQuotaException error = await Assert.ThrowsAsync<OpenTopographyQuotaException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(string.Empty, error.ServerMessage);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns400AsARequestValidationException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.BadRequest, "south and north are required"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyRequestValidationException error = await Assert.ThrowsAsync<OpenTopographyRequestValidationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("south and north are required", error.ServerMessage, StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns204AsANoDataException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => EmptyResponse(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNoDataException error = await Assert.ThrowsAsync<OpenTopographyNoDataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.NoContent, error.StatusCode);
        AssertSingleWellFormedRequest(handler);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ReturnsServerErrorStatusCodesAsAServerException(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(statusCode, "upstream failure"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyServerException error = await Assert.ThrowsAsync<OpenTopographyServerException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(statusCode, error.StatusCode);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsHttpRequestExceptionAsANetworkException()
    {
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) =>
            throw new HttpRequestException("Simulated connection reset"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<HttpRequestException>(error.InnerException);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsAnIOExceptionAsANetworkException()
    {
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) =>
            throw new IOException("Simulated connection drop"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<IOException>(error.InnerException);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsATimeoutStyleCancellationAsANetworkExceptionWhenTheCallerTokenIsNotCancelled()
    {
        var handler = new FakeHttpMessageHandler((HttpRequestMessage _, CancellationToken _) =>
            throw new TaskCanceledException("Simulated timeout", new TimeoutException()));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<TaskCanceledException>(error.InnerException);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsHttpRequestExceptionWithoutLeakingTheKeyThroughTheInnerExceptionChain()
    {
        // Regression test: the caught HttpRequestException used to be attached to OpenTopographyNetworkException
        // as InnerException verbatim, even though its own Message (here scripted the way a caller-attached
        // DelegatingHandler realistically could, by embedding the request URI) was never redacted.
        // Exception.ToString() recurses into InnerException, so the raw key survived in the outer,
        // publicly-thrown exception's own ToString() even though its outer Message was correctly redacted.
        var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
            throw new HttpRequestException($"Connection failed while requesting {request.RequestUri}"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsAnIOExceptionWithoutLeakingTheKeyThroughTheInnerExceptionChain()
    {
        // Same regression as above, for the IOException catch.
        var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
            throw new IOException($"Connection dropped while requesting {request.RequestUri}"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<IOException>(error.InnerException);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WrapsATimeoutStyleCancellationWithoutLeakingTheKeyThroughTheInnerExceptionChain()
    {
        // Same regression as above, for the timeout-style OperationCanceledException catch. The outer
        // message here is a fixed string that never interpolates ex.Message, but the original ex was still
        // attached as InnerException verbatim, so a caller-thrown cancellation exception whose own Message
        // embeds the request URI still leaked the key through the exception chain.
        var handler = new FakeHttpMessageHandler((HttpRequestMessage request, CancellationToken _) =>
            throw new TaskCanceledException($"Simulated timeout while requesting {request.RequestUri}"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<TaskCanceledException>(error.InnerException);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task PropagatesACallerCancelledTokenUnwrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new FakeHttpMessageHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EmptyResponse(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        // This test deliberately controls its own cancellation token (it must cancel it itself) rather
        // than using TestContext.Current.CancellationToken, so xUnit1051 does not apply here.
#pragma warning disable xUnit1051
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.AcquireAsync(SmallRequest(), cts.Token).AsTask());
#pragma warning restore xUnit1051
    }

    [Fact]
    public async Task ZipWithAscAndPrjSidecarProducesASuccessfulAcquisition()
    {
        string prjText = ReadFixture("example-site-synthetic.prj");
        string ascText = ReadFixture("example-site-synthetic.asc");
        byte[] zipBytes = CreateZipArchive(("example-site-synthetic.asc", ascText), ("example-site-synthetic.prj", prjText));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        HorizontalReference horizontal = result.Acquisition.Data.HorizontalReference;
        Assert.Equal("EPSG:26915", horizontal.CoordinateReferenceSystem);
        Assert.Contains("NAD83", horizontal.Datum, StringComparison.Ordinal);
        Assert.Equal(HorizontalReferenceKind.Projected, horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, horizontal.Unit.LinearUnit);
        Assert.Equal(HorizontalAxisOrder.EastingNorthing, horizontal.AxisOrder);

        VerticalReference vertical = result.Acquisition.Data.VerticalReference;
        Assert.Equal("NAVD88", vertical.Datum);
        Assert.Equal(LengthUnit.Meter, vertical.Unit);

        ElevationGrid grid = Assert.IsType<ElevationGrid>(result.Acquisition.Data);
        Assert.Equal(3, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(184.20d, grid.GetElevation(0, 0));
        Assert.Null(grid.GetElevation(0, 2));

        Assert.Equal("OpenTopography", result.Acquisition.Source.SourceName);
        Assert.Equal("USGS1m", result.Acquisition.Source.DatasetIdentifier);
        Assert.Null(result.Acquisition.Source.CollectionPeriod);
        Assert.Null(result.Acquisition.Source.QualityLevel);

        Assert.Equal(HttpStatusCode.OK, result.Evidence.StatusCode);
        Assert.Equal("application/zip", result.Evidence.ContentType);
        Assert.Equal("usgs1m.zip", result.Evidence.ContentDispositionFileName);
        Assert.Equal(OpenTopographyReferenceSource.PrjSidecar, result.Evidence.ReferenceSource);
        Assert.Equal(prjText, result.Evidence.WellKnownText);
        Assert.Contains("example-site-synthetic.asc", result.Evidence.ArchiveEntryNames);
        Assert.Contains("example-site-synthetic.prj", result.Evidence.ArchiveEntryNames);
        Assert.Equal(zipBytes.LongLength, result.Evidence.ResponseByteCount);
        Assert.DoesNotContain(FakeKey, result.Evidence.RedactedRequestUri, StringComparison.Ordinal);

        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task SuccessfulAcquisitionEvidenceRedactsContentDispositionFileNameAndEntryNamesContainingTheApiKey()
    {
        // Regression test: OpenTopographyResponseEvidence is documented as evidence a caller may log or
        // inspect to "audit or diagnose an acquisition", and it is returned on a fully successful (non-
        // throwing) acquisition, not just on a failure path. Its constructor used to store
        // contentDispositionFileName and archiveEntryNames verbatim, with no redaction, even though
        // RedactedRequestUri on the very same type is (as its name implies) always redacted.
        string prjText = ReadFixture("example-site-synthetic.prj");
        string ascText = ReadFixture("example-site-synthetic.asc");
        string traceEntryName = $"trace-{FakeKey}.log";
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ascText),
            ("example-site-synthetic.prj", prjText),
            (traceEntryName, "irrelevant"));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes, $"usgs1m-{FakeKey}.zip"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Evidence.ContentDispositionFileName);
        Assert.DoesNotContain(FakeKey, result.Evidence.ContentDispositionFileName, StringComparison.Ordinal);
        Assert.All(result.Evidence.ArchiveEntryNames, entryName => Assert.DoesNotContain(FakeKey, entryName, StringComparison.Ordinal));
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task SuccessfulAcquisitionEvidenceRedactsWellKnownTextContainingTheApiKeyInADiscardedUnitName()
    {
        // The evidence's WellKnownText must be redacted too (it is the same kind of server-controlled
        // sidecar content as a zip entry name), but redaction must not run before parsing: parsing must see
        // the original, unredacted WKT. UNIT's quoted name is validated only for blankness and then discarded
        // once its numeric factor resolves to a supported LengthUnit, so embedding the key there exercises
        // "the raw key appeared in the sidecar but not in a field this source checks or returns", distinct
        // from the coordinate reference system name, horizontal datum, and vertical datum fields that are
        // now deliberately rejected when they echo the key (see the WellKnownText...FailsAsASourceMetadataException
        // tests below).
        string prjWithKeyAsUnitName = $"""
            COMPD_CS["Test Compound",
                PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["{FakeKey}",1.0]],
                VERT_CS["Test Vertical",VERT_DATUM["Test Vertical Datum"],UNIT["Meter",1.0]]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsUnitName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(LengthUnit.Meter, result.Acquisition.Data.HorizontalReference.Unit.LinearUnit);
        Assert.DoesNotContain(FakeKey, result.Evidence.WellKnownText, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task SuccessfulAcquisitionParsesACoordinateReferenceSystemNameContainingTheGenericApiKeyPatternVerbatim()
    {
        // Regression test: redaction must not run on the WKT before parsing. OpenTopographyRedaction.RedactText
        // also removes any literal "API_Key=<value>" pattern independent of whether it matches the actually
        // configured key. A legitimate-but-unlucky coordinate reference system name that merely resembles
        // that generic pattern (without actually echoing the configured key) must still parse to its true
        // value, not a "[REDACTED]"-corrupted one, and must not be rejected by the new echoed-key guard
        // below, which only fires when the identifier actually matches the configured key.
        const string crsName = "Test-API_Key=NotActuallyASecret";
        string prjWithGenericPatternInName = $"""
            COMPD_CS["Test Compound",
                PROJCS["{crsName}",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0]],
                VERT_CS["Test Vertical",VERT_DATUM["Test Vertical Datum"],UNIT["Meter",1.0]]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithGenericPatternInName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(crsName, result.Acquisition.Data.HorizontalReference.CoordinateReferenceSystem);
        Assert.DoesNotContain(crsName, result.Evidence.WellKnownText, StringComparison.Ordinal);
        Assert.DoesNotContain(crsName, result.Evidence.ToString(), StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WellKnownTextProjectedCoordinateSystemNameMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        // Regression test: a WKT coordinate reference system name that happens to equal or contain the
        // configured API key (OpenTopography has been observed to echo a rejected key back in other response
        // shapes) used to be parsed through and attached, verbatim, to the returned ElevationGrid's public
        // HorizontalReference -- a plain record with no ToString() override -- reaching the successful,
        // caller-facing acquisition result rather than just a discardable diagnostic.
        string prjWithKeyAsProjcsName = $"""
            PROJCS["{FakeKey}",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsProjcsName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("coordinate reference system name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WellKnownTextHorizontalDatumNameMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        string prjWithKeyAsDatumName = $"""
            PROJCS["Test",GEOGCS["Test",DATUM["{FakeKey}"]],UNIT["Meter",1.0]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsDatumName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("horizontal datum name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WellKnownTextVerticalDatumNameMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        string prjWithKeyAsVerticalDatumName = $"""
            COMPD_CS["Test Compound",
                PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0]],
                VERT_CS["Test Vertical",VERT_DATUM["{FakeKey}"],UNIT["Meter",1.0]]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsVerticalDatumName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("vertical datum name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WellKnownTextAuthorityCodeContainingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        // AUTHORITY's code becomes part of a composed "<authority>:<code>" identifier, so this must be
        // caught by a substring check, not only an exact-equality check against the configured key.
        string prjWithKeyAsAuthorityCode = $"""
            PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["Meter",1.0],AUTHORITY["EPSG","{FakeKey}"]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsAuthorityCode));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("coordinate reference system name", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithOnlyAnAscEntryFailsListingTheEntries()
    {
        byte[] zipBytes = CreateZipArchive(("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("example-site-synthetic.asc", error.Message, StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithAPrjLackingAVerticalCoordinateSystemFailsMentioningVertical()
    {
        const string prjWithoutVertical = """
            PROJCS["NAD_1983_UTM_Zone_15N",
                GEOGCS["GCS_North_American_1983",
                    DATUM["NAD83",
                        SPHEROID["GRS_1980",6378137.0,298.257222101]],
                    PRIMEM["Greenwich",0.0],
                    UNIT["Degree",0.0174532925199433]],
                PROJECTION["Transverse_Mercator"],
                PARAMETER["False_Easting",500000.0],
                PARAMETER["False_Northing",0.0],
                PARAMETER["Central_Meridian",-93.0],
                PARAMETER["Scale_Factor",0.9996],
                PARAMETER["Latitude_Of_Origin",0.0],
                UNIT["Meter",1.0],
                AUTHORITY["EPSG","26915"]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithoutVertical));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("vertical", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithABlankProjcsNameFailsAsSourceMetadataExceptionInsteadOfAnUnwrappedArgumentException()
    {
        // Regression test: a present-but-blank quoted PROJCS name used to slip past
        // WellKnownTextReferenceParser's null-only "?? throw FormatException" guard (FirstStringArgument
        // returned "" rather than null for a blank quoted string), reach HorizontalReference's constructor,
        // and surface as a raw, undocumented System.ArgumentException instead of the documented
        // OpenTopographySourceMetadataException, breaking the OpenTopographyException hierarchy guarantee.
        const string prjWithBlankName = """
            PROJCS["",
                GEOGCS["Test Geographic",
                    DATUM["Test Datum"]],
                UNIT["Meter",1.0]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithBlankName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("quoted name", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithAnAuxXmlSrsElementFallsBackSuccessfully()
    {
        string prjText = ReadFixture("example-site-synthetic.prj");
        string auxXml = $"""<PAMDataset><SRS>{prjText}</SRS></PAMDataset>""";
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.asc.aux.xml", auxXml));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(OpenTopographyReferenceSource.AuxXmlSidecar, result.Evidence.ReferenceSource);
        Assert.Equal("EPSG:26915", result.Acquisition.Data.HorizontalReference.CoordinateReferenceSystem);
        Assert.Equal("NAVD88", result.Acquisition.Data.VerticalReference.Datum);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithTwoAscEntriesFailsListingTheEntries()
    {
        string ascText = ReadFixture("example-site-synthetic.asc");
        byte[] zipBytes = CreateZipArchive(("first.asc", ascText), ("second.asc", ascText));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("first.asc", error.Message, StringComparison.Ordinal);
        Assert.Contains("second.asc", error.Message, StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithTwoConflictingPrjEntriesFailsListingTheEntriesInsteadOfPickingOne()
    {
        // Regression test: unlike .asc (checked above), the .prj lookup used to be a bare FirstOrDefault
        // with no count check, so a second .prj entry with conflicting reference metadata was silently
        // discarded instead of causing a failure.
        string ascText = ReadFixture("example-site-synthetic.asc");
        string prjText = ReadFixture("example-site-synthetic.prj");
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ascText),
            ("first.prj", prjText),
            ("second.prj", prjText));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("first.prj", error.Message, StringComparison.Ordinal);
        Assert.Contains("second.prj", error.Message, StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipWithTwoConflictingAuxXmlEntriesFailsListingTheEntriesInsteadOfPickingOne()
    {
        // Same regression as the .prj case above, for the .aux.xml fallback lookup.
        string ascText = ReadFixture("example-site-synthetic.asc");
        string auxXml = $"""<PAMDataset><SRS>{ReadFixture("example-site-synthetic.prj")}</SRS></PAMDataset>""";
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ascText),
            ("first.aux.xml", auxXml),
            ("second.aux.xml", auxXml));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("first.aux.xml", error.Message, StringComparison.Ordinal);
        Assert.Contains("second.aux.xml", error.Message, StringComparison.Ordinal);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task ZipEntryNameContainingTheApiKeyIsRedactedWhenNoAscEntryExists()
    {
        // Regression test: ParseZipResponse used to interpolate raw archive entry names (via
        // DescribeEntries) into OpenTopographySourceMetadataException with no redaction at all, even though
        // sibling non-zip paths in this same source already redact server-derived text. A zip entry name
        // that happens to embed the caller's own API key (OpenTopography is documented to echo it back in
        // other response shapes) used to leak the raw key through this exception's Message.
        byte[] zipBytes = CreateZipArchive(($"notes-{FakeKey}.txt", "irrelevant"));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(".asc entries", error.Message, StringComparison.Ordinal);
        Assert.Contains("notes-", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task AuxXmlSidecarFileNameContainingTheApiKeyIsRedactedWhenItHasNoSrsElement()
    {
        string ascText = ReadFixture("example-site-synthetic.asc");
        string auxEntryName = $"sidecar-{FakeKey}.aux.xml";
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ascText),
            (auxEntryName, "<PAMDataset></PAMDataset>"));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("no <SRS> element", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task AscEntryFileNameContainingTheApiKeyIsRedactedWhenTheRasterFailsToParse()
    {
        string prjText = ReadFixture("example-site-synthetic.prj");
        string ascEntryName = $"grid-{FakeKey}.asc";
        byte[] zipBytes = CreateZipArchive((ascEntryName, string.Empty), ("example-site-synthetic.prj", prjText));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("could not be parsed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task WktUnitNameContainingTheApiKeyIsRedactedWhenTheConversionFactorIsUnsupported()
    {
        // Regression test: WellKnownTextReferenceParser is deliberately generic and API-key-agnostic, so an
        // unsupported UNIT name is quoted verbatim into its own FormatException.Message. ParseZipResponse
        // used to wrap that ex.Message straight into OpenTopographySourceMetadataException with no
        // redaction, in both the outer exception's Message and the preserved InnerException.
        string prjWithKeyAsUnitName = $"""
            PROJCS["Test",GEOGCS["Test",DATUM["Test Datum"]],UNIT["{FakeKey}",999.0]]
            """;
        byte[] zipBytes = CreateZipArchive(
            ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")),
            ("example-site-synthetic.prj", prjWithKeyAsUnitName));
        var handler = new FakeHttpMessageHandler((_, _) => ZipResponse(zipBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Unsupported linear unit", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.DoesNotContain(FakeKey, error.InnerException!.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task BareAaiGridBodyFailsMentioningMissingReferenceMetadata()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("example-site-synthetic.asc")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("coordinate reference", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task BareAaiGridResponseRedactsAContentDispositionFileNameContainingTheApiKey()
    {
        // Regression test: HandleSuccessAsync's bare-AAIGrid branch read contentDispositionFileName straight
        // from the response's Content-Disposition header and interpolated it into
        // OpenTopographySourceMetadataException with no redaction, even though apiKey and
        // OpenTopographyRedaction were already in scope and used two branches later in the same method.
        var handler = new FakeHttpMessageHandler((_, _) =>
            TextResponseWithFileName(HttpStatusCode.OK, ReadFixture("example-site-synthetic.asc"), $"grid-{FakeKey}.asc"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("coordinate reference", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task BareAaiGridResponseRedactsAContentTypeHeaderContainingTheApiKey()
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(ReadFixture("example-site-synthetic.asc"));
        var handler = new FakeHttpMessageHandler((_, _) => BinaryResponse(HttpStatusCode.OK, bodyBytes, $"text/plain; x-echo={FakeKey}"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("coordinate reference", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns200HtmlAsAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, "<html><body>Not Found</body></html>", "text/html"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns200GzipMagicBytesAsAnUnexpectedResponseException()
    {
        byte[] gzipBytes = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00];
        var handler = new FakeHttpMessageHandler((_, _) => BinaryResponse(HttpStatusCode.OK, gzipBytes, "application/gzip"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task Returns200UnrecognizedBodyWithAKeyContainingAnAngleBracketStillFullyRedactsItInTheSnippet()
    {
        // Regression test for the same tag-stripping-before-redaction bug as
        // Returns401WithAKeyContainingAnAngleBracketStillFullyRedactsIt, but for the other call site that
        // built a redacted snippet the same (buggy) way: HandleSuccessAsync's unrecognized-200-body path.
        const string keyWithAngleBracket = "KEYPREFIXVISIBLE<KEYSUFFIXHIDDEN";
        string body = $"<html>unexpected {keyWithAngleBracket} response</html>";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, body, "text/html"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, keyWithAngleBracket);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(error.ServerMessage);
        Assert.DoesNotContain("KEYPREFIXVISIBLE", error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYSUFFIXHIDDEN", error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("KEYPREFIXVISIBLE", error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error, keyWithAngleBracket);
        AssertExceptionChainDoesNotContainTheKey(error, "KEYPREFIXVISIBLE");
    }

    [Fact]
    public async Task Returns200UnrecognizedBodyRedactsAContentTypeHeaderContainingTheApiKey()
    {
        // Regression test: the unrecognized-200-body branch already redacted the body snippet, but
        // snippetContentType (the same branch's Content-Type header text) was interpolated unredacted.
        byte[] bodyBytes = Encoding.UTF8.GetBytes("<html>unexpected response</html>");
        var handler = new FakeHttpMessageHandler((_, _) => BinaryResponse(HttpStatusCode.OK, bodyBytes, $"text/html; x-echo={FakeKey}"));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task OversizeResponseFailsWithAnUnexpectedResponseException()
    {
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, new string('x', 1000)));
        using var httpClient = new HttpClient(handler);
        var options = new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = 16 };
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey, options);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSingleWellFormedRequest(handler);
    }

    [Fact]
    public async Task AByteCapThatSplitsAnEchoedKeyMidValueStillFullyRedactsTheSurvivingFragment()
    {
        // Regression test: ReadBodyAsync's byte cap commits whatever chunks were fully read before the cap
        // was reached. A real transport can split its reads anywhere (TLS record boundaries, chunked
        // transfer-encoding, ordinary TCP fragmentation), so a chunk boundary can land in the middle of an
        // echoed key. RedactText's whole-value match cannot see a value that was cut in half, so the
        // surviving prefix used to reach both Message and ServerMessage unredacted. This scripts a stream
        // that returns exactly enough bytes on its first read to include only the key's first 5 characters,
        // then the remainder on a second read that pushes the total over the configured cap.
        const string truncationKey = "ABCDEFGHIJKLMNOPQRST";
        const string prefix = "Error: Not a valid format API Key: ";
        string body = prefix + truncationKey;
        string leakedFragmentIfUnfixed = truncationKey[..5];
        int firstChunkByteCount = prefix.Length + leakedFragmentIfUnfixed.Length;
        var handler = new FakeHttpMessageHandler((_, _) =>
            ChunkedTextResponse(HttpStatusCode.Unauthorized, body, firstChunkByteCount, "application/xml"));
        using var httpClient = new HttpClient(handler);
        var options = new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = firstChunkByteCount };
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, truncationKey, options);

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(error.ServerMessage);
        Assert.Contains("truncated", error.ServerMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(leakedFragmentIfUnfixed, error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(leakedFragmentIfUnfixed, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(truncationKey, error.ServerMessage, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error, truncationKey);
        AssertExceptionChainDoesNotContainTheKey(error, leakedFragmentIfUnfixed);
    }

    [Fact]
    public async Task AByteCapThatSplitsAMultiByteCharacterInsideAnEchoedKeyStillFullyRedactsTheSurvivingPrefix()
    {
        // Regression test: when a byte cap cuts through the middle of a multi-byte UTF-8 character that is
        // part of an echoed key, DecodeUtf8 (Encoding.UTF8.GetString with the default replacement fallback)
        // renders the incomplete trailing sequence as a single U+FFFD character. RedactTrailingKeyFragment's
        // plain suffix comparison used to find no match at all (U+FFFD cannot equal any character of the raw
        // key), leaving the entire surviving ASCII prefix of the key ("ABCDEFGHIJ" below) exposed in both
        // ServerMessage and Message. 'é' (U+00E9) is 2 bytes in UTF-8; this cuts exactly after "ABCDEFGHIJ"
        // plus the lead byte of 'é'.
        const string truncationKey = "ABCDEFGHIJéKLMNOP";
        const string prefix = "Error: Not a valid format API Key: ";
        string body = prefix + truncationKey;
        const string leakedPrefixIfUnfixed = "ABCDEFGHIJ";
        int firstChunkByteCount = Encoding.UTF8.GetByteCount(prefix + leakedPrefixIfUnfixed) + 1;
        var handler = new FakeHttpMessageHandler((_, _) =>
            ChunkedTextResponse(HttpStatusCode.Unauthorized, body, firstChunkByteCount, "application/xml"));
        using var httpClient = new HttpClient(handler);
        var options = new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = firstChunkByteCount };
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, truncationKey, options);

        OpenTopographyAuthorizationException error = await Assert.ThrowsAsync<OpenTopographyAuthorizationException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.NotNull(error.ServerMessage);
        Assert.Contains("truncated", error.ServerMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(leakedPrefixIfUnfixed, error.ServerMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(leakedPrefixIfUnfixed, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(truncationKey, error.ServerMessage, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error, truncationKey);
        AssertExceptionChainDoesNotContainTheKey(error, leakedPrefixIfUnfixed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnOversizeErrorBodyStillClassifiesByStatusCodeRatherThanAsAnUnexpectedResponse(HttpStatusCode statusCode)
    {
        // Regression test: the byte cap used to be enforced by ReadBodyAsync itself, which threw
        // OpenTopographyUnexpectedResponseException purely from the accumulated byte count, before any of
        // the status-code classification ran. That meant a genuine 401/403/429/400/5xx response whose body
        // merely happened to exceed a caller-configured MaximumResponseBytes was reported as a generic
        // "unexpected response" instead of the status-appropriate exception, even though the real status
        // code was already known before the cap was hit.
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(statusCode, new string('x', 1000)));
        using var httpClient = new HttpClient(handler);
        var options = new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = 16 };
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey, options);

        OpenTopographyException error = await Assert.ThrowsAnyAsync<OpenTopographyException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsNotType<OpenTopographyUnexpectedResponseException>(error);
        AssertSingleWellFormedRequest(handler);
    }

    private static OpenTopographyUsgs1mSource CreateSource(HttpClient httpClient, string apiKeyValue, OpenTopographyUsgs1mSourceOptions? options = null) =>
        new(httpClient, new StaticOpenTopographyApiKeyProvider(new OpenTopographyApiKey(apiKeyValue)), options);

    private static ElevationSourceRequest SmallRequest() =>
        new(new Wgs84BoundingBoxAoi([withheld], [withheld], [withheld], [withheld]));

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    internal static byte[] CreateZipArchive(params (string EntryName, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string entryName, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                using Stream entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static Task<HttpResponseMessage> EmptyResponse(HttpStatusCode statusCode) =>
        Task.FromResult(new HttpResponseMessage(statusCode));

    private static Task<HttpResponseMessage> TextResponse(HttpStatusCode statusCode, string body, string mediaType = "text/plain")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> BinaryResponse(HttpStatusCode statusCode, byte[] body, string mediaType)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(body),
        };
        // MediaTypeHeaderValue's single-string constructor only accepts a bare "type/subtype" and rejects
        // any trailing ";name=value" parameter; MediaTypeHeaderValue.Parse accepts the full grammar, which
        // this helper's callers need to script a Content-Type header that carries extra parameters.
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> TextResponseWithFileName(HttpStatusCode statusCode, string body, string fileName, string mediaType = "text/plain")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        return Task.FromResult(response);
    }

    /// <summary>
    /// Builds a response whose body stream returns exactly <paramref name="firstChunkByteCount"/> bytes on
    /// its first read, then the remainder on later reads. Used to script a byte-cap truncation that cuts a
    /// response body at a precise, arbitrary point, mirroring how a real transport (TLS records, chunked
    /// transfer-encoding, TCP fragmentation) can split a response across multiple reads at a boundary this
    /// source does not control.
    /// </summary>
    private static Task<HttpResponseMessage> ChunkedTextResponse(HttpStatusCode statusCode, string body, int firstChunkByteCount, string mediaType = "text/plain")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(new ChunkedByteStream(bytes, firstChunkByteCount)),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> ZipResponse(byte[] zipBytes, string? fileName = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (fileName is not null)
        {
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        }

        return Task.FromResult(response);
    }

    private static void AssertSingleWellFormedRequest(FakeHttpMessageHandler handler)
    {
        HttpRequestMessage sent = Assert.Single(handler.Requests);
        string query = sent.RequestUri!.Query;
        Assert.DoesNotContain("USGS10m", query, StringComparison.Ordinal);
        Assert.DoesNotContain("USGS30m", query, StringComparison.Ordinal);
        Assert.DoesNotContain("GTiff", query, StringComparison.Ordinal);
    }

    private static void AssertExceptionChainDoesNotContainTheKey(Exception exception, string key = FakeKey)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(key, current.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A non-seekable, forward-only stream that returns exactly <paramref name="firstReadByteCount"/> bytes
    /// on its first read (however many bytes the caller requested), then whatever remains on later reads.
    /// <see cref="StreamContent"/> hands this exact instance back from <c>ReadAsStreamAsync</c> on first
    /// consumption, so it lets a test control precisely where <see cref="OpenTopographyUsgs1mSource"/>'s own
    /// chunked body-reading loop sees its first read end.
    /// </summary>
    private sealed class ChunkedByteStream(byte[] data, int firstReadByteCount) : Stream
    {
        private int position;
        private bool consumedFirstRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= data.Length)
            {
                return ValueTask.FromResult(0);
            }

            int available = data.Length - position;
            int allowedThisCall = consumedFirstRead ? available : Math.Min(firstReadByteCount, available);
            consumedFirstRead = true;
            int toCopy = Math.Min(buffer.Length, allowedThisCall);
            data.AsSpan(position, toCopy).CopyTo(buffer.Span);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
