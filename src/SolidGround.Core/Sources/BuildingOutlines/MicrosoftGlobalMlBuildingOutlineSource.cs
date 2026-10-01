using System.Globalization;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;

namespace SolidGround.Core.Sources.BuildingOutlines;

/// <summary>
/// Bounded client-direct reader for Microsoft's pinned 2026-08-13 Global ML Building Footprints release.
/// It sends only static manifest and L9-tile URLs; neither the display bounds nor any address, parcel id, API
/// key, feature property, height, or door-related value is sent or retained.
/// </summary>
public sealed class MicrosoftGlobalMlBuildingOutlineSource : IBuildingOutlineSource
{
    public const string ManifestSha256 = "8E479A213F6B9C4670CD80B036B06D6DEFEA613F1BB6B14417DB26E82AA137F2";
    private const string Release = "2026-08-13";
    private const string ManifestHost = "bfppub.blob.core.windows.net";
    private const string TileHost = "bfppub.z5.web.core.windows.net";
    private const int MaximumManifestBytes = 8 * 1024 * 1024;
    private const int MaximumCompressedTileBytes = 25 * 1024 * 1024;
    private const int MaximumCompressedTotalBytes = 40 * 1024 * 1024;
    private const int MaximumDecompressedTileBytes = 128 * 1024 * 1024;
    private const int MaximumDecompressedTotalBytes = 160 * 1024 * 1024;
    private const int MaximumLineBytes = 256 * 1024;
    private const int MaximumFeaturesPerTile = 250_000;
    private const int MaximumRetainedFeatures = 5_000;
    private const int MaximumCoordinatesPerFeature = 16_384;
    private const int MaximumRingsPerFeature = 32;
    private const int MaximumPartsPerFeature = 16;
    private const int MaximumTiles = 4;
    private static readonly Uri ManifestUri = new($"https://{ManifestHost}/%24web/{Release}/dataset-links.csv", UriKind.Absolute);
    private static readonly HorizontalReference Wgs84 = new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
    private static readonly ConcurrentDictionary<string, TileIndex> ValidatedReleaseIndexes = new(StringComparer.Ordinal);
    private readonly HttpClient client;
    private readonly string expectedManifestSha256;
    private readonly object manifestGate = new();
    private Task<TileIndex>? manifestTask;

    /// <summary>Creates the production source with the reviewed manifest digest.</summary>
    public MicrosoftGlobalMlBuildingOutlineSource(HttpClient client)
        : this(client, ManifestSha256)
    {
    }

    /// <summary>
    /// Creates a source with an explicit reviewed digest. This overload exists for synthetic offline tests and
    /// for a future reviewed release bump; product code uses <see cref="MicrosoftGlobalMlBuildingOutlineSource(HttpClient)"/>.
    /// </summary>
    public MicrosoftGlobalMlBuildingOutlineSource(HttpClient client, string expectedManifestSha256)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        if (expectedManifestSha256.Length != 64 || !expectedManifestSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The expected manifest digest must be a SHA-256 hex value.", nameof(expectedManifestSha256));
        }

