using System.Text.Json;
using System.Text.Json.Nodes;
using SolidGround.Core.Processing;
using SolidGround.Core.Simplification;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="TerrainRequestSettings.Validate"/> (every problem found, never short-circuiting) and
/// <see cref="TerrainRequestSettings.JsonOptions"/> (strict decode, every enum bound directly from its
/// documented camelCase token). See SolidGround Issue #15's design record §3/§4.
/// </summary>
public sealed class TerrainRequestSettingsTests
{
    // The exact §4.3 shipped template, byte-for-byte, including its Revit-only `level`/`toposolidType` name
    // overrides (RevitTargetSettings' own fields -- not part of TerrainRequestSettings at all). This
    // Core-only test never references RevitSettings/RevitTargetSettings; JsonOptionsDecodesTheShippedTemplateTextVerbatim
    // strips those two keys before decoding, simulating the split SolidGround.Revit's own settings I/O
    // performs between the Request-shaped and Target-shaped portions of the one flat document.
    private const string ShippedTemplateText = """
        // %ProgramData%\SolidGround\Revit\settings.json
        // SolidGround edits this file only to create it; it never rewrites an existing one.
        // Delete or rename this file to have SolidGround regenerate this template on the next run.
        {
          // "fetch": call OpenTopography live (needs OPENTOPOGRAPHY_API_KEY in Revit's own process environment).
          // "process": read a local AAIGrid .asc/.prj pair (and optional .source.json sidecar) from disk, no network.
          "mode": "process",

          "areaOfInterest": {
            // "boundingBox" | "radius" | "parcel" -- give exactly the matching object below.
            "kind": "parcel",
            "boundingBox": null,
            "radius": null,
            "parcel": { "path": "C:\\SolidGround\\parcel.geojson", "format": "geojson", "bufferMeters": 0.0 }
          },

          // Required when mode is "process"; ignored (may be omitted) when mode is "fetch".
          "process": {
            "asc": "C:\\SolidGround\\terrain.asc",
            "prj": null,
            "sourceJson": null,
            "sourceName": null, "dataset": null,
            "verticalDatum": null, "verticalUnit": null, "geoid": null,
            "collectionStart": null, "collectionEnd": null, "qualityLevel": null
          },

          // "southwest" | "centroid" | "explicit". x/y/z are only read when kind is "explicit".
          "localOrigin": { "kind": "southwest", "x": 0.0, "y": 0.0, "z": 0.0 },

          // "usSurveyFoot" | "internationalFoot" | "meter" -- exact 1200/3937 m and 0.3048 m definitions.
          "outputUnit": "usSurveyFoot",

          "simplification": { "method": "curvatureAware", "pointBudget": 15000, "coverageFloorFraction": 0.2 },

          // Blank/null means: pick the existing Level with the lowest elevation (ties by name).
          "level": { "name": null },
          // Blank/null means: pick the first existing ToposolidType by name.
          "toposolidType": { "name": null },

          "output": { "directory": "C:\\ProgramData\\SolidGround\\Revit\\Exports", "baseName": "terrain" },

          "networkTimeoutSeconds": 300
        }
        """;

    [Fact]
    public void ValidateReturnsNoProblemsForAMinimalValidFetchRequest()
    {
        IReadOnlyList<string> problems = MinimalFetch().Validate();

        Assert.Empty(problems);
    }

    [Fact]
    public void ValidateReturnsNoProblemsForAMinimalValidProcessRequest()
    {
        IReadOnlyList<string> problems = MinimalProcess().Validate();

        Assert.Empty(problems);
    }

