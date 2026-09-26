using System.Text;
using SolidGround.Cli.Options;
using SolidGround.Cli.Secrets;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.Geocodio;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The online `geocode` command: resolves a street address to ranked, approximate WGS 84 coordinate
/// candidates and prints deterministic JSON. See docs/architecture/cli-workflow.md's "### geocode" subsection.
/// <see cref="BuildGeocoder"/> and <see cref="NotSurveyGradeDisclaimer"/> are <see langword="internal"/> so
/// <see cref="ParcelCommand"/> can reuse them directly (zero duplicated provider-selection or wording logic).
/// </summary>
internal static class GeocodeCommand
{
    /// <summary>A CLI-owned constant, not a Core one: unlike <c>ParcelBoundaryCandidate.NotASurveyDisclaimer</c>, Core defines no analogous constant for a geocode candidate.</summary>
    internal const string NotSurveyGradeDisclaimer = "This coordinate is an approximate geocoding result, not a survey-grade measurement.";

    internal static async Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        string address = invocation.GetValue("address")!;
        AddressGeocoderProvider provider = ParseProvider(invocation.GetValue("provider"), "provider");
        string? outputFile = invocation.GetValue("output-file");
        int timeoutSeconds = FetchCommand.ParseTimeout(invocation);

        using HttpClient httpClient = new(host.HttpMessageHandlerFactory(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        IAddressGeocoder geocoder = BuildGeocoder(provider, httpClient, host);

        AddressGeocodeAcquisition acquisition = await geocoder.GeocodeAsync(
            new AddressGeocodeRequest(address), cancellationToken).ConfigureAwait(false);
        // A zero-candidate result never reaches here: IAddressGeocoder.GeocodeAsync always throws its own
        // provider's ...NoCandidatesException instead -- see CliApplication.RunAsync's catch chain.

        string json = RenderGeocodeJson(provider, address, acquisition);
        host.StandardOutput.Write(json);
        if (outputFile is not null)
        {
            try
            {
                await File.WriteAllTextAsync(outputFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Mirrors RasterSetIo.WriteFileAsync/ParcelCommand.WriteFileAsync's identical pattern. Unlike
                // ParcelCommand's own "written" JSON object, this command's JSON never mentions --output-file
                // at all, so stdout above makes no claim about this file either way; only the exit code and
                // stderr message need to reflect the failure.
                throw new CliProcessingException($"Could not write the geocode output file '{outputFile}'.", ex);
            }
        }

        return CliExitCodes.Success;
    }

    /// <param name="token">The raw option value, or null when the option was omitted.</param>
    /// <param name="optionName">
    /// The calling command's own flag name (without its leading <c>--</c>), interpolated into the thrown
    /// message so an invalid value reports the flag the operator actually typed -- <c>geocode</c> passes
    /// <c>"provider"</c>, <see cref="ParcelCommand"/> passes <c>"geocode-provider"</c>; the two verbs name
    /// this choice differently, and this method must never hardcode either one.
    /// </param>
    internal static AddressGeocoderProvider ParseProvider(string? token, string optionName) => token switch
    {
        null or "census" => AddressGeocoderProvider.Census,
        "geocodio" => AddressGeocoderProvider.Geocodio,
        "esri" => AddressGeocoderProvider.Esri,
        _ => throw new CliUsageException($"--{optionName} must be one of: census, geocodio, esri."),
    };

    /// <summary>
    /// Geocodio/Esri are constructed directly, never through <see cref="AddressGeocoderFactory.Create"/>:
    /// that factory's own Geocodio/Esri branches hardcode <c>EnvironmentGeocodioApiKeyProvider</c>/
    /// <c>EnvironmentEsriApiKeyProvider</c>, which call the real <see cref="Environment.GetEnvironmentVariable(string)"/>
    /// directly -- unusable from a CLI test that must never touch the real process environment. The factory is
    /// still reused, unmodified, for the default Census branch (keyless, no such gap).
    /// </summary>
    internal static IAddressGeocoder BuildGeocoder(AddressGeocoderProvider provider, HttpClient httpClient, CliHost host) => provider switch
    {
        AddressGeocoderProvider.Census => AddressGeocoderFactory.Create(new AddressGeocoderSettings { Provider = provider }, httpClient),
        AddressGeocoderProvider.Geocodio => new GeocodioGeocoder(httpClient, new CliGeocodioApiKeyProvider(host.GetEnvironmentVariable)),
        AddressGeocoderProvider.Esri => new EsriGeocoder(httpClient, new CliEsriApiKeyProvider(host.GetEnvironmentVariable)),
        _ => throw new CliUsageException("--provider must be one of: census, geocodio, esri."),
    };

    internal static string RenderGeocodeJson(AddressGeocoderProvider provider, string address, AddressGeocodeAcquisition acquisition) =>
        CliJsonOutput.Render(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("provider", ProviderName(provider));
            writer.WriteString("requestedAddress", address);
            writer.WriteNumber("candidateCount", acquisition.Candidates.Count);
            writer.WriteStartArray("candidates");
            for (int i = 0; i < acquisition.Candidates.Count; i++)
            {
                AddressGeocodeCandidate candidate = acquisition.Candidates[i];
                writer.WriteStartObject();
                writer.WriteNumber("index", i + 1);
                writer.WriteNumber("latitude", candidate.Latitude);
                writer.WriteNumber("longitude", candidate.Longitude);
                writer.WriteString("matchedAddress", candidate.MatchedAddress);
                if (candidate.PrecisionLabel is { } label)
                {
                    writer.WriteString("precisionLabel", label);
                }
                else
                {
                    writer.WriteNull("precisionLabel");
                }

                if (candidate.Score is { } score)
                {
                    writer.WriteNumber("score", score);
                }
                else
                {
                    writer.WriteNull("score");
                }

                writer.WriteString("attribution", candidate.Attribution);
                writer.WriteString("accuracyLabel", NotSurveyGradeDisclaimer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    internal static string ProviderName(AddressGeocoderProvider provider) => provider switch
    {
        AddressGeocoderProvider.Census => CensusGeocoder.ProviderName,
        AddressGeocoderProvider.Geocodio => GeocodioGeocoder.ProviderName,
        AddressGeocoderProvider.Esri => EsriGeocoder.ProviderName,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported address geocoder provider."),
    };
}