        this.expectedManifestSha256 = expectedManifestSha256.ToUpperInvariant();
    }

    /// <summary>Creates the dedicated no-redirect HTTP client required by this static-host source.</summary>
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<BuildingOutlineAcquisition> GetAsync(Wgs84BoundingBoxAoi bounds, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        token.ThrowIfCancellationRequested();

        BuildingOutlineProvenance emptyProvenance = CreateProvenance([]);
        Level9QuadKey[] keys;
        try
        {
            keys = WebMercatorTileSelector.Select(bounds);
            if (keys.Length > MaximumTiles)
            {
                return Unavailable(emptyProvenance, "Building outlines are unavailable because this display area covers too many source tiles.");
            }
        }
        catch (ArgumentException)
        {
            return Unavailable(emptyProvenance, "Building outlines are unavailable for this display area.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken requestToken = timeout.Token;
        try
        {
            TileIndex index = await GetManifestAsync(requestToken).ConfigureAwait(false);
            List<TileEntry> tiles = [];
            foreach (Level9QuadKey key in keys)
            {
                if (!index.TryGet(key, out TileEntry tile))
                {
                    return Unavailable(CreateProvenance(keys), "Building outlines are unavailable for this source coverage.");
                }

                tiles.Add(tile);
            }

            if (tiles.Any(tile => tile.CompressedBytes > MaximumCompressedTileBytes) || tiles.Sum(tile => tile.CompressedBytes) > MaximumCompressedTotalBytes)
            {
                return Unavailable(CreateProvenance(keys), "Building outlines are unavailable because this source tile set exceeds its download limit.");
            }

            var counters = new TransferCounters();
            List<PolygonalRegion> outlines = [];
            foreach (TileEntry tile in tiles)
            {
                await ReadTileAsync(tile, bounds, outlines, counters, requestToken).ConfigureAwait(false);
            }

            return new BuildingOutlineAcquisition(outlines, CreateProvenance(keys));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Unavailable(CreateProvenance(keys), "Building outlines are unavailable because the source did not respond in time.");
        }
        catch (LimitExceededException)
        {
            return Unavailable(CreateProvenance(keys), "Building outlines are unavailable because the source response exceeded a safety limit.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // All remote/format failures are optional-context failures. Do not surface server text, URL, or a
            // feature payload: those can contain an operator's coarse location or untrusted source content.
            return Unavailable(CreateProvenance(keys), "Building outlines are temporarily unavailable.");
        }
    }

    private async Task<TileIndex> GetManifestAsync(CancellationToken token)
    {
        Task<TileIndex> task;
        lock (manifestGate)
        {
            task = manifestTask ??= LoadManifestAsync(token);
        }

        try
        {
            return await task.WaitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            lock (manifestGate)
            {
                if (ReferenceEquals(task, manifestTask))
                {
                    manifestTask = null;
                }
            }

            throw;
        }
    }

    private async Task<TileIndex> LoadManifestAsync(CancellationToken token)
    {
        if (ValidatedReleaseIndexes.TryGetValue(expectedManifestSha256, out TileIndex? cached))
        {
            return cached;
        }

        using HttpResponseMessage response = await SendAsync(ManifestUri, ManifestHost, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new FormatException("The reviewed building-outline manifest was unavailable.");
        }

        byte[] bytes = await ReadBoundedBytesAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), MaximumManifestBytes, token).ConfigureAwait(false);
        string actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedManifestSha256, StringComparison.Ordinal))
        {
            throw new FormatException("The reviewed building-outline manifest did not match its pinned digest.");
        }

        TileIndex parsed = TileIndex.Parse(Encoding.UTF8.GetString(bytes));
        return ValidatedReleaseIndexes.GetOrAdd(expectedManifestSha256, parsed);
    }

    private async Task ReadTileAsync(TileEntry tile, Wgs84BoundingBoxAoi bounds, List<PolygonalRegion> outlines, TransferCounters counters, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync(tile.Uri, TileHost, token).ConfigureAwait(false);
        string[] contentEncodings = response.Content.Headers.ContentEncoding.ToArray();
        if (response.StatusCode != HttpStatusCode.OK || contentEncodings.Length > 1 || contentEncodings.Any(value => !string.Equals(value, "gzip", StringComparison.OrdinalIgnoreCase)))
        {
            throw new FormatException("The building-outline tile response had an unexpected content encoding.");
        }

        if (response.Content.Headers.ContentLength is long length && (length > MaximumCompressedTileBytes || counters.CompressedBytes + length > MaximumCompressedTotalBytes))
        {
            throw new LimitExceededException();
        }

        await using Stream raw = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var compressed = new CountingStream(raw, MaximumCompressedTileBytes, MaximumCompressedTotalBytes, counters, compressed: true);
        byte[] magic = new byte[2];
        await compressed.ReadExactlyAsync(magic, token).ConfigureAwait(false);
        if (magic[0] != 0x1f || magic[1] != 0x8b)
        {
            throw new FormatException("The building-outline tile payload was not gzip data.");
        }

        await using var prefixed = new PrefixStream(magic, compressed);
        await using var gzip = new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: false);
        await using var decompressed = new CountingStream(gzip, MaximumDecompressedTileBytes, MaximumDecompressedTotalBytes, counters, compressed: false);
        var reader = new BoundedUtf8LineReader(decompressed, MaximumLineBytes);
        int features = 0;
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is byte[] line)
        {
            token.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                continue;
            }

            if (++features > MaximumFeaturesPerTile)
            {
                throw new LimitExceededException();
            }

            PolygonalRegion? outline = BuildingOutlineGeoJsonLineReader.Read(line, bounds);
            if (outline is not null)
            {
                if (outlines.Count == MaximumRetainedFeatures)
                {
                    throw new LimitExceededException();
                }

                outlines.Add(outline);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, string expectedHost, CancellationToken token)
    {
        ValidateStaticUri(uri, expectedHost);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new FormatException("Building-outline redirects are not permitted.");
        }

        Uri? finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is not null && !UriEquals(finalUri, uri))
        {
            response.Dispose();
            throw new FormatException("Building-outline redirects are not permitted.");
        }

        return response;
    }

    private static bool UriEquals(Uri left, Uri right) => Uri.Compare(left, right, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.Ordinal) == 0;

    private static void ValidateStaticUri(Uri uri, string expectedHost)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, expectedHost, StringComparison.Ordinal) || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new ArgumentException("The building-outline source URL is not an approved static HTTPS URL.", nameof(uri));
        }

        string expectedPrefix = expectedHost == TileHost ? $"/{Release}/global-buildings.geojsonl/" : $"/%24web/{Release}/dataset-links.csv";
        if (expectedHost == TileHost ? !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith(".csv.gz", StringComparison.Ordinal) : uri.AbsolutePath != expectedPrefix)
        {
            throw new ArgumentException("The building-outline source URL has an unexpected release path.", nameof(uri));
        }
    }

    private BuildingOutlineProvenance CreateProvenance(IEnumerable<Level9QuadKey> keys) => new(
        "Microsoft", Release, "CDLA-Permissive-2.0", new Uri("https://cdla.dev/permissive-2-0/"),
        "The Microsoft Global ML Building Footprints dataset is freely available for download and use under CDLA Permissive 2.0.",
        "Microsoft Global ML Building Footprints (2026-08-13). Outlines are approximate visual context, not legal or surveyed building boundaries.",
        expectedManifestSha256, keys.Select(key => key.Value).ToArray());

    private static BuildingOutlineAcquisition Unavailable(BuildingOutlineProvenance provenance, string reason) => new([], provenance, reason);

    private static async Task<byte[]> ReadBoundedBytesAsync(Stream stream, int maximumBytes, CancellationToken token)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximumBytes)
            {
                throw new LimitExceededException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private sealed class TransferCounters
    {
        public long CompressedBytes { get; set; }
        public long DecompressedBytes { get; set; }
    }

    private sealed class CountingStream(Stream inner, long perStreamMaximum, long totalMaximum, TransferCounters counters, bool compressed) : Stream
    {
        private long bytesRead;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => Count(await inner.ReadAsync(buffer, token).ConfigureAwait(false));
        private int Count(int read)
        {
            if (read == 0) return 0;
            long total = compressed ? counters.CompressedBytes : counters.DecompressedBytes;
            if (read > perStreamMaximum - bytesRead || total > totalMaximum - read)
            {
                throw new LimitExceededException();
            }

            bytesRead += read;
            if (compressed)
            {
                counters.CompressedBytes += read;
            }
            else
            {
                counters.DecompressedBytes += read;
            }
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Replays the validated gzip magic bytes without taking ownership of the counted network stream.</summary>
    private sealed class PrefixStream(byte[] prefix, Stream inner) : Stream
    {
        private int offset;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int bufferOffset, int count) => Read(buffer.AsSpan(bufferOffset, count));
        public override int Read(Span<byte> buffer)
        {
            int copied = CopyPrefix(buffer);
            return copied != 0 ? copied : inner.Read(buffer);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            int copied = CopyPrefix(buffer.Span);
            return copied != 0 ? copied : await inner.ReadAsync(buffer, token).ConfigureAwait(false);
        }
        private int CopyPrefix(Span<byte> destination)
        {
            int available = prefix.Length - offset;
            int copied = Math.Min(destination.Length, available);
            if (copied != 0)
            {
                prefix.AsSpan(offset, copied).CopyTo(destination);
                offset += copied;
            }

            return copied;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BoundedUtf8LineReader(Stream stream, int maximumLineBytes)
    {
        private readonly byte[] readBuffer = new byte[8192];
        private int offset;
        private int count;

        public async Task<byte[]?> ReadLineAsync(CancellationToken token)
        {
            using var line = new MemoryStream();
            while (true)
            {
                if (offset == count)
                {
                    count = await stream.ReadAsync(readBuffer, token).ConfigureAwait(false);
                    offset = 0;
                    if (count == 0)
                    {
                        return line.Length == 0 ? null : line.ToArray();
                    }
                }

                byte value = readBuffer[offset++];
                if (value == (byte)'\n')
                {
                    byte[] result = line.ToArray();
                    return result.Length > 0 && result[^1] == (byte)'\r' ? result[..^1] : result;
                }

                if (line.Length == maximumLineBytes)
                {
                    throw new LimitExceededException();
                }

                line.WriteByte(value);
            }
        }
    }

    private sealed class LimitExceededException : Exception;

    private sealed record TileEntry(Level9QuadKey Key, Uri Uri, long CompressedBytes);

    private sealed class TileIndex(Dictionary<string, TileEntry> entries)
    {
        public bool TryGet(Level9QuadKey key, out TileEntry entry) => entries.TryGetValue(key.Value, out entry!);

        public static TileIndex Parse(string csv)
        {
            string[] lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2 || !string.Equals(lines[0], "Location,QuadKey,Url,Size,UploadDate", StringComparison.Ordinal))
            {
                throw new FormatException("The building-outline manifest has an unexpected schema.");
            }

            var entries = new Dictionary<string, TileEntry>(StringComparer.Ordinal);
            foreach (string line in lines.Skip(1))
            {
                string[] fields = line.Split(',');
                if (fields.Length != 5 || fields[0].Length == 0 || !DateOnly.TryParseExact(fields[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) || date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != Release)
                {
                    throw new FormatException("The building-outline manifest contains an invalid row.");
                }

                var key = new Level9QuadKey(fields[1]);
                if (!Uri.TryCreate(fields[2], UriKind.Absolute, out Uri? uri))
                {
                    throw new FormatException("The building-outline manifest contains an invalid tile URL.");
                }

                ValidateStaticUri(uri, TileHost);
                long bytes = ParseSize(fields[3]);
                if (!entries.TryAdd(key.Value, new TileEntry(key, uri, bytes)))
                {
                    throw new FormatException("The building-outline manifest contains duplicate tile keys.");
                }
            }

            return new TileIndex(entries);
        }

        private static long ParseSize(string text)
        {
            string value = text.Trim();
            string[] suffixes = ["GB", "MB", "KB", "B"];
            string? suffix = suffixes.FirstOrDefault(candidate => value.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
            if (suffix is null || !double.TryParse(value[..^suffix.Length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number <= 0d)
            {
                throw new FormatException("The building-outline manifest contains an invalid tile size.");
            }

            double factor = suffix.ToUpperInvariant() switch { "GB" => 1024d * 1024d * 1024d, "MB" => 1024d * 1024d, "KB" => 1024d, _ => 1d };
            double bytes = Math.Ceiling(number * factor);
            if (bytes > long.MaxValue)
            {
                throw new FormatException("The building-outline manifest contains an oversized tile.");
            }

            return (long)bytes;
        }
    }

    private static class WebMercatorTileSelector
    {
        private const double MaximumLatitude = 85.0511287798066d;

        public static Level9QuadKey[] Select(Wgs84BoundingBoxAoi bounds)
        {
            if (bounds.SouthLatitude < -MaximumLatitude || bounds.NorthLatitude > MaximumLatitude)
            {
                throw new ArgumentException("Web Mercator tiles do not cover this latitude.", nameof(bounds));
            }

            int width = 1 << Level9QuadKey.Level;
            SortedSet<int> xs = IntersectingIndices(ProjectX(bounds.WestLongitude, width), ProjectX(bounds.EastLongitude, width), width);
            SortedSet<int> ys = IntersectingIndices(ProjectY(bounds.NorthLatitude, width), ProjectY(bounds.SouthLatitude, width), width);
            var keys = new List<Level9QuadKey>();
            foreach (int y in ys)
            {
                foreach (int x in xs)
                {
                    keys.Add(ToQuadKey(x, y));
                }
            }

            return keys.OrderBy(key => key.Value, StringComparer.Ordinal).ToArray();
        }

        private static SortedSet<int> IntersectingIndices(double start, double end, int width)
        {
            var indices = new SortedSet<int>();
            AddCoordinate(start);
            AddCoordinate(end);
            int first = (int)Math.Floor(Math.Min(start, end));
            int last = (int)Math.Floor(Math.Max(start, end));
            for (int index = first; index <= last; index++) indices.Add(Math.Clamp(index, 0, width - 1));
            return indices;

            void AddCoordinate(double coordinate)
            {
                int index = Math.Clamp((int)Math.Floor(coordinate), 0, width - 1);
                indices.Add(index);
                if (Math.Abs(coordinate - Math.Round(coordinate)) < 1e-12 && index > 0) indices.Add(index - 1);
            }
        }

        private static double ProjectX(double longitude, int width) => (longitude + 180d) / 360d * width;
        private static double ProjectY(double latitude, int width)
        {
            double radians = latitude * Math.PI / 180d;
            return (1d - Math.Log(Math.Tan(radians) + (1d / Math.Cos(radians))) / Math.PI) / 2d * width;
        }

        private static Level9QuadKey ToQuadKey(int x, int y)
        {
            Span<char> characters = stackalloc char[Level9QuadKey.Level];
            for (int level = Level9QuadKey.Level; level > 0; level--)
            {
                int mask = 1 << (level - 1);
                int digit = (x & mask) != 0 ? 1 : 0;
                if ((y & mask) != 0) digit += 2;
                characters[Level9QuadKey.Level - level] = (char)('0' + digit);
            }

            return new Level9QuadKey(new string(characters));
        }
    }

    private static class BuildingOutlineGeoJsonLineReader
    {
        public static PolygonalRegion? Read(byte[] line, Wgs84BoundingBoxAoi bounds)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "Feature" || !root.TryGetProperty("geometry", out JsonElement geometry) || geometry.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The building-outline response contains an invalid GeoJSON feature.");
            }

            ValidateGeometryShape(geometry);
            PolygonalRegion parsed = ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, geometry.GetRawText(), Wgs84);
            var factory = GeometryInterop.Services.CreateGeometryFactory();
            NetTopologySuite.Geometries.Geometry rectangle = factory.ToGeometry(new Envelope(bounds.WestLongitude, bounds.EastLongitude, bounds.SouthLatitude, bounds.NorthLatitude));
            if (!parsed.Geometry.Intersects(rectangle))
            {
                return null;
            }

            NetTopologySuite.Geometries.Geometry clipped = parsed.Geometry.Intersection(rectangle);
            return clipped.IsEmpty || clipped.Dimension != Dimension.Surface ? null : PolygonalRegion.FromGeometry(clipped, Wgs84);
        }

        private static void ValidateGeometryShape(JsonElement geometry)
        {
            if (!geometry.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || !geometry.TryGetProperty("coordinates", out JsonElement coordinates) || coordinates.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The building-outline feature has invalid geometry.");
            }

            int parts = 0, rings = 0, coordinatesCount = 0;
            IEnumerable<JsonElement> polygons = type.GetString() switch
            {
                "Polygon" => [coordinates],
                "MultiPolygon" => coordinates.EnumerateArray(),
                _ => throw new FormatException("The building-outline feature uses an unsupported geometry type."),
            };
            foreach (JsonElement polygon in polygons)
            {
                if (++parts > MaximumPartsPerFeature || polygon.ValueKind != JsonValueKind.Array) throw new LimitExceededException();
                foreach (JsonElement ring in polygon.EnumerateArray())
                {
                    if (++rings > MaximumRingsPerFeature || ring.ValueKind != JsonValueKind.Array) throw new LimitExceededException();
                    int ringCoordinates = 0;
                    foreach (JsonElement position in ring.EnumerateArray())
                    {
                        if (++ringCoordinates > MaximumCoordinatesPerFeature || ++coordinatesCount > MaximumCoordinatesPerFeature || position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2 || !position[0].TryGetDouble(out double x) || !position[1].TryGetDouble(out double y) || !double.IsFinite(x) || !double.IsFinite(y)) throw new LimitExceededException();
                    }
                }
            }
        }
    }
}
