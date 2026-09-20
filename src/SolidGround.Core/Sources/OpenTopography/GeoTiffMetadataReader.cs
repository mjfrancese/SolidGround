using System.Buffers.Binary;
using System.Text;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Reads only IFD0's tags and GeoKeys from a GeoTIFF byte buffer -- never a pixel, never a decompression --
/// into a <see cref="GeoTiffMetadata"/>. Used to recover a raster's horizontal and vertical reference
/// metadata from a GeoTIFF requested only for that purpose. See
/// docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis" section.
/// </summary>
public static class GeoTiffMetadataReader
{
    private const ushort TypeByte = 1;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeDouble = 12;

    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagModelPixelScale = 33550;
    private const ushort TagModelTiepoint = 33922;
    private const ushort TagGeoKeyDirectory = 34735;
    private const ushort TagGeoDoubleParams = 34736;
    private const ushort TagGeoAsciiParams = 34737;
    private const ushort TagGdalNoData = 42113;

    private const ushort KeyModelType = 1024;
    private const ushort KeyRasterType = 1025;
    private const ushort KeyCitation = 1026;
    private const ushort KeyGeographicCoordinateSystemCode = 2048;
    private const ushort KeyGeographicCitation = 2049;
    private const ushort KeyProjectedCoordinateSystemCode = 3072;
    private const ushort KeyProjectedCitation = 3073;
    private const ushort KeyLinearUnitsCode = 3076;
    private const ushort KeyVerticalCoordinateSystemCode = 4096;

    private const ushort GeoKeyLocationInline = 0;
    private const ushort GeoKeyLocationGeoDoubleParams = 34736;
    private const ushort GeoKeyLocationGeoAsciiParams = 34737;

    /// <summary>
    /// Reads IFD0 of a TIFF byte buffer. Supports classic TIFF ("II" or "MM" with magic number 42) only;
    /// BigTIFF (magic number 43) and anything else fail. Only the tags and GeoKeys
    /// <see cref="GeoTiffMetadata"/> exposes are interpreted; every other tag and GeoKey is skipped
    /// unread. Every offset and length this method follows is bounds-checked against
    /// <paramref name="bytes"/> before use, so a corrupt or truncated buffer always fails with
    /// <see cref="FormatException"/>, never a raw indexing exception.
    /// </summary>
    /// <param name="bytes">The complete byte content of a TIFF (or GeoTIFF) file.</param>
    /// <exception cref="FormatException">
    /// The buffer is too short, does not begin with a recognized TIFF byte-order mark, declares an
    /// unsupported magic number (including BigTIFF's 43), an offset or length would read outside the
    /// buffer, a required tag (256 or 257) is missing, a needed tag or GeoKey has an unexpected type or
    /// representation, or the GeoKey directory's header is malformed or declares an unsupported version.
    /// </exception>
    public static GeoTiffMetadata Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> buffer = bytes.Span;
        if (buffer.Length < 8)
        {
            throw new FormatException($"The buffer is only {buffer.Length} byte(s); a TIFF header needs at least 8.");
        }

        GeoTiffByteOrder byteOrder = ReadByteOrder(buffer);

        ushort magic = ReadUInt16(buffer.Slice(2, 2), byteOrder);
        if (magic == 43)
        {
            throw new FormatException("The buffer is a BigTIFF file (magic number 43); BigTIFF is not supported.");
        }

        if (magic != 42)
        {
            throw new FormatException($"Unsupported TIFF magic number {magic}; expected 42.");
        }

        uint ifdOffset = ReadUInt32(buffer.Slice(4, 4), byteOrder);
        EnsureInBounds(buffer, ifdOffset, 2, "IFD0's entry count");
        ushort entryCount = ReadUInt16(buffer.Slice((int)ifdOffset, 2), byteOrder);

        long entriesStart = (long)ifdOffset + 2;
        long entriesLength = (long)entryCount * 12;
        EnsureInBounds(buffer, entriesStart, entriesLength, "IFD0's entries");

        uint? imageWidth = null;
        uint? imageLength = null;
        (double, double, double)? pixelScale = null;
        (double, double, double, double, double, double)? tiepoint = null;
        string? noDataText = null;

