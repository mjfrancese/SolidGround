using System.Globalization;
using System.Text;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class AaiGridWriterTests
{
    [Fact]
    public void RoundTripsTheCommittedLowerLeftCornerFixtureCellForCellAndIsIdempotent()
    {
        (ElevationGrid grid, HorizontalReference horizontalReference, VerticalReference verticalReference) = LoadFixtureGrid();

        AssertRoundTripsAndIsIdempotent(grid, horizontalReference, verticalReference);
    }

    [Fact]
    public void RoundTripsACellCenterAnchoredGridConstructedInMemory()
    {
        const string text = """
            ncols 3
            nrows 3
            xllcenter 10.5
            yllcenter 20.5
            cellsize 1
            NODATA_value -9999
            1 2 -9999
            3 4 5
            6 7 8
            """;
        ElevationGrid grid = ParseText(text, HorizontalReference(), VerticalReference());

        string writtenText = AssertRoundTripsAndIsIdempotent(grid, HorizontalReference(), VerticalReference());

        Assert.Contains("xllcenter", writtenText, StringComparison.Ordinal);
        Assert.Contains("yllcenter", writtenText, StringComparison.Ordinal);
        Assert.DoesNotContain("xllcorner", writtenText, StringComparison.Ordinal);
        Assert.DoesNotContain("yllcorner", writtenText, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesDataRowsInTheGridsStoredOrderRegardlessOfDeclaredRowOrder()
    {
        double?[,] elevations = { { 1d, 2d }, { 3d, null }, { 5d, 6d } };
        ElevationGrid northToSouth = new(
            HorizontalReference(), VerticalReference(), new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, elevations);
        ElevationGrid southToNorth = new(
            HorizontalReference(), VerticalReference(), new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.SouthToNorth, elevations);

        string[] northToSouthDataLines = GetDataLines(WriteToString(northToSouth));
        string[] southToNorthDataLines = GetDataLines(WriteToString(southToNorth));

        Assert.Equal(northToSouthDataLines, southToNorthDataLines);
    }

    [Fact]
    public void PreservesTheNodataHoleAtItsOriginalCell()
    {
        (ElevationGrid grid, HorizontalReference horizontalReference, VerticalReference verticalReference) = LoadFixtureGrid();
        Assert.Null(grid.GetElevation(0, 2));

        string text = WriteToString(grid);
        string firstDataRow = GetDataLines(text)[0];

        Assert.Equal("-9999", firstDataRow.Split(' ')[2]);

        ElevationGrid roundTripped = ParseText(text, horizontalReference, verticalReference);
        Assert.Null(roundTripped.GetElevation(0, 2));
    }

    [Fact]
    public void ThrowsWhenAValidElevationEqualsTheConfiguredNodataSentinel()
    {
        ElevationGrid grid = SingleCellGrid(42d);
        using StringWriter writer = new();

        ArgumentException error = Assert.Throws<ArgumentException>(() => AaiGridWriter.Write(grid, writer, noDataValue: 42d));

        Assert.Equal("noDataValue", error.ParamName);
    }

    [Fact]
    public void ThrowsWhenCellSizeXAndCellSizeYDiffer()
    {
        ElevationGrid grid = new(
            HorizontalReference(), VerticalReference(), new Coordinate2D(0d, 0d), 1d, 2d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, new double?[,] { { 10d } });
        using StringWriter writer = new();

        ArgumentException error = Assert.Throws<ArgumentException>(() => AaiGridWriter.Write(grid, writer));

        Assert.Equal("grid", error.ParamName);
    }

    [Fact]
    public void ThrowsForANullGridOrWriter()
    {
        ElevationGrid grid = SingleCellGrid(10d);
        using StringWriter writer = new();

        Assert.Throws<ArgumentNullException>(() => AaiGridWriter.Write(null!, writer));
        Assert.Throws<ArgumentNullException>(() => AaiGridWriter.Write(grid, null!));
    }

    [Fact]
    public void RendersBitIdenticalOutputUnderTheDeDeCulture()
    {
        (ElevationGrid grid, _, _) = LoadFixtureGrid();
        byte[] invariantBytes = WriteToUtf8Bytes(grid);
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            byte[] deDeBytes = WriteToUtf8Bytes(grid);

            Assert.Equal(invariantBytes, deDeBytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void NeverEmitsACarriageReturn()
    {
        (ElevationGrid grid, _, _) = LoadFixtureGrid();

        byte[] bytes = WriteToUtf8Bytes(grid);

        Assert.DoesNotContain((byte)0x0D, bytes);
    }

    // ---- shared helpers ----

    private static string AssertRoundTripsAndIsIdempotent(ElevationGrid grid, HorizontalReference horizontalReference, VerticalReference verticalReference)
    {
        string firstText = WriteToString(grid);
        ElevationGrid roundTripped = ParseText(firstText, horizontalReference, verticalReference);

        AssertGridsAreCellForCellIdentical(grid, roundTripped);

        string secondText = WriteToString(roundTripped);
        Assert.Equal(firstText, secondText);

        return firstText;
    }

    private static void AssertGridsAreCellForCellIdentical(ElevationGrid expected, ElevationGrid actual)
    {
        Assert.Equal(expected.RowCount, actual.RowCount);
        Assert.Equal(expected.ColumnCount, actual.ColumnCount);
        Assert.Equal(expected.SouthwestAnchor, actual.SouthwestAnchor);
        Assert.Equal(expected.CellSizeX, actual.CellSizeX);
        Assert.Equal(expected.CellSizeY, actual.CellSizeY);
        Assert.Equal(expected.AnchorConvention, actual.AnchorConvention);
        Assert.Equal(expected.RowOrder, actual.RowOrder);

        for (int row = 0; row < expected.RowCount; row++)
        {
            for (int column = 0; column < expected.ColumnCount; column++)
            {
                double? expectedCell = expected.GetElevation(row, column);
                double? actualCell = actual.GetElevation(row, column);
                Assert.Equal(expectedCell.HasValue, actualCell.HasValue);
                if (expectedCell.HasValue && actualCell.HasValue)
                {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expectedCell.Value), BitConverter.DoubleToInt64Bits(actualCell.Value));
                }
            }
        }
    }

    private static (ElevationGrid Grid, HorizontalReference Horizontal, VerticalReference Vertical) LoadFixtureGrid()
    {
        WellKnownTextReference parsedPrj = WellKnownTextReferenceParser.Parse(ReadFixture("example-site-synthetic.prj"));
        Assert.NotNull(parsedPrj.Vertical);
        VerticalReference verticalReference = parsedPrj.Vertical!;

        ElevationGrid grid = ParseText(ReadFixture("example-site-synthetic.asc"), parsedPrj.Horizontal, verticalReference);

        return (grid, parsedPrj.Horizontal, verticalReference);
    }

    private static ElevationGrid SingleCellGrid(double elevation) => new(
        HorizontalReference(), VerticalReference(), new Coordinate2D(0d, 0d), 1d, 1d,
        GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, new double?[,] { { elevation } });

    private static string WriteToString(ElevationGrid grid)
    {
        StringWriter writer = new();
        AaiGridWriter.Write(grid, writer);
        return writer.ToString();
    }

    private static byte[] WriteToUtf8Bytes(ElevationGrid grid)
    {
        using MemoryStream stream = new();
        using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        {
            AaiGridWriter.Write(grid, writer);
        }

        return stream.ToArray();
    }

    private static ElevationGrid ParseText(string text, HorizontalReference horizontalReference, VerticalReference verticalReference)
    {
        using StringReader reader = new(text);
        return AaiGridParser.Parse(reader, horizontalReference, verticalReference);
    }

    private static string[] GetDataLines(string text) =>
        text.Split('\n').Skip(6).Where(line => line.Length > 0).ToArray();

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static HorizontalReference HorizontalReference() => new(
        "EPSG:26915",
        "NAD83(2011)",
        HorizontalReferenceKind.Projected,
        HorizontalUnit.Linear(LengthUnit.Meter),
        HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