    [Fact]
    public void ValidateReportsMissingProcessBlockWhenModeIsProcess()
    {
        TerrainRequestSettings settings = MinimalFetch() with { Mode = TerrainAcquisitionMode.Process };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("process", StringComparison.Ordinal) && p.Contains("required", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(50_001)]
    public void ValidateReportsOutOfRangePointBudget(int budget)
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with { Simplification = settings.Simplification with { PointBudget = budget } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("pointBudget", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ValidateReportsOutOfRangeCoverageFloor(double coverageFloor)
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with { Simplification = settings.Simplification with { CoverageFloorFraction = coverageFloor } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("coverageFloorFraction", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsMismatchedAoiKindAndSubObject()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with { AreaOfInterest = settings.AreaOfInterest with { BoundingBox = null } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAStaleParcelSubObjectAlongsideABoundingBoxKind()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with
        {
            AreaOfInterest = settings.AreaOfInterest with
            {
                Kind = AreaOfInterestKind.BoundingBox,
                Parcel = new ParcelAoiSettings { Path = @"C:\SolidGround\parcel.geojson", Format = "geojson", BufferMeters = 0 },
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest.parcel must be null when areaOfInterest.kind is 'boundingBox'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAStaleBoundingBoxSubObjectAlongsideARadiusKind()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with
        {
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.Radius,
                BoundingBox = settings.AreaOfInterest.BoundingBox,
                Radius = new RadiusAoiSettings { CenterLatitude = 41.59, CenterLongitude = -93.60, RadiusMeters = 50 },
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest.boundingBox must be null when areaOfInterest.kind is 'radius'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsStaleBoundingBoxAndRadiusSubObjectsAlongsideAParcelKind()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with
        {
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.Parcel,
                BoundingBox = settings.AreaOfInterest.BoundingBox,
                Radius = new RadiusAoiSettings { CenterLatitude = 41.59, CenterLongitude = -93.60, RadiusMeters = 50 },
                Parcel = new ParcelAoiSettings { Path = @"C:\SolidGround\parcel.geojson", Format = "geojson", BufferMeters = 0 },
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest.boundingBox must be null when areaOfInterest.kind is 'parcel'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("areaOfInterest.radius must be null when areaOfInterest.kind is 'parcel'", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReturnsEveryProblemNotJustTheFirst()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with
        {
            AreaOfInterest = settings.AreaOfInterest with { BoundingBox = null },
            Simplification = settings.Simplification with { PointBudget = 0, CoverageFloorFraction = 5d },
            Output = settings.Output with { Directory = "  " },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.True(problems.Count >= 4, $"Expected at least 4 problems, found {problems.Count}: {string.Join(" | ", problems)}");
    }

    [Fact]
    public void ValidateAcceptsAProcessSourceJsonPath()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { SourceJson = @"C:\SolidGround\terrain.source.json" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Empty(problems);
    }

    [Fact]
    public void ValidateReportsAProblemWhenOnlyCollectionStartIsGiven()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = "2024-01-01", CollectionEnd = null } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("collectionStart", StringComparison.Ordinal) && p.Contains("both be given or both be omitted", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemWhenOnlyCollectionEndIsGiven()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = null, CollectionEnd = "2024-01-01" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("collectionEnd", StringComparison.Ordinal) && p.Contains("both be given or both be omitted", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemForAMalformedCollectionStartDate()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = "01/01/2024", CollectionEnd = "2024-01-02" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("collectionStart must be a yyyy-MM-dd date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemForAMalformedCollectionEndDate()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = "2024-01-01", CollectionEnd = "not-a-date" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("collectionEnd must be a yyyy-MM-dd date", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemWhenCollectionStartIsAfterCollectionEnd()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = "2024-06-01", CollectionEnd = "2024-01-01" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("collectionStart must not be after process.collectionEnd", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsAWellFormedCollectionPeriodWithStartOnOrBeforeEnd()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { CollectionStart = "2024-01-01", CollectionEnd = "2024-01-01" } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ValidateReportsAProblemForAWhitespaceOnlyProcessSourceName(string blank)
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with { Process = settings.Process! with { SourceName = blank } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("process.sourceName", StringComparison.Ordinal) && p.Contains("must not be blank when given", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemForEachWhitespaceOnlyOptionalProcessField()
    {
        TerrainRequestSettings settings = MinimalProcess();
        settings = settings with
        {
            Process = settings.Process! with
            {
                Dataset = "  ",
                VerticalDatum = "  ",
                Geoid = "  ",
                QualityLevel = "  ",
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("process.dataset", StringComparison.Ordinal) && p.Contains("must not be blank when given", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("process.verticalDatum", StringComparison.Ordinal) && p.Contains("must not be blank when given", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("process.geoid", StringComparison.Ordinal) && p.Contains("must not be blank when given", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("process.qualityLevel", StringComparison.Ordinal) && p.Contains("must not be blank when given", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemInsteadOfThrowingWhenAreaOfInterestIsNull()
    {
        TerrainRequestSettings settings = MinimalFetch() with { AreaOfInterest = null! };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest", StringComparison.Ordinal) && p.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemInsteadOfThrowingWhenLocalOriginIsNull()
    {
        TerrainRequestSettings settings = MinimalFetch() with { LocalOrigin = null! };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("localOrigin", StringComparison.Ordinal) && p.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemInsteadOfThrowingWhenSimplificationIsNull()
    {
        TerrainRequestSettings settings = MinimalFetch() with { Simplification = null! };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("simplification", StringComparison.Ordinal) && p.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemInsteadOfThrowingWhenOutputIsNull()
    {
        TerrainRequestSettings settings = MinimalFetch() with { Output = null! };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("output", StringComparison.Ordinal) && p.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemInsteadOfThrowingWhenParcelPathIsNullAndFormatIsUnset()
    {
        TerrainRequestSettings settings = MinimalFetch() with
        {
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.Parcel,
                Parcel = new ParcelAoiSettings { Path = null!, Format = null, BufferMeters = 0 },
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest.parcel", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemWhenParcelPathIsBlankEvenWithFormatGivenExplicitly()
    {
        TerrainRequestSettings settings = MinimalFetch() with
        {
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.Parcel,
                Parcel = new ParcelAoiSettings { Path = "   ", Format = "wkt", BufferMeters = 0 },
            },
        };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("areaOfInterest.parcel", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsTinErrorAsTheSimplificationMethod()
    {
        TerrainRequestSettings settings = MinimalFetch();
        settings = settings with { Simplification = settings.Simplification with { Method = SimplificationMethod.TinError } };

        IReadOnlyList<string> problems = settings.Validate();

        Assert.Contains(problems, p => p.Contains("tinError", StringComparison.Ordinal));
    }

    [Fact]
    public void JsonOptionsDecodesTheShippedTemplateTextVerbatim()
    {
        JsonDocumentOptions documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonNode? node = JsonNode.Parse(ShippedTemplateText, documentOptions: documentOptions);
        JsonObject requestShapedPortion = Assert.IsType<JsonObject>(node);
        // "level"/"toposolidType" are RevitTargetSettings' own fields, not TerrainRequestSettings'; the real
        // RevitSettingsIo (Revit-side, SolidGround Issue #15 Stage 3) splits the one flat document the same
        // way before decoding each half.
        requestShapedPortion.Remove("level");
        requestShapedPortion.Remove("toposolidType");

        TerrainRequestSettings? settings = requestShapedPortion.Deserialize<TerrainRequestSettings>(TerrainRequestSettings.JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(TerrainAcquisitionMode.Process, settings!.Mode);
        Assert.Equal(AreaOfInterestKind.Parcel, settings.AreaOfInterest.Kind);
        Assert.Equal("C:\\SolidGround\\parcel.geojson", settings.AreaOfInterest.Parcel?.Path);
        Assert.Equal("C:\\SolidGround\\terrain.asc", settings.Process?.Asc);
        Assert.Equal(LocalOriginKind.Southwest, settings.LocalOrigin.Kind);
        Assert.Equal(LengthUnit.UsSurveyFoot, settings.OutputUnit);
        Assert.Equal(SimplificationMethod.CurvatureAware, settings.Simplification.Method);
        Assert.Equal(15000, settings.Simplification.PointBudget);
        Assert.Equal(0.2, settings.Simplification.CoverageFloorFraction);
        Assert.Equal("C:\\ProgramData\\SolidGround\\Revit\\Exports", settings.Output.Directory);
        Assert.Equal("terrain", settings.Output.BaseName);
        Assert.Equal(300, settings.NetworkTimeoutSeconds);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void JsonOptionsDecodesEveryDocumentedTokenForModeOutputUnitLocalOriginKindAndSimplificationMethod()
    {
        foreach ((string token, TerrainAcquisitionMode expected) in new (string, TerrainAcquisitionMode)[]
        {
            ("fetch", TerrainAcquisitionMode.Fetch),
            ("process", TerrainAcquisitionMode.Process),
        })
        {
            Assert.Equal(expected, DecodeMinimal(mode: token).Mode);
        }

        foreach ((string token, LengthUnit expected) in new (string, LengthUnit)[]
        {
            ("usSurveyFoot", LengthUnit.UsSurveyFoot),
            ("internationalFoot", LengthUnit.InternationalFoot),
            ("meter", LengthUnit.Meter),
        })
        {
            Assert.Equal(expected, DecodeMinimal(outputUnit: token).OutputUnit);
        }

        foreach ((string token, LocalOriginKind expected) in new (string, LocalOriginKind)[]
        {
            ("southwest", LocalOriginKind.Southwest),
            ("centroid", LocalOriginKind.Centroid),
            ("explicit", LocalOriginKind.Explicit),
        })
        {
            Assert.Equal(expected, DecodeMinimal(localOriginKind: token).LocalOrigin.Kind);
        }

        // Every token the converter must reproduce, including "tinError" -- Validate() (not the JSON layer)
        // is what rejects it as a settings choice (ValidateRejectsTinErrorAsTheSimplificationMethod above).
        foreach ((string token, SimplificationMethod expected) in new (string, SimplificationMethod)[]
        {
            ("curvatureAware", SimplificationMethod.CurvatureAware),
            ("uniformSampler", SimplificationMethod.UniformSampler),
            ("tinError", SimplificationMethod.TinError),
        })
        {
            Assert.Equal(expected, DecodeMinimal(simplificationMethod: token).Simplification.Method);
        }
    }

    [Fact]
    public void JsonOptionsThrowsAJsonExceptionForAnUnrecognizedEnumToken()
    {
        Assert.Throws<JsonException>(() => DecodeMinimal(mode: "bogus"));
    }

    [Fact]
    public void JsonOptionsThrowsAJsonExceptionWhenLocalOriginKindIsAbsent()
    {
        // localOrigin.kind is documented as required with no default (design record §4.1), the same tier
        // as mode/outputUnit/areaOfInterest.kind; an absent key must not silently bind
        // LocalOriginKind.Southwest, its enum default value.
        string json = """
            {
              "mode": "fetch",
              "areaOfInterest": { "kind": "boundingBox", "boundingBox": { "west": -93.7, "south": 41.5, "east": -93.6, "north": 41.6 }, "radius": null, "parcel": null },
              "process": null,
              "localOrigin": { "x": 1000.0, "y": 2000.0, "z": 50.0 },
              "outputUnit": "usSurveyFoot",
              "simplification": { "method": "curvatureAware", "pointBudget": 15000, "coverageFloorFraction": 0.2 },
              "output": { "directory": "C:\\SolidGround\\Exports", "baseName": "terrain" },
              "networkTimeoutSeconds": 300
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TerrainRequestSettings>(json, TerrainRequestSettings.JsonOptions));
    }

    [Fact]
    public void JsonOptionsDecodesAProcessVerticalUnitTokenDirectlyIntoALengthUnit()
    {
        string json = """
            {
              "mode": "process",
              "areaOfInterest": { "kind": "boundingBox", "boundingBox": { "west": -93.7, "south": 41.5, "east": -93.6, "north": 41.6 }, "radius": null, "parcel": null },
              "process": {
                "asc": "C:\\SolidGround\\terrain.asc", "prj": null, "sourceJson": null,
                "sourceName": null, "dataset": null,
                "verticalDatum": null, "verticalUnit": "meter", "geoid": null,
                "collectionStart": null, "collectionEnd": null, "qualityLevel": null
              },
              "localOrigin": { "kind": "southwest", "x": 0.0, "y": 0.0, "z": 0.0 },
              "outputUnit": "usSurveyFoot",
              "simplification": { "method": "curvatureAware", "pointBudget": 15000, "coverageFloorFraction": 0.2 },
              "output": { "directory": "C:\\SolidGround\\Exports", "baseName": "terrain" },
              "networkTimeoutSeconds": 300
            }
            """;

        TerrainRequestSettings? settings = JsonSerializer.Deserialize<TerrainRequestSettings>(json, TerrainRequestSettings.JsonOptions);

        Assert.Equal(LengthUnit.Meter, settings?.Process?.VerticalUnit);
    }

    private static TerrainRequestSettings DecodeMinimal(
        string mode = "fetch", string outputUnit = "usSurveyFoot", string localOriginKind = "southwest", string simplificationMethod = "curvatureAware")
    {
        string json = $$"""
            {
              "mode": "{{mode}}",
              "areaOfInterest": { "kind": "boundingBox", "boundingBox": { "west": -93.7, "south": 41.5, "east": -93.6, "north": 41.6 }, "radius": null, "parcel": null },
              "process": null,
              "localOrigin": { "kind": "{{localOriginKind}}", "x": 0.0, "y": 0.0, "z": 0.0 },
              "outputUnit": "{{outputUnit}}",
              "simplification": { "method": "{{simplificationMethod}}", "pointBudget": 15000, "coverageFloorFraction": 0.2 },
              "output": { "directory": "C:\\SolidGround\\Exports", "baseName": "terrain" },
              "networkTimeoutSeconds": 300
            }
            """;

        return JsonSerializer.Deserialize<TerrainRequestSettings>(json, TerrainRequestSettings.JsonOptions)!;
    }

    private static TerrainRequestSettings MinimalFetch() => new()
    {
        Mode = TerrainAcquisitionMode.Fetch,
        AreaOfInterest = new AoiSettings
        {
            Kind = AreaOfInterestKind.BoundingBox,
            BoundingBox = new BoundingBoxAoiSettings { West = -93.7, South = 41.5, East = -93.6, North = 41.6 },
        },
        LocalOrigin = new LocalOriginRequest(LocalOriginKind.Southwest, 0d, 0d, 0d),
        OutputUnit = LengthUnit.UsSurveyFoot,
        Simplification = new SimplificationSettings { Method = SimplificationMethod.CurvatureAware, PointBudget = 15000, CoverageFloorFraction = 0.2 },
        Output = new OutputSettings { Directory = @"C:\SolidGround\Exports", BaseName = "terrain" },
    };

    private static TerrainRequestSettings MinimalProcess() => MinimalFetch() with
    {
        Mode = TerrainAcquisitionMode.Process,
        Process = new ProcessInputSettings { Asc = @"C:\SolidGround\terrain.asc" },
    };
}
