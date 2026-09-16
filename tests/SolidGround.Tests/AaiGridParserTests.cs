using System.Globalization;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class AaiGridParserTests
{
    [Fact]
    public void ParsesAValidCornerAnchoredGrid()
    {
        const string text = """
            ncols 2
            nrows 2
            xllcorner 10
            yllcorner 20
            cellsize 2
            NODATA_value -9999
            4 3
            2 1
            """;

        ElevationGrid grid = Parse(text);

        Assert.Equal(2, grid.ColumnCount);
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(GridAnchorConvention.LowerLeftCorner, grid.AnchorConvention);
        Assert.Equal(GridRowOrder.NorthToSouth, grid.RowOrder);
        Assert.Equal(4d, grid.GetElevation(0, 0));
        Assert.Equal(new Core.Geometry.Coordinate2D(11d, 23d), grid.GetCellCenter(0, 0));
    }

    [Fact]
    public void ParsesCenterAnchorsAndTheDefaultNoDataValue()
    {
        const string text = """
            ncols 2
            nrows 2
            xllcenter 10.5
            yllcenter 20.5
            cellsize 1
            -9999 3
            2 1
            """;

        ElevationGrid grid = Parse(text);

        Assert.Equal(GridAnchorConvention.CellCenter, grid.AnchorConvention);
        Assert.Null(grid.GetElevation(0, 0));
        Assert.Equal(new Core.Geometry.Coordinate2D(10.5d, 21.5d), grid.GetCellCenter(0, 0));
    }

    [Fact]
    public void ParsesCellValuesAcrossArbitraryLineBreaks()
    {
        const string text = """
            ncols 2
            nrows 2
            xllcorner 10
            yllcorner 20
            cellsize 1
            NODATA_value -9999
            4 3 2
            1
            """;

        ElevationGrid grid = Parse(text);

        Assert.Equal(4d, grid.GetElevation(0, 0));
        Assert.Equal(3d, grid.GetElevation(0, 1));
        Assert.Equal(2d, grid.GetElevation(1, 0));
        Assert.Equal(1d, grid.GetElevation(1, 1));
    }

    [Fact]
    public void RejectsExtraCellValues()
    {
        const string text = """
            ncols 2
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1
            NODATA_value -9999
            4 3 2
            """;

        FormatException error = Assert.Throws<FormatException>(() => Parse(text));

        Assert.Contains("extra", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsesTheOfflineExampleSiteFixtureWithNorthToSouthRowsAndANoDataHole()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-site-synthetic.asc");
        using var reader = File.OpenText(path);

        ElevationGrid grid = AaiGridParser.Parse(reader, HorizontalReference(), VerticalReference());

        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(3, grid.RowCount);
        Assert.Equal(184.20d, grid.GetElevation(0, 0));
        Assert.Null(grid.GetElevation(0, 2));
        Assert.Equal(new Core.Geometry.Coordinate2D([withheld], [withheld]), grid.GetCellCenter(0, 0));
        Assert.Equal(new Core.Geometry.Coordinate2D([withheld], [withheld]), grid.GetCellCenter(2, 0));
    }

    [Fact]
    public void RejectsMalformedHeadersMissingCellsAndNonInvariantNumbers()
    {
        const string mixedOrigins = """
            ncols 1
            nrows 1
            xllcorner 10
            yllcenter 20
            cellsize 1
            1
            """;
        const string missingCell = """
            ncols 2
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1
            1
            """;
        const string commaDecimal = """
            ncols 1
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1
            1,5
            """;
        const string zeroColumns = """
            ncols 0
            nrows 1
            xllcorner 10
            yllcorner 20
            cellsize 1
            1
            """;

        Assert.Contains("corner form or both use the center form", Assert.Throws<FormatException>(() => Parse(mixedOrigins)).Message);
        Assert.Contains("row 1, column 2", Assert.Throws<FormatException>(() => Parse(missingCell)).Message);
        Assert.Contains("invariant-culture", Assert.Throws<FormatException>(() => Parse(commaDecimal)).Message);
        Assert.Contains("positive integer", Assert.Throws<FormatException>(() => Parse(zeroColumns)).Message);
    }

    [Fact]
    public void ParsesDecimalPointsIndependentlyOfTheCurrentCulture()
    {
        const string text = """
            ncols 1
            nrows 1
            xllcenter 10.25
            yllcenter 20.75
            cellsize 0.5
            184.125
            """;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ElevationGrid grid = Parse(text);

            Assert.Equal(184.125d, grid.GetElevation(0, 0));
            Assert.Equal(0.5d, grid.CellSizeX);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static ElevationGrid Parse(string text)
    {
        using var reader = new StringReader(text);
        return AaiGridParser.Parse(reader, HorizontalReference(), VerticalReference());
    }

    private static HorizontalReference HorizontalReference() => new(
        "EPSG:26915",
        "NAD83(2011)",
        HorizontalReferenceKind.Projected,
        HorizontalUnit.Linear(LengthUnit.Meter),
        HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
