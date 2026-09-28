using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

public sealed class FixtureSecurityTests
{
    [Fact]
    public void CommittedFixturesContainNoCredentialsOrRequestUrls()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string[] forbiddenMarkers =
        [
            "authorization:",
            "bearer ",
            "api_key",
            "apikey",
            "http://",
            "https://",
        ];

        foreach (string path in Directory.EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);
            foreach (string marker in forbiddenMarkers)
            {
                Assert.DoesNotContain(marker, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Guards the golden-fixture landmine at each attribution constant's own definition site, not only at
    /// fixture-commit time: a literal <c>http://</c>/<c>https://</c> substring baked into a shipped attribution
    /// constant would eventually reach a committed golden export document and trip
    /// <see cref="CommittedFixturesContainNoCredentialsOrRequestUrls"/> above. See
    /// docs/architecture/source-licensing-and-attribution.md's "OpenTopography USGS 1 m attribution" section
    /// for why <see cref="OpenTopographyUsgs1mSource.AttributionNotice"/> is deliberately written with no
    /// literal URL.
    /// </summary>
    [Fact]
    public void AttributionConstantsContainNoRequestUrls()
    {
        string[] forbiddenMarkers = ["http://", "https://"];
        string[] attributionConstants =
        [
            OpenTopographyUsgs1mSource.AttributionNotice,
            CensusGeocoder.AttributionNotice,
            EsriGeocoder.AttributionNotice,
        ];

        foreach (string attribution in attributionConstants)
        {
            foreach (string marker in forbiddenMarkers)
            {
                Assert.DoesNotContain(marker, attribution, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Proves AC2's "no public fixture or default path contains their data" going forward, not just today: a
    /// committed fixture under <c>Fixtures/</c> must never carry any never-default vendor's own domain or
    /// identifier. See docs/architecture/source-licensing-and-attribution.md's "Never-default sources"
    /// section for why each of these vendors is excluded as a default. "corelogic" is listed separately from
    /// "cotality" because Cotality's own Evaluation Terms use "CoreLogic" as the operative legal name in the
    /// derivative-ownership clause, so a fixture carrying that name but not the brand name would otherwise
    /// pass this scan undetected.
    /// </summary>
    [Fact]
    public void CommittedFixturesContainNoNeverDefaultVendorDataOrIdentifiers()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string[] neverDefaultMarkers =
        [
            "nominatim",
            "attomdata.com",
            "lightboxre.com",
            "cotality",
            "corelogic",
            "regrid.com",
            "a2050b09baff493aa4ad7848ba2fac00",
        ];

        foreach (string path in Directory.EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);
            foreach (string marker in neverDefaultMarkers)
            {
                Assert.DoesNotContain(marker, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