        ReadOnlySpan<byte> geoKeyDirectory = default;
        bool hasGeoKeyDirectory = false;
        ReadOnlySpan<byte> geoDoubleParams = default;
        bool hasGeoDoubleParams = false;
        ReadOnlySpan<byte> geoAsciiParams = default;
        bool hasGeoAsciiParams = false;

        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = (int)entriesStart + i * 12;
            ushort tag = ReadUInt16(buffer.Slice(entryOffset, 2), byteOrder);
            ushort type = ReadUInt16(buffer.Slice(entryOffset + 2, 2), byteOrder);
            uint count = ReadUInt32(buffer.Slice(entryOffset + 4, 4), byteOrder);
            ReadOnlySpan<byte> valueField = buffer.Slice(entryOffset + 8, 4);

            switch (tag)
            {
                case TagImageWidth:
                    imageWidth = ReadShortOrLong(tag, type, count, valueField, buffer, byteOrder);
                    break;

                case TagImageLength:
                    imageLength = ReadShortOrLong(tag, type, count, valueField, buffer, byteOrder);
                    break;

                case TagModelPixelScale:
                    pixelScale = ReadDoubleTriple(tag, type, count, valueField, buffer, byteOrder);
                    break;

                case TagModelTiepoint:
                    tiepoint = ReadDoubleSextuple(tag, type, count, valueField, buffer, byteOrder);
                    break;

                case TagGdalNoData:
                    RequireType(tag, type, TypeAscii);
                    ReadOnlySpan<byte> noDataBytes = ResolveValueBytes(tag, count, 1, valueField, buffer, byteOrder);
                    noDataText = DecodeAscii(noDataBytes);
                    break;

                case TagGeoKeyDirectory:
                    RequireType(tag, type, TypeShort);
                    geoKeyDirectory = ResolveValueBytes(tag, count, 2, valueField, buffer, byteOrder);
                    hasGeoKeyDirectory = true;
                    break;

                case TagGeoDoubleParams:
                    RequireType(tag, type, TypeDouble);
                    geoDoubleParams = ResolveValueBytes(tag, count, 8, valueField, buffer, byteOrder);
                    hasGeoDoubleParams = true;
                    break;

                case TagGeoAsciiParams:
                    RequireType(tag, type, TypeAscii);
                    geoAsciiParams = ResolveValueBytes(tag, count, 1, valueField, buffer, byteOrder);
                    hasGeoAsciiParams = true;
                    break;

                default:
                    // Unknown tags are skipped entirely: their value is never read, so an unreadable or
                    // out-of-range value/offset in a tag SolidGround does not need can never fail this parse.
                    break;
            }
        }

        if (imageWidth is null)
        {
            throw new FormatException("Required tag 256 (ImageWidth) is missing.");
        }

        if (imageLength is null)
        {
            throw new FormatException("Required tag 257 (ImageLength) is missing.");
        }

        ushort? modelType = null;
        ushort? rasterType = null;
        ushort? projectedCode = null;
        ushort? geographicCode = null;
        ushort? linearUnitsCode = null;
        ushort? verticalCode = null;
        string? citation = null;
        string? geographicCitation = null;
        string? projectedCitation = null;

        if (hasGeoKeyDirectory)
        {
            ReadGeoKeyDirectory(
                geoKeyDirectory,
                byteOrder,
                geoAsciiParams,
                hasGeoAsciiParams,
                geoDoubleParams,
                hasGeoDoubleParams,
                out modelType,
                out rasterType,
                out citation,
                out geographicCode,
                out geographicCitation,
                out projectedCode,
                out projectedCitation,
                out linearUnitsCode,
                out verticalCode);
        }

        return new GeoTiffMetadata(
            byteOrder,
            imageWidth.Value,
            imageLength.Value,
            modelType,
            rasterType,
            projectedCode,
            geographicCode,
            linearUnitsCode,
            citation,
            geographicCitation,
            projectedCitation,
            verticalCode,
            pixelScale,
            tiepoint,
            noDataText);
    }

    private static GeoTiffByteOrder ReadByteOrder(ReadOnlySpan<byte> buffer)
    {
        if (buffer[0] == (byte)'I' && buffer[1] == (byte)'I')
        {
            return GeoTiffByteOrder.LittleEndian;
        }

        if (buffer[0] == (byte)'M' && buffer[1] == (byte)'M')
        {
            return GeoTiffByteOrder.BigEndian;
        }

        throw new FormatException("The buffer does not begin with a recognized TIFF byte-order mark ('II' or 'MM').");
    }

    private static void ReadGeoKeyDirectory(
        ReadOnlySpan<byte> geoKeyDirectory,
        GeoTiffByteOrder byteOrder,
        ReadOnlySpan<byte> geoAsciiParams,
        bool hasGeoAsciiParams,
        ReadOnlySpan<byte> geoDoubleParams,
        bool hasGeoDoubleParams,
        out ushort? modelType,
        out ushort? rasterType,
        out string? citation,
        out ushort? geographicCode,
        out string? geographicCitation,
        out ushort? projectedCode,
        out string? projectedCitation,
        out ushort? linearUnitsCode,
        out ushort? verticalCode)
    {
        modelType = null;
        rasterType = null;
        citation = null;
        geographicCode = null;
        geographicCitation = null;
        projectedCode = null;
        projectedCitation = null;
        linearUnitsCode = null;
        verticalCode = null;

        if (geoKeyDirectory.Length < 8)
        {
            throw new FormatException("The GeoKey directory (tag 34735) is too short to contain its 4-SHORT header.");
        }

        ushort keyDirectoryVersion = ReadUInt16(geoKeyDirectory[..2], byteOrder);
        if (keyDirectoryVersion != 1)
        {
            throw new FormatException($"The GeoKey directory's KeyDirectoryVersion is {keyDirectoryVersion}; expected 1.");
        }

        ushort numberOfKeys = ReadUInt16(geoKeyDirectory.Slice(6, 2), byteOrder);
        int requiredLength = 8 + numberOfKeys * 8;
        if (geoKeyDirectory.Length < requiredLength)
        {
            throw new FormatException(
                $"The GeoKey directory declares {numberOfKeys} key(s), which needs {requiredLength} byte(s), " +
                $"but only {geoKeyDirectory.Length} byte(s) were supplied.");
        }

        for (int i = 0; i < numberOfKeys; i++)
        {
            int entryOffset = 8 + i * 8;
            ushort keyId = ReadUInt16(geoKeyDirectory.Slice(entryOffset, 2), byteOrder);
            ushort location = ReadUInt16(geoKeyDirectory.Slice(entryOffset + 2, 2), byteOrder);
            ushort keyCount = ReadUInt16(geoKeyDirectory.Slice(entryOffset + 4, 2), byteOrder);
            ushort valueOffset = ReadUInt16(geoKeyDirectory.Slice(entryOffset + 6, 2), byteOrder);

            switch (location)
            {
                case GeoKeyLocationInline:
                    AssignNumericGeoKey(
                        keyId, valueOffset, ref modelType, ref rasterType, ref geographicCode, ref projectedCode,
                        ref linearUnitsCode, ref verticalCode);
                    break;

                case GeoKeyLocationGeoAsciiParams:
                    if (!hasGeoAsciiParams)
                    {
                        throw new FormatException($"GeoKey {keyId} references GeoAsciiParams (tag 34737), which is not present in this file.");
                    }

                    string text = ResolveGeoAsciiSlice(keyId, geoAsciiParams, valueOffset, keyCount);
                    AssignTextGeoKey(keyId, text, ref citation, ref geographicCitation, ref projectedCitation);
                    break;

                case GeoKeyLocationGeoDoubleParams:
                    if (!hasGeoDoubleParams)
                    {
                        throw new FormatException($"GeoKey {keyId} references GeoDoubleParams (tag 34736), which is not present in this file.");
                    }

                    // No GeoKey SolidGround currently reads resolves through GeoDoubleParams; the range is
                    // still validated so a corrupt or malicious directory referencing data outside the
                    // buffer is rejected exactly like every other out-of-bounds reference.
                    EnsureGeoDoubleParamsRangeInBounds(keyId, geoDoubleParams, valueOffset, keyCount);
                    break;

                default:
                    throw new FormatException($"GeoKey {keyId} has an unsupported TIFFTagLocation {location}; expected 0, 34736, or 34737.");
            }
        }
    }

    private static void AssignNumericGeoKey(
        ushort keyId,
        ushort value,
        ref ushort? modelType,
        ref ushort? rasterType,
        ref ushort? geographicCode,
        ref ushort? projectedCode,
        ref ushort? linearUnitsCode,
        ref ushort? verticalCode)
    {
        switch (keyId)
        {
            case KeyModelType:
                modelType = value;
                break;
            case KeyRasterType:
                rasterType = value;
                break;
            case KeyGeographicCoordinateSystemCode:
                geographicCode = value;
                break;
            case KeyProjectedCoordinateSystemCode:
                projectedCode = value;
                break;
            case KeyLinearUnitsCode:
                linearUnitsCode = value;
                break;
            case KeyVerticalCoordinateSystemCode:
                verticalCode = value;
                break;
            default:
                // Unknown GeoKeys are ignored.
                break;
        }
    }

    private static void AssignTextGeoKey(
        ushort keyId,
        string text,
        ref string? citation,
        ref string? geographicCitation,
        ref string? projectedCitation)
    {
        switch (keyId)
        {
            case KeyCitation:
                citation = text;
                break;
            case KeyGeographicCitation:
                geographicCitation = text;
                break;
            case KeyProjectedCitation:
                projectedCitation = text;
                break;
            default:
                // Unknown GeoKeys are ignored.
                break;
        }
    }

    private static string ResolveGeoAsciiSlice(ushort keyId, ReadOnlySpan<byte> geoAsciiParams, ushort valueOffset, ushort keyCount)
    {
        if ((long)valueOffset + keyCount > geoAsciiParams.Length)
        {
            throw new FormatException(
                $"GeoKey {keyId}'s GeoAsciiParams range [{valueOffset}, {valueOffset + keyCount}) is out of " +
                $"bounds for a {geoAsciiParams.Length}-byte value.");
        }

        string text = DecodeAscii(geoAsciiParams.Slice(valueOffset, keyCount));
        return text.TrimEnd('\0', '|');
    }

    private static void EnsureGeoDoubleParamsRangeInBounds(ushort keyId, ReadOnlySpan<byte> geoDoubleParams, ushort valueOffset, ushort keyCount)
    {
        long startBytes = (long)valueOffset * 8;
        long lengthBytes = (long)keyCount * 8;
        if (startBytes + lengthBytes > geoDoubleParams.Length)
        {
            throw new FormatException(
                $"GeoKey {keyId}'s GeoDoubleParams range is out of bounds for a {geoDoubleParams.Length}-byte value.");
        }
    }

    private static uint ReadShortOrLong(
        ushort tag, ushort type, uint count, ReadOnlySpan<byte> valueField, ReadOnlySpan<byte> buffer, GeoTiffByteOrder byteOrder)
    {
        int elementSize = type switch
        {
            TypeShort => 2,
            TypeLong => 4,
            _ => throw new FormatException($"Tag {tag} has unexpected type {type}; expected SHORT (3) or LONG (4)."),
        };

        ReadOnlySpan<byte> valueBytes = ResolveValueBytes(tag, count, elementSize, valueField, buffer, byteOrder);
        if (valueBytes.Length < elementSize)
        {
            throw new FormatException($"Tag {tag} declares no value.");
        }

        return elementSize == 2 ? ReadUInt16(valueBytes, byteOrder) : ReadUInt32(valueBytes, byteOrder);
    }

    private static (double, double, double) ReadDoubleTriple(
        ushort tag, ushort type, uint count, ReadOnlySpan<byte> valueField, ReadOnlySpan<byte> buffer, GeoTiffByteOrder byteOrder)
    {
        RequireType(tag, type, TypeDouble);
        ReadOnlySpan<byte> valueBytes = ResolveValueBytes(tag, count, 8, valueField, buffer, byteOrder);
        if (valueBytes.Length < 24)
        {
            throw new FormatException($"Tag {tag} must supply at least 3 DOUBLE values.");
        }

        return (
            ReadDouble(valueBytes[..8], byteOrder),
            ReadDouble(valueBytes.Slice(8, 8), byteOrder),
            ReadDouble(valueBytes.Slice(16, 8), byteOrder));
    }

    private static (double, double, double, double, double, double) ReadDoubleSextuple(
        ushort tag, ushort type, uint count, ReadOnlySpan<byte> valueField, ReadOnlySpan<byte> buffer, GeoTiffByteOrder byteOrder)
    {
        RequireType(tag, type, TypeDouble);
        ReadOnlySpan<byte> valueBytes = ResolveValueBytes(tag, count, 8, valueField, buffer, byteOrder);
        if (valueBytes.Length < 48)
        {
            throw new FormatException($"Tag {tag} must supply at least 6 DOUBLE values (the first tiepoint).");
        }

        return (
            ReadDouble(valueBytes[..8], byteOrder),
            ReadDouble(valueBytes.Slice(8, 8), byteOrder),
            ReadDouble(valueBytes.Slice(16, 8), byteOrder),
            ReadDouble(valueBytes.Slice(24, 8), byteOrder),
            ReadDouble(valueBytes.Slice(32, 8), byteOrder),
            ReadDouble(valueBytes.Slice(40, 8), byteOrder));
    }

    private static void RequireType(ushort tag, ushort actualType, ushort expectedType)
    {
        if (actualType != expectedType)
        {
            throw new FormatException($"Tag {tag} has unexpected type {actualType}; expected type {expectedType}.");
        }
    }

    /// <summary>
    /// Resolves a tag's value bytes, honoring TIFF's inline-value rule: a value whose total byte size is 4
    /// or less is stored inline in <paramref name="valueField"/> itself; a larger value is stored elsewhere,
    /// and <paramref name="valueField"/> holds a 4-byte offset to it instead. Every byte read is
    /// bounds-checked against <paramref name="buffer"/> using a widened (<see langword="long"/>) byte-count
    /// computation, so an oversized <paramref name="count"/> can never overflow into a false pass -- it is
    /// reported as an ordinary out-of-bounds <see cref="FormatException"/>, never an
    /// <see cref="OverflowException"/> or a raw indexing exception.
    /// </summary>
    private static ReadOnlySpan<byte> ResolveValueBytes(
        ushort tag, uint count, int elementSize, ReadOnlySpan<byte> valueField, ReadOnlySpan<byte> buffer, GeoTiffByteOrder byteOrder)
    {
        long totalBytes = (long)elementSize * count;
        if (totalBytes <= 4)
        {
            return valueField[..(int)totalBytes];
        }

        uint offset = ReadUInt32(valueField, byteOrder);
        if (offset + totalBytes > buffer.Length)
        {
            throw new FormatException(
                $"Tag {tag}'s value of {totalBytes} byte(s) at offset {offset} extends beyond the end of the " +
                $"{buffer.Length}-byte buffer.");
        }

        return buffer.Slice((int)offset, (int)totalBytes);
    }

    private static void EnsureInBounds(ReadOnlySpan<byte> buffer, long offset, long length, string what)
    {
        if (offset < 0 || length < 0 || offset + length > buffer.Length)
        {
            throw new FormatException(
                $"The buffer is too short to contain {what} ({length} byte(s) needed at offset {offset}; " +
                $"the buffer is {buffer.Length} byte(s)).");
        }
    }

    private static string DecodeAscii(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes).TrimEnd('\0');

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, GeoTiffByteOrder byteOrder) =>
        byteOrder == GeoTiffByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(span)
            : BinaryPrimitives.ReadUInt16BigEndian(span);

    private static uint ReadUInt32(ReadOnlySpan<byte> span, GeoTiffByteOrder byteOrder) =>
        byteOrder == GeoTiffByteOrder.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt32BigEndian(span);

    private static double ReadDouble(ReadOnlySpan<byte> span, GeoTiffByteOrder byteOrder) =>
        byteOrder == GeoTiffByteOrder.LittleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
}
