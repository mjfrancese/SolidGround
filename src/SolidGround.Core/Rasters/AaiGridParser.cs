using System.Globalization;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;

namespace SolidGround.Core.Rasters;

/// <summary>Parses an Esri ASCII raster into a normalized elevation grid.</summary>
public static class AaiGridParser
{
    public static ElevationGrid Parse(
        TextReader reader,
        HorizontalReference horizontalReference,
        VerticalReference verticalReference)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(horizontalReference);
        ArgumentNullException.ThrowIfNull(verticalReference);

        int columns = ParsePositiveInteger(ReadHeaderValue(reader, "ncols"), "ncols");
        int rows = ParsePositiveInteger(ReadHeaderValue(reader, "nrows"), "nrows");
        (string xKey, string xText) = ReadHeaderPair(reader, "x origin");
        (string yKey, string yText) = ReadHeaderPair(reader, "y origin");
        GridAnchorConvention anchorConvention = ParseAnchorConvention(xKey, yKey);
        double x = ParseFiniteDouble(xText, xKey);
        double y = ParseFiniteDouble(yText, yKey);
        double cellSize = ParseFiniteDouble(ReadHeaderValue(reader, "cellsize"), "cellsize");
        if (cellSize <= 0d)
        {
            throw new FormatException("cellsize must be positive.");
        }

        string firstLineAfterHeader = ReadRequiredLine(reader, "raster data");
        string[] firstParts = SplitFields(firstLineAfterHeader);
        double noData = -9999d;
        string? firstDataLine = firstLineAfterHeader;
        if (firstParts.Length > 0 && firstParts[0].Equals("nodata_value", StringComparison.OrdinalIgnoreCase))
        {
            if (firstParts.Length != 2)
            {
                throw new FormatException("NODATA_value header must contain exactly one value.");
            }

            noData = ParseFiniteDouble(firstParts[1], "NODATA_value");
            firstDataLine = null;
        }

        var elevations = new double?[rows, columns];
        using IEnumerator<string> values = ReadDataValues(reader, firstDataLine).GetEnumerator();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (!values.MoveNext())
                {
                    throw new FormatException($"Missing cell value at row {row + 1}, column {column + 1}.");
                }

                double value = ParseFiniteDouble(values.Current, $"cell at row {row + 1}, column {column + 1}");
                elevations[row, column] = value == noData ? null : value;
            }
        }

        if (values.MoveNext())
        {
            throw new FormatException($"Raster contains extra cell values after the expected {rows * columns} samples.");
        }

        return new ElevationGrid(
            horizontalReference,
            verticalReference,
            new Coordinate2D(x, y),
            cellSize,
            cellSize,
            anchorConvention,
            GridRowOrder.NorthToSouth,
            elevations);
    }

    private static string ReadHeaderValue(TextReader reader, string expectedKey)
    {
        (string key, string value) = ReadHeaderPair(reader, expectedKey);
        if (!key.Equals(expectedKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Expected {expectedKey} header.");
        }

        return value;
    }

    private static (string Key, string Value) ReadHeaderPair(TextReader reader, string description)
    {
        string[] parts = SplitFields(ReadRequiredLine(reader, description));
        if (parts.Length != 2)
        {
            throw new FormatException($"{description} header must contain a keyword and one value.");
        }

        return (parts[0], parts[1]);
    }

    private static GridAnchorConvention ParseAnchorConvention(string xKey, string yKey)
    {
        if (xKey.Equals("xllcorner", StringComparison.OrdinalIgnoreCase)
            && yKey.Equals("yllcorner", StringComparison.OrdinalIgnoreCase))
        {
            return GridAnchorConvention.LowerLeftCorner;
        }

        if (xKey.Equals("xllcenter", StringComparison.OrdinalIgnoreCase)
            && yKey.Equals("yllcenter", StringComparison.OrdinalIgnoreCase))
        {
            return GridAnchorConvention.CellCenter;
        }

        throw new FormatException("X and Y origins must both use the corner form or both use the center form.");
    }

    private static string[] SplitFields(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> ReadDataValues(TextReader reader, string? firstDataLine)
    {
        if (firstDataLine is not null)
        {
            foreach (string value in SplitFields(firstDataLine))
            {
                yield return value;
            }
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            foreach (string value in SplitFields(line))
            {
                yield return value;
            }
        }
    }

    private static string ReadRequiredLine(TextReader reader, string description) =>
        reader.ReadLine() ?? throw new FormatException($"Missing {description}.");

    private static int ParsePositiveInteger(string text, string field)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new FormatException($"{field} must be a positive integer.");
        }

        return value;
    }

    private static double ParseFiniteDouble(string text, string field)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new FormatException($"{field} must be a finite invariant-culture number.");
        }

        return value;
    }
}
