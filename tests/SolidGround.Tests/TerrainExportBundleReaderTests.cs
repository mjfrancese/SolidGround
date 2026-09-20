using System.Globalization;
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

public sealed class TerrainExportBundleReaderTests
{
    [Fact]
    public void ReadOfARenderedPayloadRoundTripsAnEqualProvenanceAndBitExactSamplesElevationRangeAndOrigin()
    {
        TerrainExportPayload payload = CreatePayload(
            retainedPointCount: 3,
            origin: new Coordinate3D(-0d, 0d, -0d),
            elevationRange: new ElevationRange(-0d, 0d, LengthUnit.InternationalFoot));
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-round-trip");

        TerrainExportPayload roundTripped = TerrainExportBundleReader.Read(bundle.DocumentBytes.Span, bundle.PointsBytes.Span);

        Assert.Equal(payload.Provenance, roundTripped.Provenance);
        Assert.Equal(payload.Samples, roundTripped.Samples);

        // Record equality treats -0.0/0.0 (and NaN/NaN) as equal, so the assertions above alone would not catch
        // a sign-of-zero regression in the reader. Re-check every double the design note's "Reconstructing
        // source coordinates" section says must reverse exactly, bit for bit.
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(payload.Provenance.ElevationRange!.Minimum),
            BitConverter.DoubleToInt64Bits(roundTripped.Provenance.ElevationRange!.Minimum));
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(payload.Provenance.ElevationRange.Maximum),
            BitConverter.DoubleToInt64Bits(roundTripped.Provenance.ElevationRange.Maximum));

        Assert.Equal(
            BitConverter.DoubleToInt64Bits(payload.Provenance.LocalFrame.Origin.X),
            BitConverter.DoubleToInt64Bits(roundTripped.Provenance.LocalFrame.Origin.X));
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(payload.Provenance.LocalFrame.Origin.Y),
            BitConverter.DoubleToInt64Bits(roundTripped.Provenance.LocalFrame.Origin.Y));
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(payload.Provenance.LocalFrame.Origin.Elevation),
            BitConverter.DoubleToInt64Bits(roundTripped.Provenance.LocalFrame.Origin.Elevation));
    }

    [Fact]
    public void ReadProvenanceAloneReconstructsTheProvenanceRecordWithoutRequiringPointsBytes()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 2);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-provenance-only");

        TerrainProvenance provenance = TerrainExportBundleReader.ReadProvenance(bundle.DocumentBytes.Span);

        Assert.Equal(payload.Provenance, provenance);
    }

    [Fact]
    public void ReadProvenanceRejectsATruncatedDocument()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-truncated");
        byte[] documentBytes = bundle.DocumentBytes.ToArray();
        byte[] truncated = documentBytes[..(documentBytes.Length / 2)];

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(truncated));
        Assert.Contains("not well-formed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentWithATrailingComma()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-trailing-comma");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string sha256RawText = document.RootElement.GetProperty("points").GetProperty("sha256").GetRawText();
        string token = $"\"sha256\": {sha256RawText}";
        string tamperedText = ReplaceExactlyOnce(documentText, token, token + ",");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("not well-formed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentWhoseRootIsNotAnObject()
    {
        byte[] arrayRootBytes = Encoding.UTF8.GetBytes("[]");

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(arrayRootBytes));
        Assert.Contains("object", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRejectsAPointsFileWhoseShaTwoFiveSixHasAFlippedHexDigit()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-sha256");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string originalSha256 = document.RootElement.GetProperty("points").GetProperty("sha256").GetString()!;
        char originalChar = originalSha256[0];
        char flippedChar = originalChar == '0' ? '1' : '0';
        string tamperedSha256 = flippedChar + originalSha256[1..];

        string tamperedText = ReplaceExactlyOnce(documentText, $"\"sha256\": \"{originalSha256}\"", $"\"sha256\": \"{tamperedSha256}\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() =>
            TerrainExportBundleReader.Read(tamperedBytes, bundle.PointsBytes.Span));
        Assert.Contains("sha256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRejectsPointsBytesWithACsvLineRemoved()
    {
        TerrainExportPayload payload = CreatePayload(retainedPointCount: 3);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-removed-line");
        string pointsText = Encoding.UTF8.GetString(bundle.PointsBytes.Span);
        string[] lines = pointsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2, "This test needs at least two CSV lines to remove one meaningfully.");

        string tamperedPointsText = string.Concat(lines.Skip(1).Select(line => line + "\n"));
        byte[] tamperedPointsBytes = Encoding.UTF8.GetBytes(tamperedPointsText);

        Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.Read(bundle.DocumentBytes.Span, tamperedPointsBytes));
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentWhoseSchemaIsChanged()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-schema");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(
            documentText,
            $"\"schema\": \"{TerrainExportBundleRenderer.DocumentSchema}\"",
            "\"schema\": \"not-solidground\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsASchemaVersionOtherThanTheCurrentSchemaVersion()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-schemaversion");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(
            documentText,
            $"\"schemaVersion\": {TerrainProvenance.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            "\"schemaVersion\": 3");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
    }

    [Fact]
    public void ReadProvenanceAcceptsSchemaVersionTwoAndReadsBothNewReferenceOriginFields()
    {
        TerrainExportPayload payload = CreatePayload(
            horizontalReferenceOrigin: ReferenceOrigin.SourceMetadataResponse,
            verticalReferenceOrigin: ReferenceOrigin.DatasetDocumentation);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-schema-version-two");

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());

        TerrainProvenance provenance = TerrainExportBundleReader.ReadProvenance(bundle.DocumentBytes.Span);
        Assert.Equal(ReferenceOrigin.SourceMetadataResponse, provenance.SourceHorizontalReferenceOrigin);
        Assert.Equal(ReferenceOrigin.DatasetDocumentation, provenance.SourceVerticalReferenceOrigin);
    }

    [Fact]
    public void ReadProvenanceRejectsAnUnrecognizedSourceHorizontalReferenceOriginValue()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-horizontal-origin");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(
            documentText,
            $"\"sourceHorizontalReferenceOrigin\": \"{ReferenceOrigin.Operator}\"",
            "\"sourceHorizontalReferenceOrigin\": \"NotARealOrigin\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("sourceHorizontalReferenceOrigin", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NotARealOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProvenanceRejectsAnUnrecognizedSourceVerticalReferenceOriginValue()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-vertical-origin");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(
            documentText,
            $"\"sourceVerticalReferenceOrigin\": \"{ReferenceOrigin.Operator}\"",
            "\"sourceVerticalReferenceOrigin\": \"NotARealOrigin\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("sourceVerticalReferenceOrigin", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NotARealOrigin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProvenanceRejectsAVersionOneDocumentBecauseVersionOneNeverWroteTheReferenceOriginFields()
    {
        // A genuine version 1 document (from before SolidGround Issue #21) never wrote
        // sourceHorizontalReferenceOrigin/sourceVerticalReferenceOrigin at all; reconstructing one exactly
        // means removing both properties in addition to changing the schemaVersion number, so this document
        // is built by hand rather than by tampering with a version 2 rendering.
        const string version1Document = """
            {
              "schema": "solidground.terrain-export",
              "schemaVersion": 1,
              "provenance": {
                "source": { "sourceName": "OpenTopography", "datasetIdentifier": "USGS1m", "collectionPeriod": null, "qualityLevel": null },
                "horizontalTransformation": {
                  "sourceReference": { "coordinateReferenceSystem": "EPSG:4326", "datum": "WGS84", "kind": "Geographic", "unit": { "referenceKind": "Geographic", "linearUnit": null }, "axisOrder": "LongitudeLatitude" },
                  "targetReference": { "coordinateReferenceSystem": "EPSG:26915", "datum": "NAD83(2011)", "kind": "Projected", "unit": { "referenceKind": "Projected", "linearUnit": "Meter" }, "axisOrder": "EastingNorthing" },
                  "forwardOperation": { "format": "PROJJSON", "definition": "forward operation" },
                  "inverseOperation": { "format": "PROJJSON", "definition": "inverse operation" },
                  "engineName": "candidate-engine",
                  "engineVersion": "1.0"
                },
                "sourceVerticalReference": { "datum": "NAVD88", "unit": "InternationalFoot", "geoidModel": "Geoid12B" },
                "localFrame": {
                  "origin": { "x": 10.5, "y": 20.25, "elevation": 30.125 },
                  "projectedHorizontalReference": { "coordinateReferenceSystem": "EPSG:26915", "datum": "NAD83(2011)", "kind": "Projected", "unit": { "referenceKind": "Projected", "linearUnit": "Meter" }, "axisOrder": "EastingNorthing" },
                  "verticalReference": { "datum": "NAVD88", "unit": "InternationalFoot", "geoidModel": "Geoid12B" },
                  "outputUnit": "UsSurveyFoot"
                },
                "simplification": { "pointBudget": 15000, "method": "CurvatureAware" },
                "originalPointCount": 1,
                "retainedPointCount": 1,
                "elevationRange": { "minimum": 1.5, "maximum": 3.75, "unit": "InternationalFoot" }
              },
              "unitDefinitions": [
                { "unit": "Meter", "metersPerUnit": 1, "definition": "1 m" },
                { "unit": "InternationalFoot", "metersPerUnit": 0.3048, "definition": "0.3048 m" },
                { "unit": "UsSurveyFoot", "metersPerUnit": 0.3048006096012192, "definition": "1200/3937 m" }
              ],
              "points": { "file": "reader-version-one.points.csv", "format": "csv", "columns": ["x", "y", "elevation"], "unit": "UsSurveyFoot", "count": 1, "sha256": "0000000000000000000000000000000000000000000000000000000000000" }
            }
            """;

        TerrainExportException exception = Assert.Throws<TerrainExportException>(
            () => TerrainExportBundleReader.ReadProvenance(Encoding.UTF8.GetBytes(version1Document)));
        Assert.Contains("schemaVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProvenanceRejectsAFractionalSchemaVersion()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-schemaversion-fractional");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(
            documentText,
            $"\"schemaVersion\": {TerrainProvenance.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            "\"schemaVersion\": 1.5");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("integer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("\"NaN\"")]
    public void ReadProvenanceRejectsANonFiniteOrNonNumericRequiredDouble(string tamperedToken)
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-double-strictness");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string minimumRawText = document.RootElement.GetProperty("provenance").GetProperty("elevationRange").GetProperty("minimum").GetRawText();
        string token = $"\"minimum\": {minimumRawText}";
        string tamperedText = ReplaceExactlyOnce(documentText, token, $"\"minimum\": {tamperedToken}");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("finite", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsAnUnrecognizedPropertyUnderProvenance()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-unknown-property");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string sourceRawText = document.RootElement.GetProperty("provenance").GetProperty("source").GetRawText();
        string token = $"\"source\": {sourceRawText}";
        string tamperedText = ReplaceExactlyOnce(documentText, token, $"\"unexpectedProperty\": true, {token}");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("unrecognized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentMissingARequiredProperty()
    {
        TerrainExportPayload payload = CreatePayload(qualityLevel: "QL2");
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-missing-property");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement source = document.RootElement.GetProperty("provenance").GetProperty("source");
        string tamperedText = RemoveJsonProperty(documentText, source, "datasetIdentifier");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadProvenanceRejectsAUnitDefinitionWhoseMetersPerUnitIsAlteredInItsLastDigit()
    {
        TerrainExportPayload payload = CreatePayload(projectedUnit: LengthUnit.UsSurveyFoot);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-metersperunit");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        JsonElement usSurveyFootDefinition = document.RootElement.GetProperty("unitDefinitions").EnumerateArray()
            .Single(element => string.Equals(element.GetProperty("unit").GetString(), nameof(LengthUnit.UsSurveyFoot), StringComparison.Ordinal));
        string originalMetersPerUnitText = usSurveyFootDefinition.GetProperty("metersPerUnit").GetRawText();
        string tamperedMetersPerUnitText = TamperLastDigit(originalMetersPerUnitText);

        string tamperedText = ReplaceExactlyOnce(documentText, originalMetersPerUnitText, tamperedMetersPerUnitText);
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("metersPerUnit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProvenanceRejectsAnEnumStringInTheWrongCase()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-enum-case");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(documentText, "\"method\": \"CurvatureAware\"", "\"method\": \"curvatureAware\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" UsSurveyFoot ")]
    [InlineData("UsSurveyFoot,Meter")]
    public void ReadProvenanceRejectsAnEnumStringThatIsNotAnExactDefinedMemberName(string tamperedValue)
    {
        // Enum.TryParse alone (unlike the exact Enum.GetNames<TEnum> match RequireEnum actually performs)
        // would also accept a defined member's numeric value, a whitespace-padded name, or a comma-joined name
        // list, silently reinterpreting a corrupted document as a different, still-valid value.
        TerrainExportPayload payload = CreatePayload(outputUnit: LengthUnit.UsSurveyFoot);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-enum-not-exact");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        string tamperedText = ReplaceExactlyOnce(documentText, "\"outputUnit\": \"UsSurveyFoot\"", $"\"outputUnit\": \"{tamperedValue}\"");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentWhosePointsUnitIsChanged()
    {
        TerrainExportPayload payload = CreatePayload(verticalUnit: LengthUnit.InternationalFoot, outputUnit: LengthUnit.UsSurveyFoot);
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-points-unit");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        // "unit" is also a unitDefinitions array property name, so the points object's own "unit" is tampered
        // by scoping the replacement to the points object's raw text (unique in the document) rather than to
        // the bare "unit": "UsSurveyFoot" token (which also matches a unitDefinitions entry).
        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string pointsRawText = document.RootElement.GetProperty("points").GetRawText();
        string tamperedPointsRawText = ReplaceExactlyOnce(pointsRawText, "\"unit\": \"UsSurveyFoot\"", "\"unit\": \"InternationalFoot\"");
        string tamperedText = ReplaceExactlyOnce(documentText, pointsRawText, tamperedPointsRawText);
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("points.unit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProvenanceRejectsADocumentWithANullElevationRange()
    {
        TerrainExportPayload payload = CreatePayload();
        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, "reader-tamper-null-elevation-range");
        string documentText = Encoding.UTF8.GetString(bundle.DocumentBytes.Span);

        using JsonDocument document = JsonDocument.Parse(bundle.DocumentBytes);
        string elevationRangeRawText = document.RootElement.GetProperty("provenance").GetProperty("elevationRange").GetRawText();
        string token = $"\"elevationRange\": {elevationRangeRawText}";
        string tamperedText = ReplaceExactlyOnce(documentText, token, "\"elevationRange\": null");
        byte[] tamperedBytes = Encoding.UTF8.GetBytes(tamperedText);

        TerrainExportException exception = Assert.Throws<TerrainExportException>(() => TerrainExportBundleReader.ReadProvenance(tamperedBytes));
        Assert.Contains("must not be null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- tamper helpers: mutate the rendered UTF-8 text deterministically rather than hand-writing documents ----

    private static string ReplaceExactlyOnce(string text, string oldValue, string newValue)
    {
        int firstIndex = text.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Token '{oldValue}' was not found in the rendered document.");
        Assert.True(
            text.IndexOf(oldValue, firstIndex + 1, StringComparison.Ordinal) < 0,
            $"Token '{oldValue}' was not unique in the rendered document.");

        return text[..firstIndex] + newValue + text[(firstIndex + oldValue.Length)..];
    }

    private static string RemoveJsonProperty(string documentText, JsonElement parent, string propertyName)
    {
        string token = $"\"{propertyName}\": {parent.GetProperty(propertyName).GetRawText()},";
        return ReplaceExactlyOnce(documentText, token, string.Empty);
    }

    private static string TamperLastDigit(string numberText)
    {
        char lastChar = numberText[^1];
        char replacement = lastChar == '1' ? '2' : '1';
        return numberText[..^1] + replacement;
    }

    // ---- payload factory (mirrors ContractModelTests' small private static factory pattern) ----------------

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
        LengthUnit outputUnit = LengthUnit.UsSurveyFoot,
        Coordinate3D? origin = null,
        ElevationRange? elevationRange = null,
        ReferenceOrigin horizontalReferenceOrigin = ReferenceOrigin.Operator,
        ReferenceOrigin verticalReferenceOrigin = ReferenceOrigin.Operator)
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
            outputUnit,
            origin,
            elevationRange,
            horizontalReferenceOrigin,
            verticalReferenceOrigin);

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
        LengthUnit outputUnit,
        Coordinate3D? origin,
        ElevationRange? elevationRange,
        ReferenceOrigin horizontalReferenceOrigin = ReferenceOrigin.Operator,
        ReferenceOrigin verticalReferenceOrigin = ReferenceOrigin.Operator)
    {
        VerticalReference vertical = VerticalReference(verticalUnit, geoidModel);
        HorizontalReference projected = ProjectedReference(projectedUnit);
        return new TerrainProvenance(
            TerrainProvenance.CurrentSchemaVersion,
            Source(collectionPeriod, qualityLevel),
            Transformation(projected),
            vertical,
            horizontalReferenceOrigin,
            verticalReferenceOrigin,
            new LocalCoordinateFrame(origin ?? new Coordinate3D(10.5d, 20.25d, 30.125d), projected, vertical, outputUnit),
            new SimplificationRequest(15000, SimplificationMethod.CurvatureAware),
            originalPointCount,
            retainedPointCount,
            elevationRange ?? new ElevationRange(1.5d, 3.75d, verticalUnit));
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
}
