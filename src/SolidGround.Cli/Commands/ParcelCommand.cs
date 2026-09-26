using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SolidGround.Cli.Options;
using SolidGround.Core.Aois;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Sources.LocalParcelFile;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The `parcel` command: resolves a parcel boundary from a WGS 84 point or a geocoded address, against a
/// county registry or a local file, and prints deterministic JSON. Online unless <c>--offline</c> is given
/// together with <c>--point</c> and <c>--source local-file</c>. See docs/architecture/cli-workflow.md's
/// "### parcel" subsection and docs/architecture/census-county-lookup.md for the automatic-GEOID lookup this
/// command chains in ahead of a county-registry query.
/// </summary>
internal static class ParcelCommand
{
    private static readonly Regex GeoidPattern = new(@"\A\d{5}\z");

    internal static async Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        // ---- Step 1: parse and validate options; no I/O of any kind yet ----------------------------------
        bool hasPoint = invocation.HasOption("point");
        bool hasAddress = invocation.HasOption("address");
        if (hasPoint == hasAddress)
        {
            throw new CliUsageException("Exactly one of --point or --address is required.");
        }

        bool hasGeocodeProvider = invocation.HasOption("geocode-provider");
        bool hasGeocodeCandidate = invocation.HasOption("geocode-candidate");
        if ((hasGeocodeProvider || hasGeocodeCandidate) && !hasAddress)
        {
            throw new CliUsageException("--geocode-provider and --geocode-candidate only apply when --address is given.");
        }

        string sourceToken = invocation.GetValue("source")!;
        bool isCountyRegistry = sourceToken switch
        {
            "county-registry" => true,
            "local-file" => false,
            _ => throw new CliUsageException("--source must be one of: county-registry, local-file."),
        };

        if (invocation.HasOption("geoid") && !isCountyRegistry)
        {
            throw new CliUsageException("--geoid only applies when --source is county-registry.");
        }

        string? registryPath = invocation.GetValue("registry");
        if (isCountyRegistry && registryPath is null)
        {
            throw new CliUsageException("--registry is required when --source is county-registry.");
        }

        string? localFilePath = invocation.GetValue("local-file");
        string? localFileLabel = invocation.GetValue("local-file-label");
        string? localFileDisclaimer = invocation.GetValue("local-file-disclaimer");
        if (!isCountyRegistry && (localFilePath is null || localFileLabel is null || localFileDisclaimer is null))
        {
            throw new CliUsageException("--local-file, --local-file-label, and --local-file-disclaimer are all required when --source is local-file.");
        }

        string? geoidOverride = invocation.GetValue("geoid");
        if (geoidOverride is not null && !GeoidPattern.IsMatch(geoidOverride))
        {
            throw new CliUsageException("--geoid must be exactly 5 digits.");
        }

        bool offline = invocation.HasOption("offline");
        if (offline && !hasPoint)
        {
            throw new CliUsageException("--offline requires --point; resolving --address needs network access.");
        }

        if (offline && isCountyRegistry)
        {
            throw new CliUsageException("--offline requires --source local-file; the county registry always needs network access.");
        }

        int geocodeCandidateNumber = ParsePositiveInteger("geocode-candidate", invocation.GetValue("geocode-candidate"), defaultValue: 1);
        int selectNumber = ParsePositiveInteger("select", invocation.GetValue("select"), defaultValue: 1);

        string? outputDirectory = invocation.GetValue("output");
        string baseName = invocation.GetValue("name") ?? "parcel";
        string wktFileName = baseName + ".wkt";
        string aoiJsonFileName = baseName + ".aoi.json";
        if (outputDirectory is not null)
        {
            ProcessCommand.ValidateOutputDirectory(outputDirectory);
            ProcessCommand.ValidateBaseName(baseName);
            bool overwrite = invocation.HasOption("overwrite");
            string wktPath = Path.Combine(outputDirectory, wktFileName);
            string aoiJsonPath = Path.Combine(outputDirectory, aoiJsonFileName);
            if (!overwrite && (File.Exists(wktPath) || File.Exists(aoiJsonPath)))
            {
                throw new CliUsageException($"'{wktPath}' or '{aoiJsonPath}' already exists; pass --overwrite to replace it.");
            }
        }

