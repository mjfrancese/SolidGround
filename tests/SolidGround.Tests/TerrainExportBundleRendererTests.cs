using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class TerrainExportBundleRendererTests
{
    [Fact]
    public void RenderingTheSamePayloadTwiceGivesIdenticalDocumentAndPointsBytes()
    {
        TerrainExportPayload payload = CreatePayload();

        TerrainExportBundle first = TerrainExportBundleRenderer.Render(payload, "renderer-repeat");
        TerrainExportBundle second = TerrainExportBundleRenderer.Render(payload, "renderer-repeat");

        Assert.Equal(first.DocumentBytes.ToArray(), second.DocumentBytes.ToArray());
        Assert.Equal(first.PointsBytes.ToArray(), second.PointsBytes.ToArray());
    }

    [Fact]
    public void RenderingAFreshlyConstructedValueEqualPayloadGivesIdenticalBytes()
    {
        TerrainExportBundle first = TerrainExportBundleRenderer.Render(CreatePayload(), "renderer-value-equal");
        TerrainExportBundle second = TerrainExportBundleRenderer.Render(CreatePayload(), "renderer-value-equal");

        Assert.Equal(first.DocumentBytes.ToArray(), second.DocumentBytes.ToArray());
        Assert.Equal(first.PointsBytes.ToArray(), second.PointsBytes.ToArray());
    }

    [Fact]
    public void RenderingUnderTheDeDeCultureGivesBytesIdenticalToInvariantCulture()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle invariantBundle = TerrainExportBundleRenderer.Render(payload, "renderer-culture");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            TerrainExportBundle deDeBundle = TerrainExportBundleRenderer.Render(payload, "renderer-culture");

            Assert.Equal(invariantBundle.DocumentBytes.ToArray(), deDeBundle.DocumentBytes.ToArray());
            Assert.Equal(invariantBundle.PointsBytes.ToArray(), deDeBundle.PointsBytes.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RenderedBytesContainNoCarriageReturnNoByteOrderMarkAndEndWithExactlyOneLineFeed()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 3);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-line-endings");
        byte[] documentBytes = bundle.DocumentBytes.ToArray();
        byte[] pointsBytes = bundle.PointsBytes.ToArray();

        Assert.DoesNotContain((byte)0x0D, documentBytes);
        Assert.DoesNotContain((byte)0x0D, pointsBytes);
        AssertNoByteOrderMark(documentBytes);
        AssertNoByteOrderMark(pointsBytes);

        Assert.Equal((byte)'\n', documentBytes[^1]);
        Assert.NotEqual((byte)'\n', documentBytes[^2]);
        Assert.Equal((byte)'\n', pointsBytes[^1]);
    }

    [Fact]
    public void DocumentPropertyOrderMatchesTheManifestAtEveryNestingLevel()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-property-order");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement root = document.RootElement;

        Assert.Equal(["schema", "schemaVersion", "provenance", "unitDefinitions", "points"], PropertyNames(root));

        JsonElement provenance = root.GetProperty("provenance");
        Assert.Equal(
            [
                "source", "horizontalTransformation", "sourceVerticalReference", "sourceHorizontalReferenceOrigin",
                "sourceVerticalReferenceOrigin", "localFrame", "simplification", "originalPointCount", "retainedPointCount", "elevationRange",
            ],
            PropertyNames(provenance));

        Assert.Equal(["sourceName", "datasetIdentifier", "collectionPeriod", "qualityLevel"], PropertyNames(provenance.GetProperty("source")));

        JsonElement horizontalTransformation = provenance.GetProperty("horizontalTransformation");
        Assert.Equal(
            ["sourceReference", "targetReference", "forwardOperation", "inverseOperation", "engineName", "engineVersion"],
            PropertyNames(horizontalTransformation));
        Assert.Equal(
            ["coordinateReferenceSystem", "datum", "kind", "unit", "axisOrder"],
            PropertyNames(horizontalTransformation.GetProperty("sourceReference")));
        Assert.Equal(["referenceKind", "linearUnit"], PropertyNames(horizontalTransformation.GetProperty("sourceReference").GetProperty("unit")));
        Assert.Equal(["format", "definition"], PropertyNames(horizontalTransformation.GetProperty("forwardOperation")));

        Assert.Equal(["datum", "unit", "geoidModel"], PropertyNames(provenance.GetProperty("sourceVerticalReference")));

        JsonElement localFrame = provenance.GetProperty("localFrame");
        Assert.Equal(["origin", "projectedHorizontalReference", "verticalReference", "outputUnit"], PropertyNames(localFrame));
        Assert.Equal(["x", "y", "elevation"], PropertyNames(localFrame.GetProperty("origin")));

        Assert.Equal(["pointBudget", "method"], PropertyNames(provenance.GetProperty("simplification")));
        Assert.Equal(["minimum", "maximum", "unit"], PropertyNames(provenance.GetProperty("elevationRange")));

        foreach (JsonElement unitDefinition in root.GetProperty("unitDefinitions").EnumerateArray())
        {
            Assert.Equal(["unit", "metersPerUnit", "definition"], PropertyNames(unitDefinition));
        }

        Assert.Equal(["file", "format", "columns", "unit", "count", "sha256"], PropertyNames(root.GetProperty("points")));
    }

    [Fact]
    public void SourceReferenceOriginsAreWrittenAsTheirEnumMemberNamesImmediatelyAfterSourceVerticalReference()
    {
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            Source(),
            Transformation(),
            VerticalReference(),
            ReferenceOrigin.SourceMetadataResponse,
            ReferenceOrigin.DatasetDocumentation,
            LocalFrame(),
            new SimplificationRequest(),
            originalPointCount: 1,
            retainedPointCount: 1,
            elevationRange: new ElevationRange(1d, 1d, LengthUnit.InternationalFoot));
        TerrainExportPayload payload = new([new LocalTerrainSample(new LocalCoordinate(1d, 1d, 1d))], provenance);

        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-reference-origins");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement provenanceElement = document.RootElement.GetProperty("provenance");
        Assert.Equal(
            [
                "source", "horizontalTransformation", "sourceVerticalReference", "sourceHorizontalReferenceOrigin",
                "sourceVerticalReferenceOrigin", "localFrame", "simplification", "originalPointCount", "retainedPointCount", "elevationRange",
            ],
            PropertyNames(provenanceElement));
        Assert.Equal("SourceMetadataResponse", provenanceElement.GetProperty("sourceHorizontalReferenceOrigin").GetString());
        Assert.Equal("DatasetDocumentation", provenanceElement.GetProperty("sourceVerticalReferenceOrigin").GetString());
    }

    [Fact]
    public void NullCollectionPeriodQualityLevelAndGeoidModelAreWrittenAsExplicitNulls()
    {
        TerrainExportPayload payload = CreatePayload(collectionPeriod: null, qualityLevel: null, geoidModel: null);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-explicit-nulls");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement provenance = document.RootElement.GetProperty("provenance");

        Assert.Equal(JsonValueKind.Null, provenance.GetProperty("source").GetProperty("collectionPeriod").ValueKind);
        Assert.Equal(JsonValueKind.Null, provenance.GetProperty("source").GetProperty("qualityLevel").ValueKind);
        Assert.Equal(JsonValueKind.Null, provenance.GetProperty("sourceVerticalReference").GetProperty("geoidModel").ValueKind);
        Assert.Equal(JsonValueKind.Null, provenance.GetProperty("localFrame").GetProperty("verticalReference").GetProperty("geoidModel").ValueKind);
    }

    [Fact]
    public void UnitDefinitionsContainsExactlyTheDistinctUnitsUsedInEnumOrderWithBitExactMetersPerUnit()
    {
        TerrainExportPayload payload = CreatePayload(
            projectedUnit: LengthUnit.UsSurveyFoot, verticalUnit: LengthUnit.InternationalFoot, outputUnit: LengthUnit.Meter);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-unit-definitions");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement[] unitDefinitions = [.. document.RootElement.GetProperty("unitDefinitions").EnumerateArray()];

        Assert.Equal(3, unitDefinitions.Length);
        AssertUnitDefinition(unitDefinitions[0], nameof(LengthUnit.Meter), 1d, "1 m");
        AssertUnitDefinition(unitDefinitions[1], nameof(LengthUnit.UsSurveyFoot), 1200d / 3937d, "1200/3937 m");
        AssertUnitDefinition(unitDefinitions[2], nameof(LengthUnit.InternationalFoot), 0.3048d, "0.3048 m");
    }

    [Fact]
    public void PointsShaTwoFiveSixEqualsAnIndependentlyComputedHashOfThePointsBytes()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 4);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-sha256");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string sha256 = document.RootElement.GetProperty("points").GetProperty("sha256").GetString()!;

        string expected = Convert.ToHexStringLower(SHA256.HashData(bundle.PointsBytes.Span));
        Assert.Equal(expected, sha256);
    }

    [Fact]
    public void PointsCountEqualsTheCsvLineCount()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 5);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-points-count");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        int count = document.RootElement.GetProperty("points").GetProperty("count").GetInt32();

        string csvText = Encoding.UTF8.GetString(bundle.PointsBytes.Span);
        int lineCount = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.Equal(5, count);
        Assert.Equal(lineCount, count);
    }

    [Fact]
    public void RenderRejectsAZeroOriginalPointCountWithTerrainExportException()
    {
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            Source(),
            Transformation(),
            VerticalReference(),
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            LocalFrame(),
            new SimplificationRequest(),
            originalPointCount: 0,
            retainedPointCount: 0,
            elevationRange: null);
        TerrainExportPayload payload = new([], provenance);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() =>
            TerrainExportBundleRenderer.Render(payload, "renderer-empty"));
        Assert.Contains("Nothing to export", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderRejectsASchemaVersionOtherThanTheCurrentSchemaVersionWithTerrainExportException()
    {
        TerrainProvenance provenance = new(
            3,
            Source(),
            Transformation(),
            VerticalReference(),
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            LocalFrame(),
            new SimplificationRequest(),
            originalPointCount: 1,
            retainedPointCount: 1,
            elevationRange: new ElevationRange(1d, 1d, LengthUnit.InternationalFoot));
        TerrainExportPayload payload = new([new LocalTerrainSample(new LocalCoordinate(1d, 1d, 1d))], provenance);

        Assert.Throws<TerrainExportException>(() => TerrainExportBundleRenderer.Render(payload, "renderer-wrong-schema"));
    }

    [Fact]
    public void RenderRejectsANullBaseNameWithArgumentException()
    {
        TerrainExportPayload payload = CreatePayload();
        Assert.Throws<ArgumentException>(() => TerrainExportBundleRenderer.Render(payload, null!));
    }

    [Theory]
    [InlineData("example-site-synthetic", true)]
    [InlineData("Example_Site.v2", true)]
    [InlineData("a", true)]
    [InlineData("123-fixture_v1", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("has space", false)]
    [InlineData("bad$char", false)]
    [InlineData(".starts-with-dot", false)]
    [InlineData("-starts-with-dash", false)]
    [InlineData("has/slash", false)]
    [InlineData("has\\backslash", false)]
    [InlineData("name.solidground.json", false)]
    [InlineData("name.points.csv", false)]
    public void BaseNameValidationAcceptsOrRejectsEachDocumentedShape(string baseName, bool isAccepted)
    {
        TerrainExportPayload payload = CreatePayload();

        if (isAccepted)
        {
            TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, baseName);
            Assert.Equal(baseName + TerrainExportBundleRenderer.DocumentFileSuffix, bundle.DocumentFileName);
            Assert.Equal(baseName + TerrainExportBundleRenderer.PointsFileSuffix, bundle.PointsFileName);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => TerrainExportBundleRenderer.Render(payload, baseName));
        }
    }

    [Fact]
    public void CsvNumbersRoundTripBitExactlyWithInvariantCultureParsing()
    {
        LocalTerrainSample[] samples =
        [
            new LocalTerrainSample(new LocalCoordinate(1d / 3d, -0d, double.Epsilon)),
            new LocalTerrainSample(new LocalCoordinate(123456.789012345d, -987654.321d, 42d)),
        ];
        TerrainExportPayload payload = CreatePayload(samples: samples);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-csv-roundtrip");

        string csvText = Encoding.UTF8.GetString(bundle.PointsBytes.Span);
        string[] lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(samples.Length, lines.Length);

        for (int index = 0; index < samples.Length; index++)
        {
            string[] fields = lines[index].Split(',');
            Assert.Equal(3, fields.Length);

            double x = double.Parse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture);
            double y = double.Parse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture);
            double elevation = double.Parse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture);

            Assert.Equal(BitConverter.DoubleToInt64Bits(samples[index].Position.X), BitConverter.DoubleToInt64Bits(x));
            Assert.Equal(BitConverter.DoubleToInt64Bits(samples[index].Position.Y), BitConverter.DoubleToInt64Bits(y));
            Assert.Equal(BitConverter.DoubleToInt64Bits(samples[index].Position.Elevation), BitConverter.DoubleToInt64Bits(elevation));
        }
    }

    [Fact]
    public void APayloadWithZeroRetainedSamplesButPositiveOriginalPointCountRendersAZeroByteCsvAndCountZero()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 0, originalPointCount: 3);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "renderer-zero-retained");

        Assert.Equal(0, bundle.PointsBytes.Length);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        Assert.Equal(0, document.RootElement.GetProperty("points").GetProperty("count").GetInt32());
    }

    private static void AssertNoByteOrderMark(byte[] bytes)
    {
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.False(hasBom, "Rendered bytes must not begin with a UTF-8 byte-order mark.");
    }

    private static void AssertUnitDefinition(JsonElement element, string expectedUnit, double expectedMetersPerUnit, string expectedDefinition)
    {
        Assert.Equal(expectedUnit, element.GetProperty("unit").GetString());
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(expectedMetersPerUnit),
            BitConverter.DoubleToInt64Bits(element.GetProperty("metersPerUnit").GetDouble()));
        Assert.Equal(expectedDefinition, element.GetProperty("definition").GetString());
    }

    private static List<string> PropertyNames(JsonElement obj) => [.. obj.EnumerateObject().Select(property => property.Name)];

    private static LocalTerrainSample[] DefaultSamples(int count) =>
        [.. Enumerable.Range(0, count).Select(index => new LocalTerrainSample(new LocalCoordinate(index + 0.5d, index + 1.25d, index + 2.125d)))];

    private static TerrainExportPayload CreatePayload(
        int retainedPointCount = 2,
        int originalPointCount = 5,
        IReadOnlyList<LocalTerrainSample>? samples = null,
        CollectionPeriod? collectionPeriod = null,
        string? qualityLevel = null,
        string? geoidModel = "Geoid12B",
        LengthUnit verticalUnit = LengthUnit.InternationalFoot,
        LengthUnit projectedUnit = LengthUnit.Meter,
        LengthUnit outputUnit = LengthUnit.UsSurveyFoot)
    {
        IReadOnlyList<LocalTerrainSample> effectiveSamples = samples ?? DefaultSamples(retainedPointCount);
        TerrainProvenance provenance = CreateProvenance(
            effectiveSamples.Count,
            Math.Max(originalPointCount, effectiveSamples.Count),
            collectionPeriod,
            qualityLevel,
            geoidModel,
            verticalUnit,
            projectedUnit,
            outputUnit);

        return new TerrainExportPayload(effectiveSamples, provenance);
    }

    private static TerrainProvenance CreateProvenance(
        int retainedPointCount,
        int originalPointCount,
        CollectionPeriod? collectionPeriod,
        string? qualityLevel,
        string? geoidModel,
        LengthUnit verticalUnit,
        LengthUnit projectedUnit,
        LengthUnit outputUnit)
    {
        VerticalReference vertical = VerticalReference(verticalUnit, geoidModel);
        HorizontalReference projected = ProjectedReference(projectedUnit);
        return new TerrainProvenance(
            TerrainProvenance.CurrentSchemaVersion,
            Source(collectionPeriod, qualityLevel),
            Transformation(projected),
            vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            new LocalCoordinateFrame(new Coordinate3D(10.5d, 20.25d, 30.125d), projected, vertical, outputUnit),
            new SimplificationRequest(15000, SimplificationMethod.CurvatureAware),
            originalPointCount,
            retainedPointCount,
            new ElevationRange(1.5d, 3.75d, verticalUnit));
    }

    private static ElevationSourceMetadata Source(CollectionPeriod? collectionPeriod = null, string? qualityLevel = null) =>
        new("OpenTopography", "USGS1m", collectionPeriod, qualityLevel);

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference(LengthUnit unit = LengthUnit.Meter) => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(unit), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.InternationalFoot, string? geoidModel = "Geoid12B") =>
        new("NAVD88", unit, geoidModel);

    private static HorizontalTransformationDefinition Transformation(HorizontalReference? target = null) => new(
        GeographicReference(),
        target ?? ProjectedReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "candidate-engine",
        "1.0");

    private static LocalCoordinateFrame LocalFrame(
        LengthUnit verticalUnit = LengthUnit.InternationalFoot, LengthUnit projectedUnit = LengthUnit.Meter, LengthUnit outputUnit = LengthUnit.UsSurveyFoot) =>
        new(new Coordinate3D(10.5d, 20.25d, 30.125d), ProjectedReference(projectedUnit), VerticalReference(verticalUnit), outputUnit);
}
