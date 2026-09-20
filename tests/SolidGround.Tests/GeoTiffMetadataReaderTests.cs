using System.Buffers.Binary;
using System.Text;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

public sealed class GeoTiffMetadataReaderTests
{
    [Fact]
    public void ReadsALittleEndianTiffWithInlineShortDimensionsAndOutOfLineGeoKeys()
    {
        const string citation = "Test Citation";
        const string geographicCitation = "Geo Citation";
        string geoAscii = $"{citation}|{geographicCitation}|";

        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 124)
            .WithShort(257, 117)
            .WithDoubles(33550, 1d, 1d, 0d)
            .WithDoubles(33922, 0d, 0d, 0d, [withheld], [withheld], 0d)
            .WithAscii(42113, "-9999")
            .WithAscii(34737, geoAscii)
            .WithGeoKeyDirectory(
                (1024, 0, 1, 1),
                (1025, 0, 1, 1),
                (3072, 0, 1, 26915),
                (1026, 34737, (ushort)(citation.Length + 1), 0),
                (2049, 34737, (ushort)(geographicCitation.Length + 1), (ushort)(citation.Length + 1)))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal(GeoTiffByteOrder.LittleEndian, metadata.ByteOrder);
        Assert.Equal(124u, metadata.ImageWidth);
        Assert.Equal(117u, metadata.ImageLength);
        Assert.Equal((ushort)1, metadata.ModelType);
        Assert.Equal((ushort)1, metadata.RasterType);
        Assert.Equal((ushort)26915, metadata.ProjectedCoordinateSystemCode);
        Assert.Equal(citation, metadata.Citation);
        Assert.Equal(geographicCitation, metadata.GeographicCitation);
        Assert.Equal((1d, 1d, 0d), metadata.ModelPixelScale);
        Assert.Equal((0d, 0d, 0d, [withheld], [withheld], 0d), metadata.ModelTiepoint);
        Assert.Equal("-9999", metadata.NoDataText);
    }

    [Fact]
    public void ReadsABigEndianTiffWithInlineLongDimensionsAndOutOfLineGeoKeys()
    {
        const string projectedCitation = "Projected Citation";
        string geoAscii = $"{projectedCitation}|";

        byte[] bytes = new TiffBuilder(bigEndian: true)
            .WithLong(256, 124)
            .WithLong(257, 117)
            .WithDoubles(33550, 1d, 1d, 0d)
            .WithDoubles(33922, 0d, 0d, 0d, [withheld], [withheld], 0d)
            .WithAscii(42113, "-999999")
            .WithAscii(34737, geoAscii)
            .WithGeoKeyDirectory(
                (1024, 0, 1, 1),
                (1025, 0, 1, 1),
                (3072, 0, 1, 26915),
                (3076, 0, 1, 9001),
                (4096, 0, 1, 5703),
                (3073, 34737, (ushort)(projectedCitation.Length + 1), 0))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal(GeoTiffByteOrder.BigEndian, metadata.ByteOrder);
        Assert.Equal(124u, metadata.ImageWidth);
        Assert.Equal(117u, metadata.ImageLength);
        Assert.Equal((ushort)9001, metadata.LinearUnitsCode);
        Assert.Equal((ushort)5703, metadata.VerticalCoordinateSystemCode);
        Assert.Equal(projectedCitation, metadata.ProjectedCitation);
        Assert.Equal("-999999", metadata.NoDataText);
    }

    [Fact]
    public void ReadsAPixelIsPointRasterType()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithGeoKeyDirectory((1025, 0, 1, 2))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal((ushort)2, metadata.RasterType);
    }

    [Fact]
    public void LeavesProjectedCoordinateSystemCodeNullWhenNoGeoKeySuppliesIt()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithGeoKeyDirectory((1024, 0, 1, 1))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Null(metadata.ProjectedCoordinateSystemCode);
    }

    [Fact]
    public void LeavesEveryGeoKeyDerivedPropertyNullWhenTheGeoKeyDirectoryIsAbsent()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Null(metadata.ModelType);
        Assert.Null(metadata.ProjectedCoordinateSystemCode);
        Assert.Null(metadata.ModelPixelScale);
        Assert.Null(metadata.NoDataText);
    }

    [Fact]
    public void IgnoresUnknownTags()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithShort(999, 42)
            .WithAscii(65000, "unrecognized tag data that is longer than four bytes")
            .WithGeoKeyDirectory((3072, 0, 1, 26915))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal(10u, metadata.ImageWidth);
        Assert.Equal((ushort)26915, metadata.ProjectedCoordinateSystemCode);
    }

    [Fact]
    public void IgnoresUnknownGeoKeysAtEveryTiffTagLocation()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithDoubles(34736, 1.5d, 2.5d)
            .WithAscii(34737, "unused|")
            .WithGeoKeyDirectory(
                (3072, 0, 1, 26915),
                (5000, 0, 1, 7),
                (5001, 34737, 7, 0),
                (5002, 34736, 1, 0))
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal((ushort)26915, metadata.ProjectedCoordinateSystemCode);
    }

    [Fact]
    public void RejectsAGeoKeyDirectoryWithAnUnsupportedVersion()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithRawGeoKeyDirectory(keyDirectoryVersion: 2, keyRevision: 1, minorRevision: 0)
            .Build();

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("KeyDirectoryVersion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABigTiffHeader()
    {
        byte[] bytes = new byte[8];
        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 43);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 8);

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("BigTIFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsBytesThatAreNotATiff()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("this is not a tiff file at all!");

        Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));
    }

    [Fact]
    public void RejectsATooShortBuffer()
    {
        byte[] bytes = [(byte)'I', (byte)'I', 42, 0];

        Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));
    }

    [Fact]
    public void RejectsATruncatedBufferThatEndsInsideTheIfdEntries()
    {
        byte[] full = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithDoubles(33550, 1d, 1d, 0d)
            .Build();

        // Cut off partway through the IFD's entry array itself (well before its out-of-line data).
        byte[] truncated = full[..14];

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(truncated));

        Assert.Contains("too short", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnOutOfLineValueOffsetThatPointsBeyondTheBuffer()
    {
        byte[] full = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithDoubles(33550, 1d, 1d, 0d)
            .Build();

        // The ModelPixelScale DOUBLE[3] value is written last, out-of-line; drop its final byte so the
        // recorded offset now points past the end of the buffer.
        byte[] truncated = full[..^1];

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(truncated));

        Assert.Contains("beyond the end", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsACountThatWouldOverflowTheValueLengthWithoutCrashing()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .WithShort(257, 10)
            .WithForcedEntry(33550, type: 12, count: uint.MaxValue, literalValueField: 0)
            .Build();

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("beyond the end", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAKnownTagWhoseTypeDoesNotMatchTheExpectedType()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithDoubles(256, 1d)
            .WithShort(257, 10)
            .Build();

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("unexpected type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsATiffMissingTheRequiredImageWidthTag()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(257, 10)
            .Build();

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATiffMissingTheRequiredImageLengthTag()
    {
        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 10)
            .Build();

        FormatException error = Assert.Throws<FormatException>(() => GeoTiffMetadataReader.Read(bytes));

        Assert.Contains("257", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reconstructs the observed live USGS 1 m GeoTIFF layout byte-for-byte from the facts recorded during
    /// Issue #21's setup (little-endian, 18 tags, the exact GeoKey and tag values OpenTopography returned
    /// for the reference parcel scenario's box), and asserts every property <see cref="GeoTiffMetadataReader"/>
    /// decodes from it equals the observed value. GeoKey 2054 (GeogAngularUnitsGeoKey) was present in the
    /// live response but is outside SolidGround's needed GeoKey set, so it is included here for fidelity to
    /// the observed layout but is expected to be silently ignored, like any other unrecognized GeoKey.
    /// </summary>
    [Fact]
    public void ReadsTheObservedLiveExampleSiteLaneGeoTiffLayoutExactly()
    {
        const string citationPart = "NAD83 / UTM zone 15N";
        const string geographicPart = "NAD83";
        string geoAscii = $"{citationPart}|{geographicPart}|";
        Assert.Equal("NAD83 / UTM zone 15N|NAD83|", geoAscii);

        byte[] dummyTilePayload = [1, 2, 3, 4];

        byte[] bytes = new TiffBuilder(bigEndian: false)
            .WithShort(256, 124)
            .WithShort(257, 117)
            .WithShort(258, 32)
            .WithShort(259, 8)
            .WithShort(262, 1)
            .WithShort(277, 1)
            .WithShort(284, 1)
            .WithShort(317, 1)
            .WithShort(322, 512)
            .WithShort(323, 512)
            .WithLongOffsetToTrailingPayload(324, dummyTilePayload)
            .WithLong(325, (uint)dummyTilePayload.Length)
            .WithShort(339, 3)
            .WithDoubles(33550, 1d, 1d, 0d)
            .WithDoubles(33922, 0d, 0d, 0d, [withheld], [withheld], 0d)
            .WithGeoKeyDirectory(
                (1024, 0, 1, 1),
                (1025, 0, 1, 1),
                (1026, 34737, (ushort)(citationPart.Length + 1), 0),
                (2049, 34737, (ushort)(geographicPart.Length + 1), (ushort)(citationPart.Length + 1)),
                (2054, 0, 1, 9102),
                (3072, 0, 1, 26915),
                (3076, 0, 1, 9001))
            .WithAscii(34737, geoAscii)
            .WithAscii(42113, "-999999")
            .Build();

        GeoTiffMetadata metadata = GeoTiffMetadataReader.Read(bytes);

        Assert.Equal(GeoTiffByteOrder.LittleEndian, metadata.ByteOrder);
        Assert.Equal(124u, metadata.ImageWidth);
        Assert.Equal(117u, metadata.ImageLength);
        Assert.Equal((ushort)1, metadata.ModelType);
        Assert.Equal((ushort)1, metadata.RasterType);
        Assert.Equal((ushort)26915, metadata.ProjectedCoordinateSystemCode);
        Assert.Null(metadata.GeographicCoordinateSystemCode);
        Assert.Equal((ushort)9001, metadata.LinearUnitsCode);
        Assert.Equal(citationPart, metadata.Citation);
        Assert.Equal(geographicPart, metadata.GeographicCitation);
        Assert.Null(metadata.ProjectedCitation);
        Assert.Null(metadata.VerticalCoordinateSystemCode);
        Assert.Equal((1d, 1d, 0d), metadata.ModelPixelScale);
        Assert.Equal((0d, 0d, 0d, [withheld], [withheld], 0d), metadata.ModelTiepoint);
        Assert.Equal("-999999", metadata.NoDataText);
    }
}

/// <summary>
/// A minimal TIFF/GeoTIFF byte-buffer builder used only by tests, for both byte orders, to exercise
/// <see cref="GeoTiffMetadataReader"/> without ever needing a real TIFF file or a native/imaging
/// dependency. Supports inline and out-of-line (offset) tag values, a GeoKey directory, and deliberately
/// malformed entries for negative tests. Widened to <see langword="internal"/> (rather than a private
/// nested type) because SolidGround Issue #21's later stages reuse it for CLI-level hybrid-flow tests.
/// </summary>
internal sealed class TiffBuilder
{
    private readonly bool bigEndian;
    private readonly List<Entry> entries = [];
    private byte[]? trailingPayload;
    private int trailingPayloadEntryIndex = -1;

    public TiffBuilder(bool bigEndian)
    {
        this.bigEndian = bigEndian;
    }

    public TiffBuilder WithShort(ushort tag, ushort value)
    {
        entries.Add(new Entry(tag, 3, 1, EncodeUInt16(value)));
        return this;
    }

    public TiffBuilder WithLong(ushort tag, uint value)
    {
        entries.Add(new Entry(tag, 4, 1, EncodeUInt32(value)));
        return this;
    }

    public TiffBuilder WithDoubles(ushort tag, params double[] values)
    {
        byte[] data = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            EncodeDouble(values[i]).CopyTo(data.AsSpan(i * 8, 8));
        }

        entries.Add(new Entry(tag, 12, (uint)values.Length, data));
        return this;
    }

    public TiffBuilder WithAscii(ushort tag, string text)
    {
        byte[] data = Encoding.ASCII.GetBytes(text);
        entries.Add(new Entry(tag, 2, (uint)data.Length, data));
        return this;
    }

    /// <summary>Adds an entry whose declared type, count, and inline 4-byte value/offset field are exactly as given, regardless of whether they are internally consistent -- used only to build deliberately adversarial entries for negative tests.</summary>
    public TiffBuilder WithForcedEntry(ushort tag, ushort type, uint count, uint literalValueField)
    {
        entries.Add(new Entry(tag, type, count, EncodeUInt32(literalValueField), ForceLiteralValueField: true));
        return this;
    }

    public TiffBuilder WithGeoKeyDirectory(params (ushort KeyId, ushort Location, ushort Count, ushort ValueOffset)[] keys)
    {
        ushort[] shorts = new ushort[4 + keys.Length * 4];
        shorts[0] = 1;
        shorts[1] = 1;
        shorts[2] = 0;
        shorts[3] = (ushort)keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            shorts[4 + i * 4] = keys[i].KeyId;
            shorts[5 + i * 4] = keys[i].Location;
            shorts[6 + i * 4] = keys[i].Count;
            shorts[7 + i * 4] = keys[i].ValueOffset;
        }

        return WithGeoKeyDirectoryShorts(shorts);
    }

    /// <summary>Adds a GeoKey directory whose header fields are exactly as given, with zero keys -- used only to test the header's own validation (for example an unsupported <c>KeyDirectoryVersion</c>).</summary>
    public TiffBuilder WithRawGeoKeyDirectory(ushort keyDirectoryVersion, ushort keyRevision, ushort minorRevision)
    {
        return WithGeoKeyDirectoryShorts([keyDirectoryVersion, keyRevision, minorRevision, 0]);
    }

    private TiffBuilder WithGeoKeyDirectoryShorts(ushort[] shorts)
    {
        byte[] data = new byte[shorts.Length * 2];
        for (int i = 0; i < shorts.Length; i++)
        {
            EncodeUInt16(shorts[i]).CopyTo(data.AsSpan(i * 2, 2));
        }

        entries.Add(new Entry(34735, 3, (uint)shorts.Length, data));
        return this;
    }

    /// <summary>Adds a scalar LONG tag whose value is a real offset into a trailing payload appended after every other entry's data -- used to model a tile-offset-style tag realistically without SolidGround's reader ever needing to follow it.</summary>
    public TiffBuilder WithLongOffsetToTrailingPayload(ushort tag, byte[] payload)
    {
        entries.Add(new Entry(tag, 4, 1, new byte[4]));
        trailingPayloadEntryIndex = entries.Count - 1;
        trailingPayload = payload;
        return this;
    }

    public byte[] Build()
    {
        const int HeaderSize = 8;
        int entryCount = entries.Count;
        int ifdSize = 2 + (entryCount * 12) + 4;
        int dataStart = HeaderSize + ifdSize;

        var offsets = new int[entryCount];
        int cursor = dataStart;
        for (int i = 0; i < entryCount; i++)
        {
            if (!entries[i].ForceLiteralValueField && entries[i].Data.Length > 4)
            {
                offsets[i] = cursor;
                cursor += entries[i].Data.Length;
            }
        }

        if (trailingPayloadEntryIndex >= 0 && trailingPayload is not null)
        {
            entries[trailingPayloadEntryIndex] = entries[trailingPayloadEntryIndex] with { Data = EncodeUInt32((uint)cursor) };
        }

        using var stream = new MemoryStream();
        byte[] byteOrderMark = bigEndian ? [(byte)'M', (byte)'M'] : [(byte)'I', (byte)'I'];
        stream.Write(byteOrderMark);
        WriteUInt16(stream, 42);
        WriteUInt32(stream, HeaderSize);
        WriteUInt16(stream, (ushort)entryCount);

        for (int i = 0; i < entryCount; i++)
        {
            Entry entry = entries[i];
            WriteUInt16(stream, entry.Tag);
            WriteUInt16(stream, entry.Type);
            WriteUInt32(stream, entry.Count);
            if (entry.ForceLiteralValueField || entry.Data.Length <= 4)
            {
                byte[] inline = new byte[4];
                entry.Data.CopyTo(inline.AsSpan());
                stream.Write(inline);
            }
            else
            {
                WriteUInt32(stream, (uint)offsets[i]);
            }
        }

        WriteUInt32(stream, 0);

        for (int i = 0; i < entryCount; i++)
        {
            if (!entries[i].ForceLiteralValueField && entries[i].Data.Length > 4)
            {
                stream.Write(entries[i].Data);
            }
        }

        if (trailingPayload is not null)
        {
            stream.Write(trailingPayload);
        }

        return stream.ToArray();
    }

    private byte[] EncodeUInt16(ushort value)
    {
        byte[] buffer = new byte[2];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        }

        return buffer;
    }

    private byte[] EncodeUInt32(uint value)
    {
        byte[] buffer = new byte[4];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        }

        return buffer;
    }

    private byte[] EncodeDouble(double value)
    {
        byte[] buffer = new byte[8];
        if (bigEndian)
        {
            BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        }

        return buffer;
    }

    private void WriteUInt16(Stream stream, ushort value) => stream.Write(EncodeUInt16(value));

    private void WriteUInt32(Stream stream, uint value) => stream.Write(EncodeUInt32(value));

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Data, bool ForceLiteralValueField = false);
}
