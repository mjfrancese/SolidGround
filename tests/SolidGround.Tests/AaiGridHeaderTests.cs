using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class AaiGridHeaderTests
{
    [Fact]
    public void ReadsACornerAnchoredHeaderWithADeclaredNoDataValue()
    {
        const string text = """
            ncols 2
            nrows 3
            xllcorner 449614.000000000000
            yllcorner 4604506.000340246595
            cellsize 1.000000000000
            NODATA_value -999999
            4 3
            """;

        AaiGridHeader header = ReadHeader(text);

        Assert.Equal(2, header.ColumnCount);
        Assert.Equal(3, header.RowCount);
        Assert.Equal(449614.000000000000d, header.AnchorX);
        Assert.Equal(4604506.000340246595d, header.AnchorY);
        Assert.Equal(GridAnchorConvention.LowerLeftCorner, header.AnchorConvention);
        Assert.Equal(1.0d, header.CellSize);
        Assert.Equal(-999999d, header.NoDataValue);
        Assert.True(header.NoDataDeclared);
    }

    [Fact]
    public void ReadsACenterAnchoredHeaderWithTheDefaultNoDataValue()
    {
        const string text = """
            ncols 2
            nrows 2
            xllcenter 10.5
            yllcenter 20.5
            cellsize 1
            3 4
            """;

        AaiGridHeader header = ReadHeader(text);

        Assert.Equal(GridAnchorConvention.CellCenter, header.AnchorConvention);
        Assert.Equal(10.5d, header.AnchorX);
        Assert.Equal(20.5d, header.AnchorY);
        Assert.Equal(-9999d, header.NoDataValue);
        Assert.False(header.NoDataDeclared);
    }

    [Fact]
    public void RejectsTheSameMalformedHeadersAsParseWithTheSameMessages()
    {
        const string mixedOrigins = """
            ncols 1
            nrows 1
            xllcorner 10
            yllcenter 20
            cellsize 1
            1
            """;
        const string missingCellsize = """
            ncols 1
            nrows 1
            xllcorner 10
            yllcorner 20
            """;
        const string commaDecimalCellsize = """
            ncols 1
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1,5
            1
            """;
        const string zeroColumns = """
            ncols 0
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1
            1
            """;

        AssertSameFailure(mixedOrigins, "corner form or both use the center form");
        AssertSameFailure(missingCellsize, "Missing cellsize");
        AssertSameFailure(commaDecimalCellsize, "invariant-culture");
        AssertSameFailure(zeroColumns, "positive integer");
    }

    [Fact]
    public void ReadHeaderRejectsANullReader()
    {
        Assert.Throws<ArgumentNullException>(() => AaiGridParser.ReadHeader(null!));
    }

    [Fact]
    public void ConstructorRejectsNonPositiveCountsAndCellSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AaiGridHeader(0, 1, 0d, 0d, GridAnchorConvention.LowerLeftCorner, 1d, -9999d, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AaiGridHeader(1, 0, 0d, 0d, GridAnchorConvention.LowerLeftCorner, 1d, -9999d, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AaiGridHeader(1, 1, 0d, 0d, GridAnchorConvention.LowerLeftCorner, 0d, -9999d, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AaiGridHeader(1, 1, 0d, 0d, GridAnchorConvention.LowerLeftCorner, double.NaN, -9999d, false));
    }

    [Fact]
    public void ParseStillReadsEveryRowWhenNodataValueIsAbsent()
    {
        const string text = """
            ncols 2
            nrows 2
            xllcorner 10
            yllcorner 20
            cellsize 1
            5 6
            7 8
            """;

        using var reader = new StringReader(text);
        ElevationGrid grid = AaiGridParser.Parse(reader, HorizontalReference(), VerticalReference());

        Assert.Equal(5d, grid.GetElevation(0, 0));
        Assert.Equal(6d, grid.GetElevation(0, 1));
        Assert.Equal(7d, grid.GetElevation(1, 0));
        Assert.Equal(8d, grid.GetElevation(1, 1));
    }

    private static void AssertSameFailure(string text, string expectedMessageFragment)
    {
        FormatException fromReadHeader = Assert.Throws<FormatException>(() => ReadHeader(text));
        FormatException fromParse = Assert.Throws<FormatException>(() =>
        {
            using var reader = new StringReader(text);
            AaiGridParser.Parse(reader, HorizontalReference(), VerticalReference());
        });

        Assert.Contains(expectedMessageFragment, fromReadHeader.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fromParse.Message, fromReadHeader.Message, StringComparer.Ordinal);
    }

    private static AaiGridHeader ReadHeader(string text)
    {
        using var reader = new StringReader(text);
        return AaiGridParser.ReadHeader(reader);
    }

    private static HorizontalReference HorizontalReference() => new(
        "EPSG:26915",
        "NAD83(2011)",
        HorizontalReferenceKind.Projected,
        HorizontalUnit.Linear(LengthUnit.Meter),
        HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