        AddressGeocoderProvider geocodeProvider = GeocodeCommand.ParseProvider(invocation.GetValue("geocode-provider"), "geocode-provider");
        double bufferMeters = ParseBufferMeters(invocation.GetValue("buffer"));
        int timeoutSeconds = FetchCommand.ParseTimeout(invocation);

        // ---- Lazy, shared HttpClient: never constructed for a fully offline invocation -------------------
        HttpClient? httpClient = null;
        HttpClient EnsureHttpClient() => httpClient ??= new HttpClient(host.HttpMessageHandlerFactory(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        try
        {
            // ---- Step 2: resolve the query point ----------------------------------------------------------
            double latitude;
            double longitude;
            AddressGeocodeCandidate? geocodeCandidate = null;
            string? requestedAddress = null;
            if (hasAddress)
            {
                requestedAddress = invocation.GetValue("address")!;
                IAddressGeocoder geocoder = GeocodeCommand.BuildGeocoder(geocodeProvider, EnsureHttpClient(), host);
                AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(
                    new AddressGeocodeRequest(requestedAddress), cancellationToken).ConfigureAwait(false);
                if (geocodeCandidateNumber > acquisition.Candidates.Count)
                {
                    throw new CliUsageException(
                        $"--geocode-candidate {geocodeCandidateNumber.ToString(CultureInfo.InvariantCulture)} is out of range; " +
                        $"the geocoder returned {acquisition.Candidates.Count.ToString(CultureInfo.InvariantCulture)} candidate(s).");
                }

                geocodeCandidate = acquisition.Candidates[geocodeCandidateNumber - 1];
                latitude = geocodeCandidate.Latitude;
                longitude = geocodeCandidate.Longitude;
            }
            else
            {
                (latitude, longitude) = ParsePoint(invocation.GetValue("point")!);
            }

            // ---- Step 3: resolve the parcel source ------------------------------------------------------
            IParcelBoundarySource source;
            string? resolvedGeoid = null;
            string geoidOrigin = "explicit";
            if (isCountyRegistry)
            {
                CountyParcelRegistry registry = CountyParcelRegistry.Load(registryPath!);
                if (geoidOverride is not null)
                {
                    resolvedGeoid = geoidOverride;
                }
                else
                {
                    CensusCountyLookup countyLookup = new(EnsureHttpClient());
                    resolvedGeoid = await countyLookup.FindCountyGeoidAsync(latitude, longitude, cancellationToken).ConfigureAwait(false);
                    geoidOrigin = "censusLookup";
                }

                source = new CountyParcelRegistrySource(EnsureHttpClient(), registry, resolvedGeoid!);
            }
            else
            {
                source = new LocalParcelFileSource(new LocalParcelFileOptions
                {
                    Path = localFilePath!,
                    SourceLabel = localFileLabel!,
                    LicenseDisclaimerText = localFileDisclaimer!,
                });
            }

            // ---- Step 4: resolve the parcel -------------------------------------------------------------
            ParcelBoundaryAcquisition parcelAcquisition = await source.FindAsync(
                new ParcelPointQuery(latitude, longitude), cancellationToken).ConfigureAwait(false);

            // ---- Step 5: zero-candidate short-circuit (a CLI-decided outcome, not a Core exception) -----
            if (parcelAcquisition.Candidates.Count == 0)
            {
                host.StandardError.WriteLine("error (not-found): no parcel candidates were found for the resolved point.");
                return CliExitCodes.NotFound;
            }

            // ---- Step 6: range-check --select ------------------------------------------------------------
            if (selectNumber > parcelAcquisition.Candidates.Count)
            {
                throw new CliUsageException(
                    $"--select {selectNumber.ToString(CultureInfo.InvariantCulture)} is out of range; " +
                    $"{parcelAcquisition.Candidates.Count.ToString(CultureInfo.InvariantCulture)} candidate(s) were resolved.");
            }

            // ---- Step 7: write files, if requested -- always before Step 8 prints anything, so a printed
            // ---- "written" object never names a file that does not exist yet, and so a failure here (an
            // ---- unwritable/uncreatable output directory, or an I/O error mid-write) is reported as
            // ---- CliProcessingException before any stdout output at all, matching FetchCommand/
            // ---- ProcessCommand/RunCommand's own write-then-report convention. -------------------------------
            bool writesFiles = outputDirectory is not null;
            if (outputDirectory is not null)
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new CliProcessingException($"Could not create the parcel output directory '{outputDirectory}'.", ex);
                }

                ParcelBoundaryCandidate selected = parcelAcquisition.Candidates[selectNumber - 1];
                ParcelGeometryAoi aoi = ParcelBoundaryAoiFactory.FromCandidate(selected, LinearDistance.Meters(bufferMeters));
                UTF8Encoding noBom = new(encoderShouldEmitUTF8Identifier: false);
                string aoiJson = RenderAoiSettingsJson(wktFileName, bufferMeters);
                await WriteFileAsync(Path.Combine(outputDirectory, wktFileName), aoi.Geometry + "\n", noBom, cancellationToken).ConfigureAwait(false);
                await WriteFileAsync(Path.Combine(outputDirectory, aoiJsonFileName), aoiJson, noBom, cancellationToken).ConfigureAwait(false);
            }

            // ---- Step 8: print the JSON -- only once every file the "written" object names already exists ---
            string json = RenderParcelJson(
                hasAddress, latitude, longitude, requestedAddress, geocodeProvider, geocodeCandidate, geocodeCandidateNumber,
                isCountyRegistry, registryPath, resolvedGeoid, geoidOrigin, localFilePath, localFileLabel,
                parcelAcquisition, writesFiles ? selectNumber : null, writesFiles, wktFileName, aoiJsonFileName);
            host.StandardOutput.Write(json);

            return CliExitCodes.Success;
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    /// <exception cref="CliProcessingException">
    /// <paramref name="path"/> could not be written because of an I/O or access error. Mirrors
    /// <c>RasterSetIo.WriteFileAsync</c>'s identical pattern.
    /// </exception>
    private static async Task WriteFileAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(path, contents, encoding, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliProcessingException($"Could not write the parcel output file '{path}'.", ex);
        }
    }

