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
        Assert.Equal(ReferenceOrigin.SourceResponse, result.Evidence.HorizontalReferenceOrigin);
        Assert.Equal(ReferenceOrigin.SourceResponse, result.Evidence.VerticalReferenceOrigin);
        Assert.Null(result.Evidence.MetadataRequest);

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
    public async Task BareAaiGridBodyTriggersASecondRequestAndFailsWhenItIsNotGeoTiff()
    {
        // Behavior change from the single-request source: a bare AAIGrid body (USGS 1 m's actual observed
        // shape, with no .prj or .aux.xml sidecar) no longer fails immediately. It triggers a second,
        // otherwise identical request with outputFormat=GTiff to recover reference metadata from GeoKeys.
        // Here that second response is not GeoTIFF either (another bare AAIGrid-shaped text body), so the
        // acquisition still fails, but now as an OpenTopographyUnexpectedResponseException about the
        // *metadata* response rather than the single-request source's old "missing reference metadata"
        // OpenTopographySourceMetadataException.
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, ReadFixture("example-site-synthetic.asc")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("GeoTIFF", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        AssertAaiGridRequest(handler.Requests[0]);
        AssertMetadataRequest(handler.Requests[1]);
    }

    [Fact]
    public async Task MalformedAaiGridHeaderInABareBodyFailsAsASourceMetadataExceptionWithoutASecondRequest()
    {
        const string malformedHeader = "ncols not-a-number\nnrows 2\nxllcorner 0\nyllcorner 0\ncellsize 1\n1 2\n3 4\n";
        var handler = new FakeHttpMessageHandler((_, _) => TextResponse(HttpStatusCode.OK, malformedHeader));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("ncols", error.Message, StringComparison.Ordinal);
        AssertAaiGridRequest(Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task MalformedAaiGridCellInABareBodyAfterAValidMetadataResponseDoesNotLeakTheApiKey()
    {
        // Regression test: HandleBareAaiGridAsync's final AaiGridParser.Parse catch (parsing the data
        // response's own AAIGrid text with the reference recovered from the metadata response) used to wrap
        // ex.Message and ex itself directly, unredacted, unlike every sibling catch in this same method that
        // wraps a parse failure over server-controlled text (the AaiGridParser.ReadHeader catch exercised by
        // MalformedAaiGridHeaderInABareBodyFailsAsASourceMetadataExceptionWithoutASecondRequest immediately
        // above, the GeoTiffMetadataReader.Read catch, and both WellKnownTextReferenceParser catches). This
        // scripts a malformed cell -- the fake key in place of a numeric elevation value -- to prove the fix:
        // AaiGridParser itself never echoes a cell's raw text (only its row/column position), so this is a
        // defense-in-depth proof, not a proof of an active leak.
        string aaiGridTextWithAMalformedCell =
            $"ncols 3\nnrows 2\nxllcorner 500000\nyllcorner 4000000\ncellsize 2\nNODATA_value -9999\n1 2 {FakeKey}\n4 5 -9999\n";
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(aaiGridTextWithAMalformedCell, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("row 1, column 3", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.DoesNotContain(FakeKey, error.InnerException!.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task HybridFlowRedactsTheMetadataResponsesContentDispositionFileNameContainingTheApiKey()
    {
        // D8: the metadata (second) response's Content-Disposition file name is redacted before it reaches
        // OpenTopographyMetadataRequestEvidence, exactly like the first response's own
        // ContentDispositionFileName already is.
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(
            HappyPathAaiGridText,
            (_, _) => BinaryResponseWithFileName(HttpStatusCode.OK, tiffBytes, $"metadata-{FakeKey}.tif", "image/tiff")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        OpenTopographyMetadataRequestEvidence metadataEvidence = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.NotNull(metadataEvidence.ContentDispositionFileName);
        Assert.DoesNotContain(FakeKey, metadataEvidence.ContentDispositionFileName, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridFlowRedactsTheMetadataResponsesContentTypeHeaderContainingTheApiKey()
    {
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(
            HappyPathAaiGridText,
            (_, _) => BinaryResponse(HttpStatusCode.OK, tiffBytes, $"image/tiff; x-echo={FakeKey}")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        OpenTopographyMetadataRequestEvidence metadataEvidence = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.NotNull(metadataEvidence.ContentType);
        Assert.DoesNotContain(FakeKey, metadataEvidence.ContentType, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridFlowRedactsTheDataResponsesContentDispositionFileNameContainingTheApiKey()
    {
        // Regression test restoring pre-hybrid-flow coverage: the single-request source's
        // BareAaiGridResponseRedactsAContentDispositionFileNameContainingTheApiKey proved the bare-AAIGrid
        // data response's own Content-Disposition file name was redacted; that failure path no longer exists
        // now that a bare AAIGrid data response triggers a second, metadata request instead of failing
        // immediately (see HandleBareAaiGridAsync), so this proves the same header is still redacted, here on
        // the successful acquisition's OpenTopographyResponseEvidence.
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(
            (_, _) => TextResponseWithFileName(HttpStatusCode.OK, HappyPathAaiGridText, $"grid-{FakeKey}.asc"),
            (_, _) => BinaryResponse(HttpStatusCode.OK, tiffBytes, "image/tiff")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Evidence.ContentDispositionFileName);
        Assert.DoesNotContain(FakeKey, result.Evidence.ContentDispositionFileName, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridFlowRedactsTheDataResponsesContentTypeHeaderContainingTheApiKey()
    {
        // Regression test restoring pre-hybrid-flow coverage: see
        // HybridFlowRedactsTheDataResponsesContentDispositionFileNameContainingTheApiKey immediately above.
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(
            (_, _) => BinaryResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes(HappyPathAaiGridText), $"text/plain; x-echo={FakeKey}"),
            (_, _) => BinaryResponse(HttpStatusCode.OK, tiffBytes, "image/tiff")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Evidence.ContentType);
        Assert.DoesNotContain(FakeKey, result.Evidence.ContentType, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridFlowHappyPathSendsTwoRequestsAndProducesGeoTiffDerivedEvidence()
    {
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        AssertAaiGridRequest(handler.Requests[0]);
        AssertMetadataRequest(handler.Requests[1]);
        AssertIdenticalAreaParameters(handler.Requests[0], handler.Requests[1]);

        HorizontalReference horizontal = result.Acquisition.Data.HorizontalReference;
        Assert.Equal("EPSG:26915", horizontal.CoordinateReferenceSystem);
        Assert.Equal(HorizontalReferenceKind.Projected, horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, horizontal.Unit.LinearUnit);

        VerticalReference vertical = result.Acquisition.Data.VerticalReference;
        Assert.Equal("NAVD88", vertical.Datum);
        Assert.Equal(LengthUnit.Meter, vertical.Unit);

        ElevationGrid grid = Assert.IsType<ElevationGrid>(result.Acquisition.Data);
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);

        Assert.Equal(OpenTopographyReferenceSource.GeoTiffGeoKeys, result.Evidence.ReferenceSource);
        Assert.Empty(result.Evidence.ArchiveEntryNames);
        Assert.Equal(ReferenceOrigin.SourceMetadataResponse, result.Evidence.HorizontalReferenceOrigin);
        Assert.Equal(ReferenceOrigin.DatasetDocumentation, result.Evidence.VerticalReferenceOrigin);
        Assert.Equal(
            NorthAmericanUtmWellKnownText.Create(26915, new OpenTopographyUsgs1mSourceOptions().DeclaredVerticalReference),
            result.Evidence.WellKnownText);
        Assert.DoesNotContain(FakeKey, result.Evidence.RedactedRequestUri, StringComparison.Ordinal);

        OpenTopographyMetadataRequestEvidence metadataEvidence = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.Equal(HttpStatusCode.OK, metadataEvidence.StatusCode);
        Assert.Equal(26915, metadataEvidence.ProjectedCoordinateSystemCode);
        Assert.Equal("PixelIsArea", metadataEvidence.RasterType);
        Assert.Equal(3, metadataEvidence.ImageWidth);
        Assert.Equal(2, metadataEvidence.ImageLength);
        Assert.Equal(tiffBytes.LongLength, metadataEvidence.ResponseByteCount);
        Assert.DoesNotContain(FakeKey, metadataEvidence.RedactedRequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HybridFlowHappyPathWithABigEndianMetadataResponseProducesTheIdenticalEvidence()
    {
        // Every other hybrid-flow test builds its GeoTIFF metadata response through BuildTiffBytes' default
        // TiffScenario, which is always little-endian, so the full pipeline this feeds -- ValidateGeoTiffMetadata,
        // EnsureGridsAgree's corner-agreement math, WKT synthesis, and OpenTopographyMetadataRequestEvidence
        // assembly -- was otherwise never proven end-to-end against a big-endian response (only
        // GeoTiffMetadataReader itself is covered for big-endian, in GeoTiffMetadataReaderTests). This is the
        // one test that exercises OpenTopographyUsgs1mSource.BigEndianTiffSignature's branch as true.
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario with { BigEndian = true });
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        AssertAaiGridRequest(handler.Requests[0]);
        AssertMetadataRequest(handler.Requests[1]);

        HorizontalReference horizontal = result.Acquisition.Data.HorizontalReference;
        Assert.Equal("EPSG:26915", horizontal.CoordinateReferenceSystem);
        Assert.Equal(HorizontalReferenceKind.Projected, horizontal.Kind);
        Assert.Equal(LengthUnit.Meter, horizontal.Unit.LinearUnit);

        VerticalReference vertical = result.Acquisition.Data.VerticalReference;
        Assert.Equal("NAVD88", vertical.Datum);
        Assert.Equal(LengthUnit.Meter, vertical.Unit);

        ElevationGrid grid = Assert.IsType<ElevationGrid>(result.Acquisition.Data);
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);

        Assert.Equal(OpenTopographyReferenceSource.GeoTiffGeoKeys, result.Evidence.ReferenceSource);
        Assert.Equal(ReferenceOrigin.SourceMetadataResponse, result.Evidence.HorizontalReferenceOrigin);
        Assert.Equal(ReferenceOrigin.DatasetDocumentation, result.Evidence.VerticalReferenceOrigin);
        Assert.Equal(
            NorthAmericanUtmWellKnownText.Create(26915, new OpenTopographyUsgs1mSourceOptions().DeclaredVerticalReference),
            result.Evidence.WellKnownText);

        OpenTopographyMetadataRequestEvidence metadataEvidence = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.Equal(HttpStatusCode.OK, metadataEvidence.StatusCode);
        Assert.Equal(26915, metadataEvidence.ProjectedCoordinateSystemCode);
        Assert.Equal("PixelIsArea", metadataEvidence.RasterType);
        Assert.Equal(3, metadataEvidence.ImageWidth);
        Assert.Equal(2, metadataEvidence.ImageLength);
        Assert.Equal(tiffBytes.LongLength, metadataEvidence.ResponseByteCount);
    }

    public static IEnumerable<object[]> VerifiedNonNad83RealizationScenarios()
    {
        // Zone 15N under each of the three other verified NAD83 realizations (NorthAmericanUtmWellKnownText's
        // Definitions table). The GTCitationGeoKey text is the realization's own EPSG name -- exactly what a
        // real GeoTIFF metadata response is expected to carry for GeoKey 1026 -- so this also proves that
        // citation reaches OpenTopographyMetadataRequestEvidence.Citation unredacted (it never matches the
        // fake API key).
        yield return [3745, "NAD83(HARN) / UTM zone 15N", "NAD83(HARN)"];
        yield return [3722, "NAD83(NSRS2007) / UTM zone 15N", "NAD83(NSRS2007)"];
        yield return [6344, "NAD83(2011) / UTM zone 15N", "NAD83(2011)"];
    }

    [Theory]
    [MemberData(nameof(VerifiedNonNad83RealizationScenarios))]
    public async Task HybridFlowSucceedsForEveryVerifiedNonNad83RealizationAndReportsItsOwnDatumAndCode(
        int epsgCode, string citation, string expectedDatum)
    {
        TiffScenario scenario = HappyPathScenario with { ProjectedCode = (ushort)epsgCode, Citation = citation };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        string codeText = epsgCode.ToString(CultureInfo.InvariantCulture);
        HorizontalReference horizontal = result.Acquisition.Data.HorizontalReference;
        Assert.Equal($"EPSG:{codeText}", horizontal.CoordinateReferenceSystem);
        Assert.Equal(expectedDatum, horizontal.Datum);
        Assert.Equal("NAVD88", result.Acquisition.Data.VerticalReference.Datum);

        OpenTopographyMetadataRequestEvidence metadataEvidence = Assert.IsType<OpenTopographyMetadataRequestEvidence>(result.Evidence.MetadataRequest);
        Assert.Equal(epsgCode, metadataEvidence.ProjectedCoordinateSystemCode);
        Assert.Equal(citation, metadataEvidence.Citation);
        Assert.DoesNotContain(FakeKey, result.Evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnsupportedZone1nCodeFailsAsASourceMetadataExceptionNamingTheCodeAndNeverContainingTheKey()
    {
        // EPSG:26901 (NAD83 / UTM zone 1N, Alaska) is a real, registered EPSG code -- NorthAmericanUtmWellKnownText
        // simply does not support it (SolidGround verified and tabulated only zones 10N-19N for the four NAD83
        // realizations; see docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis"
        // section), so this must fail the same way any other unsupported family does, naming only the integer
        // code, never the GeoKey number or the API key.
        TiffScenario scenario = HappyPathScenario with { ProjectedCode = 26901 };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("EPSG:26901", error.Message, StringComparison.Ordinal);
        Assert.Contains("10N to 19N", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.RedactedRequestUri, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
    }

    [Fact]
    public async Task AZone19NResultOverPuertoRicoFailsAsASourceMetadataExceptionInsteadOfDeclaringNavd88()
    {
        // EPSG:26919 (NAD83 / UTM zone 19N) is otherwise a supported code -- unlike EPSG:26901 above -- but
        // zone 19N (-72 to -66 degrees west) is the one supported zone that is not entirely within the
        // conterminous United States: it also covers Puerto Rico. A request bounding box entirely south of
        // the Florida Keys cannot be a conterminous United States location, so this must fail rather than
        // pair Puerto Rico with the declared NAVD88 vertical reference (which USGS documents as a per-project
        // choice there, not NAVD88) -- see docs/architecture/opentopography-usgs1m-source.md's "CONUS scope
        // and its rationale" section.
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(-66.11, 18.40, -66.10, 18.41));
        TiffScenario scenario = HappyPathScenario with { ProjectedCode = 26919 };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(request, TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("EPSG:26919", error.Message, StringComparison.Ordinal);
        Assert.Contains("Puerto Rico", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.RedactedRequestUri, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
    }

    [Fact]
    public async Task AZone19NResultOverMaineStillSucceedsAndDeclaresNavd88()
    {
        // The same zone 19N code as above, but with a conterminous United States bounding box (central
        // Maine, well north of the MinimumConusLatitudeForZone19N threshold), must still succeed: the new
        // Puerto Rico guard must not reject every zone 19N request, only ones south of the threshold.
        var request = new ElevationSourceRequest(new Wgs84BoundingBoxAoi(-68.01, 44.50, -68.00, 44.51));
        TiffScenario scenario = HappyPathScenario with { ProjectedCode = 26919 };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("EPSG:26919", result.Acquisition.Data.HorizontalReference.CoordinateReferenceSystem);
        Assert.Equal("NAVD88", result.Acquisition.Data.VerticalReference.Datum);
    }

    [Fact]
    public async Task HybridFlowWithTheObservedLiveGridDimensionsPassesTheCornerAgreementTolerance()
    {
        // Reconstructs the live the reference parcel scenario's AAIGrid header and GeoTIFF tiepoint (Issue #21
        // setup facts): the AAIGrid's yllcorner ([withheld]) and the corner derived from the
        // GeoTIFF's tiepoint ([withheld] minus 117 rows of 1-unit cells = [withheld]) disagree
        // by roughly 4e-10, which must still pass GridAgreementTolerance (1e-6).
        string data = string.Join(' ', Enumerable.Repeat("0", 124 * 117));
        string aaiGridText =
            "ncols 124\nnrows 117\nxllcorner [withheld]\nyllcorner [withheld]\n" +
            $"cellsize 1.000000000000\nNODATA_value -999999\n{data}\n";
        TiffScenario scenario = HappyPathScenario with
        {
            ImageWidth = 124,
            ImageLength = 117,
            ScaleX = 1d,
            ScaleY = 1d,
            TiepointX = [withheld],
            TiepointY = [withheld],
            NoDataText = "-999999",
        };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(aaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        ElevationGrid grid = Assert.IsType<ElevationGrid>(result.Acquisition.Data);
        Assert.Equal(124, grid.ColumnCount);
        Assert.Equal(117, grid.RowCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, typeof(OpenTopographyNoDataException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(OpenTopographyRequestValidationException))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(OpenTopographyAuthorizationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(OpenTopographyAuthorizationException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(OpenTopographyQuotaException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(OpenTopographyServerException))]
    public async Task SecondRequestNonSuccessStatusSurfacesTheSameExceptionTypeWithTheMetadataRequestsRedactedUri(
        HttpStatusCode statusCode, Type expectedExceptionType)
    {
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> metadataResponder = statusCode == HttpStatusCode.NoContent
            ? (_, _) => EmptyResponse(statusCode)
            : (_, _) => TextResponse(statusCode, $"server message mentioning {FakeKey}");
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, metadataResponder));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyException error = await Assert.ThrowsAnyAsync<OpenTopographyException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType(expectedExceptionType, error);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("outputFormat=GTiff", error.RedactedRequestUri, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.RedactedRequestUri, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
    }

    [Fact]
    public async Task SecondRequestHttpRequestExceptionSurfacesAsANetworkExceptionWithTheMetadataRequestsRedactedUri()
    {
        var handler = new FakeHttpMessageHandler(HybridResponder(
            HappyPathAaiGridText,
            (HttpRequestMessage _, CancellationToken _) => throw new HttpRequestException("Simulated connection reset")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Contains("outputFormat=GTiff", error.RedactedRequestUri, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SecondRequestTimeoutSurfacesAsANetworkExceptionWithTheMetadataRequestsRedactedUri()
    {
        var handler = new FakeHttpMessageHandler(HybridResponder(
            HappyPathAaiGridText,
            (HttpRequestMessage _, CancellationToken _) => throw new TaskCanceledException("Simulated timeout", new TimeoutException())));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyNetworkException error = await Assert.ThrowsAsync<OpenTopographyNetworkException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.IsType<TaskCanceledException>(error.InnerException);
        Assert.Contains("outputFormat=GTiff", error.RedactedRequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropagatesACallerCancelledTokenUnwrappedOnTheSecondRequest()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(HybridResponder(
            HappyPathAaiGridText,
            (_, cancellationToken) =>
            {
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return EmptyResponse(HttpStatusCode.OK);
            }));
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
    public async Task SecondRequestReturningAZipBodyFailsAsAnUnexpectedGeoTiffResponse()
    {
        byte[] zipBytes = CreateZipArchive(("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")));
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, (_, _) => ZipResponse(zipBytes)));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("GeoTIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRequestReturningAGzipBodyFailsAsAnUnexpectedGeoTiffResponse()
    {
        byte[] gzipBytes = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00];
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, (_, _) => BinaryResponse(HttpStatusCode.OK, gzipBytes, "application/gzip")));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("GeoTIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRequestReturningAnEmptyBodyFailsAsAnUnexpectedGeoTiffResponse()
    {
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, (_, _) => EmptyResponse(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("GeoTIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRequestOversizeBodyFailsAsAnUnexpectedResponseException()
    {
        byte[] oversizeTiffBytes = BuildTiffBytes(HappyPathScenario with { Citation = new string('x', 4096) });
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, (_, _) => BinaryResponse(HttpStatusCode.OK, oversizeTiffBytes, "image/tiff")));
        using var httpClient = new HttpClient(handler);
        var options = new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = 150 };
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey, options);

        OpenTopographyUnexpectedResponseException error = await Assert.ThrowsAsync<OpenTopographyUnexpectedResponseException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<object[]> GeoKeyGapScenarios()
    {
        yield return ["3072", "MissingProjectedCode"];
        yield return ["1024", "UnsupportedModelType"];
        // Unlike every other row here, an unsupported projected code's message names the observed EPSG code
        // itself (see OpenTopographyUsgs1mSource.ValidateGeoTiffMetadata), not the GeoKey number, since
        // Issue #22 replaced "GeoTIFF GeoKey 3072 (ProjectedCSTypeGeoKey) is EPSG:<code>" with a message aimed
        // at an operator rather than a GeoKey debugger.
        yield return ["EPSG:32615", "UnsupportedProjectedCode"];
        yield return ["3076", "UnsupportedLinearUnits"];
        yield return ["1025", "UnsupportedRasterType"];
        yield return ["33550", "MissingPixelScale"];
        yield return ["33922", "MissingTiepoint"];
        yield return ["42113", "MissingNoData"];
        yield return ["42113", "NonNumericNoData"];
    }

    [Theory]
    [MemberData(nameof(GeoKeyGapScenarios))]
    public async Task GeoKeyGapInTheMetadataResponseFailsNamingTheKey(string expectedKeyToken, string scenarioKey)
    {
        byte[] tiffBytes = BuildTiffBytes(GeoKeyGapScenarioLookup[scenarioKey]);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(expectedKeyToken, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> GridDisagreementFailureScenarios()
    {
        yield return ["ImageWidth", "ImageWidth"];
        yield return ["ImageLength", "ImageLength"];
        yield return ["ModelPixelScale X", "ScaleX"];
        yield return ["ModelPixelScale Y", "ScaleY"];
        yield return ["lower-left X", "TiepointX"];
        yield return ["lower-left Y", "TiepointY"];
        yield return ["GDAL_NODATA", "NoDataMismatch"];
    }

    [Theory]
    [MemberData(nameof(GridDisagreementFailureScenarios))]
    public async Task GridDisagreementBetweenTheTwoResponsesFailsNamingTheField(string expectedFieldToken, string scenarioKey)
    {
        byte[] tiffBytes = BuildTiffBytes(GridDisagreementScenarioLookup[scenarioKey]);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(expectedFieldToken, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CornerAgreementWithinToleranceStillSucceeds()
    {
        TiffScenario scenario = HappyPathScenario with { TiepointX = 500000d + 5e-7, TiepointY = 4000004d - 5e-7 };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(OpenTopographyReferenceSource.GeoTiffGeoKeys, result.Evidence.ReferenceSource);
    }

    [Fact]
    public async Task PixelIsPointTiepointShiftAgreesWithTheAaiGridCornerAndSucceeds()
    {
        TiffScenario scenario = HappyPathScenario with { RasterType = 2, TiepointX = 500001d, TiepointY = 4000003d };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal("PixelIsPoint", result.Evidence.MetadataRequest!.RasterType);
    }

    [Fact]
    public async Task CenterAnchoredAaiGridHeaderAgreesWithAPixelIsAreaGeoTiffAndSucceeds()
    {
        const string centerAnchoredAaiGridText = "ncols 3\nnrows 2\nxllcenter 500001\nyllcenter 4000001\ncellsize 2\nNODATA_value -9999\n1 2 3\n4 5 -9999\n";
        byte[] tiffBytes = BuildTiffBytes(HappyPathScenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(centerAnchoredAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographyUsgs1mAcquisition result = await source.AcquireDetailedAsync(SmallRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(OpenTopographyReferenceSource.GeoTiffGeoKeys, result.Evidence.ReferenceSource);
    }

    [Fact]
    public async Task GeoTiffCitationMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        TiffScenario scenario = HappyPathScenario with { Citation = FakeKey };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("citation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
    }

    [Fact]
    public async Task GeoTiffGeographicCitationMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        // GeoTiffCitationMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt above only
        // exercises the guard on Citation (GeoKey 1026); HandleBareAaiGridAsync runs the identical guard
        // against GeographicCitation (GeoKey 2049) and ProjectedCitation (GeoKey 3073), which were otherwise
        // unverified.
        TiffScenario scenario = HappyPathScenario with { GeographicCitation = FakeKey };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("geographic citation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
    }

    [Fact]
    public async Task GeoTiffProjectedCitationMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt()
    {
        // See GeoTiffGeographicCitationMatchingTheApiKeyFailsAsASourceMetadataExceptionInsteadOfLeakingIt
        // immediately above: this covers the third and last of the three citation GeoKeys, ProjectedCitation
        // (GeoKey 3073).
        TiffScenario scenario = HappyPathScenario with { ProjectedCitation = FakeKey };
        byte[] tiffBytes = BuildTiffBytes(scenario);
        var handler = new FakeHttpMessageHandler(HybridResponder(HappyPathAaiGridText, tiffBytes));
        using var httpClient = new HttpClient(handler);
        OpenTopographyUsgs1mSource source = CreateSource(httpClient, FakeKey);

        OpenTopographySourceMetadataException error = await Assert.ThrowsAsync<OpenTopographySourceMetadataException>(
            () => source.AcquireAsync(SmallRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("projected citation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
        AssertExceptionChainDoesNotContainTheKey(error);
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

    private static Task<HttpResponseMessage> BinaryResponseWithFileName(HttpStatusCode statusCode, byte[] body, string fileName, string mediaType)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(body),
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        return Task.FromResult(response);
    }

    /// <summary>
    /// The AAIGrid text body for a small, self-consistent 3x2 hybrid-flow scenario: cellsize 2, lower-left
    /// corner (500000, 4000000). Paired with <see cref="HappyPathScenario"/>, whose default GeoTIFF tiepoint
    /// (500000, 4000004, PixelIsArea) derives the identical lower-left corner, so the two responses agree.
    /// </summary>
    private const string HappyPathAaiGridText = "ncols 3\nnrows 2\nxllcorner 500000\nyllcorner 4000000\ncellsize 2\nNODATA_value -9999\n1 2 3\n4 5 -9999\n";

    /// <summary>The default GeoTIFF metadata scenario matching <see cref="HappyPathAaiGridText"/> exactly.</summary>
    private static readonly TiffScenario HappyPathScenario = new();

    /// <summary>
    /// Named <see cref="TiffScenario"/> variations for <see cref="GeoKeyGapScenarios"/>, keyed by a
    /// serializable string instead of the record itself. xunit.v3 needs every discovered theory data value to
    /// be serializable (implementing xunit's <c>IXunitSerializable</c> or via a registered serializer) to
    /// give each row its own discovered test identity, and <see cref="TiffScenario"/> cannot satisfy that: its only
    /// constructor is a 14-arity positional primary constructor, so it has no true parameterless constructor
    /// at the IL level even though every parameter has a default value. This field must stay declared after
    /// <see cref="HappyPathScenario"/> above, since its initializer depends on that field already being set.
    /// </summary>
    private static readonly Dictionary<string, TiffScenario> GeoKeyGapScenarioLookup = new()
    {
        ["MissingProjectedCode"] = HappyPathScenario with { ProjectedCode = null },
        ["UnsupportedModelType"] = HappyPathScenario with { ModelType = 2 },
        ["UnsupportedProjectedCode"] = HappyPathScenario with { ProjectedCode = 32615 },
        ["UnsupportedLinearUnits"] = HappyPathScenario with { LinearUnitsCode = 9002 },
        ["UnsupportedRasterType"] = HappyPathScenario with { RasterType = 3 },
        ["MissingPixelScale"] = HappyPathScenario with { IncludePixelScale = false },
        ["MissingTiepoint"] = HappyPathScenario with { IncludeTiepoint = false },
        ["MissingNoData"] = HappyPathScenario with { NoDataText = null },
        ["NonNumericNoData"] = HappyPathScenario with { NoDataText = "not-a-number" },
    };

    /// <summary>
    /// Named <see cref="TiffScenario"/> variations for <see cref="GridDisagreementFailureScenarios"/>; see
    /// <see cref="GeoKeyGapScenarioLookup"/> for why a lookup keyed by string, not the record itself, is
    /// required.
    /// </summary>
    private static readonly Dictionary<string, TiffScenario> GridDisagreementScenarioLookup = new()
    {
        ["ImageWidth"] = HappyPathScenario with { ImageWidth = 4 },
        ["ImageLength"] = HappyPathScenario with { ImageLength = 3 },
        ["ScaleX"] = HappyPathScenario with { ScaleX = 3d },
        ["ScaleY"] = HappyPathScenario with { ScaleY = 3d },
        ["TiepointX"] = HappyPathScenario with { TiepointX = 500000.01d },
        ["TiepointY"] = HappyPathScenario with { TiepointY = 4000004.01d },
        ["NoDataMismatch"] = HappyPathScenario with { NoDataText = "-1234" },
    };

    /// <summary>
    /// A minimal, mutable description of a GeoTIFF metadata response's tags and GeoKeys, used with
    /// <see cref="BuildTiffBytes"/> to script hybrid-flow test scenarios (happy path, GeoKey gaps, and
    /// same-grid disagreements) as small, targeted <c>with</c>-expression variations of
    /// <see cref="HappyPathScenario"/> instead of one bespoke <see cref="TiffBuilder"/> call per test.
    /// </summary>
    public sealed record TiffScenario(
        ushort ImageWidth = 3,
        ushort ImageLength = 2,
        ushort? ModelType = 1,
        ushort? RasterType = 1,
        ushort? ProjectedCode = 26915,
        ushort? LinearUnitsCode = 9001,
        double ScaleX = 2d,
        double ScaleY = 2d,
        double TiepointX = 500000d,
        double TiepointY = 4000004d,
        bool IncludePixelScale = true,
        bool IncludeTiepoint = true,
        string? NoDataText = "-9999",
        string? Citation = null,
        bool BigEndian = false,
        string? GeographicCitation = null,
        string? ProjectedCitation = null);

    private static byte[] BuildTiffBytes(TiffScenario scenario)
    {
        TiffBuilder builder = new TiffBuilder(bigEndian: scenario.BigEndian)
            .WithShort(256, scenario.ImageWidth)
            .WithShort(257, scenario.ImageLength);

        if (scenario.IncludePixelScale)
        {
            builder = builder.WithDoubles(33550, scenario.ScaleX, scenario.ScaleY, 0d);
        }

        if (scenario.IncludeTiepoint)
        {
            builder = builder.WithDoubles(33922, 0d, 0d, 0d, scenario.TiepointX, scenario.TiepointY, 0d);
        }

        if (scenario.NoDataText is not null)
        {
            builder = builder.WithAscii(42113, scenario.NoDataText);
        }

        List<(ushort KeyId, ushort Location, ushort Count, ushort ValueOffset)> keys = [];
        if (scenario.ModelType is ushort modelType)
        {
            keys.Add((1024, 0, 1, modelType));
        }

        if (scenario.RasterType is ushort rasterType)
        {
            keys.Add((1025, 0, 1, rasterType));
        }

        if (scenario.ProjectedCode is ushort projectedCode)
        {
            keys.Add((3072, 0, 1, projectedCode));
        }

        if (scenario.LinearUnitsCode is ushort linearUnitsCode)
        {
            keys.Add((3076, 0, 1, linearUnitsCode));
        }

        // All three citation GeoKeys (1026, 2049, 3073) resolve through the same shared GeoAsciiParams
        // (34737) tag, each by its own offset/count -- exactly per the GeoTIFF spec and
        // GeoTiffMetadataReader.ResolveGeoAsciiSlice -- so every non-null citation here packs into one
        // concatenated, '|'-terminated blob instead of one WithAscii(34737, ...) call per citation.
        string geoAscii = "";
        void AppendCitation(ushort keyId, string? text)
        {
            if (text is null)
            {
                return;
            }

            var offset = (ushort)geoAscii.Length;
            geoAscii += text + "|";
            keys.Add((keyId, 34737, (ushort)(text.Length + 1), offset));
        }

        AppendCitation(1026, scenario.Citation);
        AppendCitation(2049, scenario.GeographicCitation);
        AppendCitation(3073, scenario.ProjectedCitation);

        if (geoAscii.Length > 0)
        {
            builder = builder.WithAscii(34737, geoAscii);
        }

        return builder.WithGeoKeyDirectory([.. keys]).Build();
    }

    /// <summary>
    /// Builds a responder that discriminates on the request's <c>outputFormat</c> query value: an
    /// <c>AAIGrid</c> request always gets <paramref name="aaiGridText"/>; a <c>GTiff</c> request always gets
    /// a 200 GeoTIFF response built from <paramref name="tiff"/>.
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HybridResponder(string aaiGridText, byte[] tiff) =>
        HybridResponder(aaiGridText, (_, _) => BinaryResponse(HttpStatusCode.OK, tiff, "image/tiff"));

    /// <summary>
    /// Builds a responder that discriminates on the request's <c>outputFormat</c> query value: an
    /// <c>AAIGrid</c> request always gets a 200 response with <paramref name="aaiGridText"/>; a <c>GTiff</c>
    /// request is handled by <paramref name="metadataResponder"/>, letting a test script any second-response
    /// shape (a non-200 status, a transport failure, a non-GeoTIFF body, and so on).
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HybridResponder(
        string aaiGridText,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> metadataResponder) =>
        (request, cancellationToken) => GetQueryValue(request, "outputFormat") == "GTiff"
            ? metadataResponder(request, cancellationToken)
            : TextResponse(HttpStatusCode.OK, aaiGridText);

    /// <summary>
    /// Builds a responder that discriminates on the request's <c>outputFormat</c> query value like the other
    /// <c>HybridResponder</c> overloads, but also lets a test script the first (data) response's own headers
    /// -- Content-Disposition, Content-Type -- instead of always sending a plain 200 <c>text/plain</c>
    /// response.
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HybridResponder(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> aaiGridResponder,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> metadataResponder) =>
        (request, cancellationToken) => GetQueryValue(request, "outputFormat") == "GTiff"
            ? metadataResponder(request, cancellationToken)
            : aaiGridResponder(request, cancellationToken);

    private static string? GetQueryValue(HttpRequestMessage request, string name)
    {
        string query = request.RequestUri!.Query.TrimStart('?');
        foreach (string pair in query.Split('&'))
        {
            int equalsIndex = pair.IndexOf('=');
            string key = equalsIndex < 0 ? pair : pair[..equalsIndex];
            if (key == name)
            {
                return equalsIndex < 0 ? null : pair[(equalsIndex + 1)..];
            }
        }

        return null;
    }

    /// <summary>Asserts a hybrid flow's first (data) request has the documented query shape for <c>outputFormat=AAIGrid</c>.</summary>
    private static void AssertAaiGridRequest(HttpRequestMessage request) => AssertRequestHasOutputFormatAndKey(request, "AAIGrid");

    /// <summary>Asserts a hybrid flow's second (metadata) request has the documented query shape for <c>outputFormat=GTiff</c>.</summary>
    private static void AssertMetadataRequest(HttpRequestMessage request) => AssertRequestHasOutputFormatAndKey(request, "GTiff");

    private static void AssertRequestHasOutputFormatAndKey(HttpRequestMessage request, string expectedOutputFormat)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedOutputFormat, GetQueryValue(request, "outputFormat"));
        string query = request.RequestUri!.Query.TrimStart('?');
        Assert.Contains($"API_Key={Uri.EscapeDataString(FakeKey)}", query, StringComparison.Ordinal);
    }

    /// <summary>Asserts two hybrid-flow requests share identical datasetName/south/north/west/east values.</summary>
    private static void AssertIdenticalAreaParameters(HttpRequestMessage first, HttpRequestMessage second)
    {
        foreach (string name in new[] { "datasetName", "south", "north", "west", "east" })
        {
            Assert.Equal(GetQueryValue(first, name), GetQueryValue(second, name));
        }
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
