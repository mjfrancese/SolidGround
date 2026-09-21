using System.Text;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="RasterSourceSidecarIo"/>, lifted into <c>SolidGround.Core</c> for SolidGround
/// Issue #15 (§0.2's fourth structural gap). No such test existed at any visibility level before this move:
/// the type was internal to <c>SolidGround.Cli</c> and exercised only indirectly, through black-box
/// <c>CliApplication.RunAsync</c> calls, inside <c>CliProcessCommandTests.cs</c> and
/// <c>CliFetchAndRunCommandTests.cs</c>. This file ports representative cases from those black-box suites
/// directly against the now-public Core type, asserting the corrected <see cref="FormatException"/> type
/// (was the CLI-only <c>CliUsageException</c>) rather than only a CLI exit code.
/// </summary>
public sealed class RasterSourceSidecarIoTests
{
    // Shaped exactly like RasterSourceSidecarIo.Write's own output (fixed property order), so each
    // strict-reader test below can mutate exactly one property and know the rest of the document is valid.
    // See docs/architecture/cli-workflow.md's "Raster set persistence" section.
    private const string ValidJson = """
        {
          "schema": "solidground.raster-source",
          "schemaVersion": 3,
          "sourceName": "OpenTopography",
          "datasetIdentifier": "USGS1m",
          "collectionPeriod": {
            "start": "2024-05-01",
            "end": "2024-05-02"
          },
          "qualityLevel": "QL2",
          "vertical": {
            "datum": "NAVD88",
            "unit": "UsSurveyFoot",
            "geoidModel": "Geoid12B"
          },
          "horizontalReferenceOrigin": "SourceResponse",
          "verticalReferenceOrigin": "SourceResponse",
          "acquisition": {
            "redactedRequestUri": "https://example.test/api?API_Key=REDACTED",
            "statusCode": 200,
            "contentType": "application/zip",
            "contentDispositionFileName": "USGS1m.zip",
            "archiveEntryNames": ["USGS1m.asc", "USGS1m.prj"],
            "referenceSource": "PrjSidecar",
            "responseByteCount": 12345,
            "metadataRequest": null,
            "fetchEnvelope": {
              "west": [withheld],
              "south": [withheld],
              "east": [withheld],
              "north": [withheld],
              "minimumSideMeters": 110.0,
              "expanded": false,
              "widthBeforeMeters": 121.8,
              "heightBeforeMeters": 154.8,
              "widthAfterMeters": 121.8,
              "heightAfterMeters": 154.8
            }
          }
        }
        """;

    [Fact]
    public void WriteThenReadRoundTripsEveryFieldUnchanged()
    {
        RasterSourceSidecar original = BuildSidecar();

        using MemoryStream stream = new();
        RasterSourceSidecarIo.Write(original, stream);

        RasterSourceSidecar roundTripped = RasterSourceSidecarIo.Read(stream.ToArray(), "sidecar.source.json");

        // ArchiveEntryNames is an IReadOnlyList<string>: the record's own synthesized equality compares it by
        // reference (via EqualityComparer<IReadOnlyList<string>>.Default), which two independently-built lists
        // never satisfy even with identical contents. Asserted separately here, by sequence, with xUnit's own
        // enumerable-aware Assert.Equal; the with-expression below then lets the outer record comparison cover
        // every other field, unaffected by this one reference-typed member.
        Assert.Equal(original.Acquisition.ArchiveEntryNames, roundTripped.Acquisition.ArchiveEntryNames);
        RasterSourceSidecar originalWithComparableArchiveEntryNames = original with
        {
            Acquisition = original.Acquisition with { ArchiveEntryNames = roundTripped.Acquisition.ArchiveEntryNames },
        };
        Assert.Equal(originalWithComparableArchiveEntryNames, roundTripped);
    }

    [Fact]
    public void ReadThrowsFormatExceptionOnMalformedJson()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{ this is not well-formed json ");

        FormatException exception = Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(bytes, "malformed.source.json"));
        Assert.Contains("malformed.source.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadThrowsFormatExceptionOnAnUnrecognizedSchemaOrSchemaVersion()
    {
        string wrongSchema = ValidJson.Replace("\"schema\": \"solidground.raster-source\",", "\"schema\": \"not-the-right-schema\",", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(wrongSchema), "path"));

        string wrongVersion = ValidJson.Replace("\"schemaVersion\": 3,", "\"schemaVersion\": 4,", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(wrongVersion), "path"));
    }

    [Fact]
    public void ReadThrowsFormatExceptionOnAMissingOrUnrecognizedProperty()
    {
        string missing = ValidJson.Replace("\"datasetIdentifier\": \"USGS1m\",", string.Empty, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(missing), "path"));

        string unrecognized = ValidJson.Replace(
            "\"qualityLevel\": \"QL2\",", "\"qualityLevel\": \"QL2\", \"zzzSentinelExtra\": \"zzz-should-not-appear\",", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(unrecognized), "path"));
    }

    [Fact]
    public void ReadThrowsFormatExceptionOnAWrongValueKind()
    {
        string wrongKind = ValidJson.Replace("\"sourceName\": \"OpenTopography\",", "\"sourceName\": 424242,", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(wrongKind), "path"));
    }

    [Fact]
    public void ReadNeverEchoesTheOffendingValueInItsMessage()
    {
        const string sentinel = "Furlongs";
        string json = ValidJson.Replace("\"unit\": \"UsSurveyFoot\",", $"\"unit\": \"{sentinel}\",", StringComparison.Ordinal);

        FormatException exception = Assert.Throws<FormatException>(() => RasterSourceSidecarIo.Read(Utf8(json), "path"));

        Assert.DoesNotContain(sentinel, exception.Message, StringComparison.Ordinal);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static RasterSourceSidecar BuildSidecar() => new(
        "OpenTopography",
        "USGS1m",
        new CollectionPeriod(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 2)),
        "QL2",
        new RasterSourceVertical("NAVD88", LengthUnit.UsSurveyFoot, "Geoid12B"),
        ReferenceOrigin.SourceResponse,
        ReferenceOrigin.SourceResponse,
        new RasterSourceAcquisition(
            "https://example.test/api?API_Key=REDACTED",
            200,
            "application/zip",
            "USGS1m.zip",
            ["USGS1m.asc", "USGS1m.prj"],
            "PrjSidecar",
            12345,
            null,
            new RasterSourceFetchEnvelope([withheld], [withheld], [withheld], [withheld], 110.0, false, 121.8, 154.8, 121.8, 154.8)));
}