    // ---- option parsing ---------------------------------------------------------------------------------

    /// <exception cref="CliUsageException"><paramref name="text"/> is not exactly two comma-separated finite numbers, or is not a valid WGS 84 latitude/longitude pair.</exception>
    private static (double Latitude, double Longitude) ParsePoint(string text)
    {
        string[] parts = text.Split(',');
        if (parts.Length != 2
            || !TryParseFiniteDouble(parts[0], out double latitude)
            || !TryParseFiniteDouble(parts[1], out double longitude))
        {
            throw new CliUsageException("--point must be exactly two comma-separated finite numbers: lat,lon.");
        }

        try
        {
            _ = new ParcelPointQuery(latitude, longitude);
        }
        catch (ArgumentException ex)
        {
            // Never ex.Message -- mirrors AoiSelection.BindRadius's identical reasoning: Core's own exception
            // embeds the offending coordinate verbatim.
            throw new CliUsageException("--point must be a valid WGS 84 latitude and longitude.", ex);
        }

        return (latitude, longitude);
    }

    private static bool TryParseFiniteDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static int ParsePositiveInteger(string optionName, string? text, int defaultValue)
    {
        if (text is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new CliUsageException($"--{optionName} must be a positive integer.");
        }

        return value;
    }

    private static double ParseBufferMeters(string? text)
    {
        if (text is null)
        {
            return 0d;
        }

        if (!TryParseFiniteDouble(text, out double value) || value < 0d)
        {
            throw new CliUsageException("--buffer must be a finite number greater than or equal to zero.");
        }

        return value;
    }

