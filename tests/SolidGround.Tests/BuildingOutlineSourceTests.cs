using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SolidGround.Core.Aois;
using SolidGround.Core.Sources.BuildingOutlines;

namespace SolidGround.Tests;

public sealed class BuildingOutlineSourceTests
{
    [Fact]
    public async Task AcquiresAndClipsSyntheticPolygonWithoutSendingTheDisplayBounds()
    {
        const string manifest = "Location,QuadKey,Url,Size,UploadDate\nSynthetic,023111113,https://bfppub.z5.web.core.windows.net/2026-08-13/global-buildings.geojsonl/RegionName=Synthetic/quadkey=023111113/part-synthetic.csv.gz,100B,2026-08-13\n";
        const string feature = """{"type":"Feature","properties":{"height":999,"address":"must-not-escape"},"geometry":{"type":"Polygon","coordinates":[[[-90.05,40.01],[-90.01,40.01],[-90.01,40.03],[-90.05,40.03],[-90.05,40.01]]]}}""";
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.Host == "bfppub.blob.core.windows.net"
            ? TextResponse(HttpStatusCode.OK, manifest)
            : GzipResponse(HttpStatusCode.OK, feature + "\n", contentEncoding: null));
        using var client = new HttpClient(handler);
        var source = new MicrosoftGlobalMlBuildingOutlineSource(client, Digest(manifest));

        BuildingOutlineAcquisition result = await source.GetAsync(
            new Wgs84BoundingBoxAoi(-90.04, 40.005, -90.02, 40.025), TestContext.Current.CancellationToken);

        Assert.True(result.UnavailabilityReason is null, $"{result.UnavailabilityReason}; requested tiles: {string.Join(',', result.Provenance.TileKeys)}");
        PolygonalRegion outline = Assert.Single(result.Outlines);
        Assert.True(outline.Envelope.MinX >= -90.04);
        Assert.True(outline.Envelope.MaxX <= -90.02);
        Assert.Equal(["023111113"], result.Provenance.TileKeys);
        Assert.Equal("Microsoft", result.Provenance.Provider);
        Assert.DoesNotContain("address", result.Provenance.ToString(), StringComparison.OrdinalIgnoreCase);

        HttpRequestMessage tileRequest = handler.Requests[1];
        Assert.Equal("bfppub.z5.web.core.windows.net", tileRequest.RequestUri!.Host);
        Assert.Equal(string.Empty, tileRequest.RequestUri.Query);
        Assert.DoesNotContain("40.005", tileRequest.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("-90.04", tileRequest.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsNoOutlineWhenManifestDigestDoesNotMatch()
    {
        const string manifest = "Location,QuadKey,Url,Size,UploadDate\n";
        var handler = new FakeHttpMessageHandler(_ => TextResponse(HttpStatusCode.OK, manifest));
        using var client = new HttpClient(handler);
        var source = new MicrosoftGlobalMlBuildingOutlineSource(client);

        BuildingOutlineAcquisition result = await source.GetAsync(
            new Wgs84BoundingBoxAoi(-90.005, 40.005, -89.995, 40.025), TestContext.Current.CancellationToken);

        Assert.Empty(result.Outlines);
        Assert.NotNull(result.UnavailabilityReason);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain("bfppub", result.UnavailabilityReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvenanceRejectsTileKeysThatAreNotLevelNineQuadkeys()
    {
        Assert.Throws<ArgumentException>(() => new BuildingOutlineProvenance(
            "Microsoft", "2026-08-13", "CDLA-Permissive-2.0", new Uri("https://cdla.dev/permissive-2-0/"),
            "licence", "attribution", MicrosoftGlobalMlBuildingOutlineSource.ManifestSha256, ["123"]));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Deliberately injects a fatal runtime exception to verify that optional-source recovery does not swallow it.")]
    public async Task PropagatesAnOutOfMemoryExceptionFromTheTransport()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new OutOfMemoryException("synthetic"));
        using var client = new HttpClient(handler);
        var source = new MicrosoftGlobalMlBuildingOutlineSource(client);

        await Assert.ThrowsAsync<OutOfMemoryException>(() => source.GetAsync(
            new Wgs84BoundingBoxAoi(-90.04, 40.005, -90.02, 40.025), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsAnUnexpectedContentEncodingWithoutReturningPartialGeometry()
    {
        const string manifest = "Location,QuadKey,Url,Size,UploadDate\nSynthetic,023111113,https://bfppub.z5.web.core.windows.net/2026-08-13/global-buildings.geojsonl/RegionName=Synthetic/quadkey=023111113/part-synthetic.csv.gz,100B,2026-08-13\n";
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.Host == "bfppub.blob.core.windows.net"
            ? TextResponse(HttpStatusCode.OK, manifest)
            : GzipResponse(HttpStatusCode.OK, "{}\n", contentEncoding: "br"));
        using var client = new HttpClient(handler);
        var source = new MicrosoftGlobalMlBuildingOutlineSource(client, Digest(manifest));

        BuildingOutlineAcquisition result = await source.GetAsync(
            new Wgs84BoundingBoxAoi(-90.04, 40.005, -90.02, 40.025), TestContext.Current.CancellationToken);

        Assert.Empty(result.Outlines);
        Assert.NotNull(result.UnavailabilityReason);
    }

    private static HttpResponseMessage TextResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/csv"),
    };

    private static HttpResponseMessage GzipResponse(HttpStatusCode statusCode, string body, string? contentEncoding)
    {
        var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(body);
        }

        var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(bytes.ToArray()) };
        if (contentEncoding is not null)
        {
            response.Content.Headers.ContentEncoding.Add(contentEncoding);
        }
        return response;
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