    // ---- JSON rendering -----------------------------------------------------------------------------------

    private static string SourceKindToken(ParcelBoundarySourceKind kind) => kind switch
    {
        ParcelBoundarySourceKind.CountyRegistry => "countyRegistry",
        ParcelBoundarySourceKind.LocalParcelFile => "localParcelFile",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported parcel boundary source kind."),
    };

    private static string RenderParcelJson(
        bool inputIsAddress, double latitude, double longitude, string? requestedAddress,
        AddressGeocoderProvider geocodeProvider, AddressGeocodeCandidate? geocodeCandidate, int geocodeCandidateNumber,
        bool isCountyRegistry, string? registryPath, string? resolvedGeoid, string geoidOrigin,
        string? localFilePath, string? localFileLabel,
        ParcelBoundaryAcquisition acquisition, int? selectedCandidateIndex, bool writesFiles, string wktFileName, string aoiJsonFileName) =>
        CliJsonOutput.Render(writer =>
        {
            writer.WriteStartObject();

            writer.WritePropertyName("input");
            writer.WriteStartObject();
            if (inputIsAddress)
            {
                // Always non-null here: inputIsAddress is set from the same hasAddress flag RunAsync used to
                // assign requestedAddress in the first place.
                writer.WriteString("kind", "address");
                writer.WriteString("address", requestedAddress!);
            }
            else
            {
                writer.WriteString("kind", "point");
                writer.WriteNumber("latitude", latitude);
                writer.WriteNumber("longitude", longitude);
            }

            writer.WriteEndObject();

            writer.WritePropertyName("geocodeInput");
            if (geocodeCandidate is { } candidate)
            {
                writer.WriteStartObject();
                writer.WriteString("provider", GeocodeCommand.ProviderName(geocodeProvider));
                writer.WriteNumber("candidateIndex", geocodeCandidateNumber);
                writer.WriteString("matchedAddress", candidate.MatchedAddress);
                writer.WriteNumber("latitude", candidate.Latitude);
                writer.WriteNumber("longitude", candidate.Longitude);
                writer.WriteString("accuracyLabel", GeocodeCommand.NotSurveyGradeDisclaimer);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteString("sourceKind", isCountyRegistry ? "countyRegistry" : "localParcelFile");

            writer.WritePropertyName("sourceDetail");
            writer.WriteStartObject();
            if (isCountyRegistry)
            {
                // registryPath/resolvedGeoid are always non-null here: Step 1 requires --registry when
                // --source is county-registry, and Step 3 always sets resolvedGeoid (from --geoid or from the
                // Census county lookup) before a CountyParcelRegistrySource is ever constructed.
                writer.WriteString("registryPath", registryPath!);
                writer.WriteString("geoid", resolvedGeoid!);
                writer.WriteString("geoidOrigin", geoidOrigin);
            }
            else
            {
                // localFilePath/localFileLabel are always non-null here: Step 1 requires both when --source
                // is local-file.
                writer.WriteString("path", localFilePath!);
                writer.WriteString("label", localFileLabel!);
            }

            writer.WriteEndObject();

            writer.WriteNumber("candidateCount", acquisition.Candidates.Count);
            writer.WriteBoolean("resultSetTruncated", acquisition.ResultSetTruncated);

            writer.WriteStartArray("candidates");
            for (int i = 0; i < acquisition.Candidates.Count; i++)
            {
                WriteCandidate(writer, acquisition.Candidates[i], i + 1);
            }

            writer.WriteEndArray();

            if (selectedCandidateIndex is { } index)
            {
                writer.WriteNumber("selectedCandidateIndex", index);
            }
            else
            {
                writer.WriteNull("selectedCandidateIndex");
            }

            writer.WritePropertyName("written");
            if (writesFiles)
            {
                writer.WriteStartObject();
                writer.WriteString("wkt", wktFileName);
                writer.WriteString("aoiJson", aoiJsonFileName);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteEndObject();
        });

    private static void WriteCandidate(Utf8JsonWriter writer, ParcelBoundaryCandidate candidate, int index)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", index);
        writer.WriteString("parcelId", candidate.ParcelId);
        WriteNullableString(writer, "situsAddress", candidate.SitusAddress);
        writer.WriteNumber("computedAreaSquareMeters", candidate.ComputedAreaSquareMeters);
        writer.WriteString("sourceKind", SourceKindToken(candidate.SourceKind));
        writer.WriteString("sourceIdentity", candidate.SourceIdentity);
        writer.WriteString("licenseDisclaimerText", candidate.LicenseDisclaimerText);
        writer.WriteString("accuracyLabel", candidate.AccuracyLabel);
        WriteNullableString(writer, "subdivision", candidate.Subdivision);
        WriteNullableString(writer, "lot", candidate.Lot);
        WriteNullableString(writer, "block", candidate.Block);
        WriteNullableString(writer, "plat", candidate.Plat);
        WriteNullableString(writer, "book", candidate.Book);
        WriteNullableString(writer, "page", candidate.Page);
        writer.WriteBoolean("bookPageAreUnconfirmedProxies", candidate.BookPageAreUnconfirmedProxies);
        WriteNullableString(writer, "legalDescription", candidate.LegalDescription);
        if (candidate.ReportedAcres is { } reportedAcres)
        {
            writer.WriteNumber("reportedAcres", reportedAcres);
        }
        else
        {
            writer.WriteNull("reportedAcres");
        }

        WriteNullableString(writer, "zoning", candidate.Zoning);
        WriteNullableString(writer, "stableParcelId", candidate.StableParcelId);
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    /// <summary>
    /// Renders <c>&lt;name&gt;.aoi.json</c>: the <c>areaOfInterest</c> fragment matching
    /// <c>TerrainRequestSettingsTests.cs</c>'s own pinned <c>areaOfInterest.parcel</c> template shape
    /// (<c>path</c>/<c>format</c>/<c>bufferMeters</c>), directly readable with <c>AoiSettingsFactory.Build</c>
    /// once the sibling <c>.wkt</c> file <paramref name="wktFileName"/> names is read into memory by the
    /// caller. <paramref name="wktFileName"/> is written as a path relative to <c>--output</c>'s own directory
    /// (never absolute) purely so two runs into two different output directories still produce
    /// byte-identical <c>.aoi.json</c> bytes (AC1/AC3's own determinism goal) -- see
    /// docs/architecture/cli-workflow.md's "### parcel" subsection. This relative form is not ready to paste
    /// as-is into the real <c>%ProgramData%\SolidGround\Revit\settings.json</c>: that document's loader
    /// (<c>SolidGround.Revit.Commands.CreateToposolidCommand</c>) resolves a relative
    /// <c>AreaOfInterest.Parcel.Path</c> against its own process's current working directory, never the
    /// settings file's own directory, so <c>path</c> must first be rewritten to an absolute location --
    /// matching that document's own established convention for its sibling <c>process.asc</c> field -- before
    /// this fragment is pasted into it.
    /// </summary>
    private static string RenderAoiSettingsJson(string wktFileName, double bufferMeters) =>
        CliJsonOutput.Render(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "parcel");
            writer.WritePropertyName("parcel");
            writer.WriteStartObject();
            writer.WriteString("path", wktFileName);
            writer.WriteString("format", "wkt");
            writer.WriteNumber("bufferMeters", bufferMeters);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
}
