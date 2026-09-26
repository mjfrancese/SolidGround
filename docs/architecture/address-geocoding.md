# Address geocoding

Issue #28 (PH3-1) adds a pluggable `IAddressGeocoder` abstraction to `SolidGround.Core` -- sibling to
`IElevationSource` -- with three implementations: the US Census Bureau Geocoder (default, keyless), Geocodio
(`GEOCODIO_API_KEY`, keyed opt-in), and Esri's World Geocoding Service with `forStorage=true`
(`ARCGIS_API_KEY`, keyed opt-in). It adds no package reference: `HttpClient` and `System.Text.Json` are both
already BCL-available to Core, and every key-handling and redaction concern is reused from Issue #27's shared
`SolidGround.Core.Http` component (`docs/architecture/shared-http-redaction-and-key-resolution.md`), whose own
"How a future source calls in" section named this issue as one of its first callers.

## Purpose and boundary

This issue turns a street address into ranked, approximate WGS 84 coordinate candidates -- mechanism, not
policy. `IAddressGeocoder` is the contract; `AddressGeocoderSettings`/`AddressGeocoderProvider` is the
settings value that selects an implementation; `AddressGeocoderFactory` builds the selected one. It
deliberately does not wire that selection into `TerrainRequestSettings`/`RevitSettingsIo`'s shipped JSON
template, or into `SolidGround.Cli`: that wiring belongs to a future dialog or CLI issue that consumes
`AddressGeocoderFactory` once it exists. As a direct consequence, this issue touches zero existing test
files -- `TerrainRequestSettingsTests.JsonOptionsDecodesTheShippedTemplateTextVerbatim` (which pins the
template's exact bytes) needed no edit.

## Provider comparison

| | Census (default) | Geocodio (opt-in) | Esri (opt-in) |
| --- | --- | --- | --- |
| Endpoint | `geocoding.geo.census.gov/geocoder/locations/onelineaddress` | `api.geocod.io/v2/geocode` | `geocode-api.arcgis.com/.../World/GeocodeServer/findAddressCandidates` |
| Key | None | `GEOCODIO_API_KEY` | `ARCGIS_API_KEY` |
| Key transport | N/A | `Authorization: Bearer` header | `Authorization: Bearer` header |
| Ranking signal | None (array order only) | `accuracy`, 0.0-1.0, server pre-sorted | `score`, 1-100, sorted by this provider (not guaranteed pre-sorted) |
| Precision label | None | `accuracy_type` | `attributes.Addr_type` |
| Attribution | Fixed Census ToS notice | Wire `source` field, verbatim | Fixed "Powered by Esri" notice |
| Zero-result shape | `addressMatches: []` (HTTP 200) | `results: []` (inferred; 422 more likely for "no good match") | `candidates: []`, no `error` (HTTP 200) |
| Cost/terms | Free, federal public domain | Paid; US forward-geocoding results storable per Geocodio's ToS | Paid under `forStorage=true` (no free "stored" tier) |

Because Geocodio and Esri transport their key only in a header, `BuildRequestUri` on both never takes a key
parameter at all -- unlike a query-string-transported key (OpenTopography's `API_Key`), there is no "build the
URI with a null key" branch to get right, because the key is structurally never part of the request URI.

## Core types and files

| Type | File | Namespace |
| --- | --- | --- |
| `IAddressGeocoder` | `Sources/IAddressGeocoder.cs` | `SolidGround.Core.Sources` |
| `AddressGeocodeRequest`, `AddressGeocodeCandidate`, `AddressGeocodeAcquisition` | `Sources/AddressGeocodeAcquisition.cs` | `SolidGround.Core.Sources` |
| `AddressGeocoderException` (shared abstract base) | `Sources/AddressGeocoderException.cs` | `SolidGround.Core.Sources` |
| `AddressGeocoderProvider`, `AddressGeocoderSettings` | `Sources/AddressGeocoderSettings.cs` | `SolidGround.Core.Sources` |
| `AddressGeocoderFactory` | `Sources/AddressGeocoderFactory.cs` | `SolidGround.Core.Sources` |
| `CensusGeocoder`, `CensusGeocoderOptions`, `CensusGeocoderException`'s five leaf types | `Sources/Census/*.cs` | `SolidGround.Core.Sources.Census` |
| `GeocodioGeocoder`, `GeocodioGeocoderOptions`, `IGeocodioApiKeyProvider`/`Environment...`/`Static...`, `GeocodioGeocoderException`'s seven leaf types | `Sources/Geocodio/*.cs` | `SolidGround.Core.Sources.Geocodio` |
| `EsriGeocoder`, `EsriGeocoderOptions`, `IEsriApiKeyProvider`/`Environment...`/`Static...`, `EsriGeocoderException`'s six leaf types | `Sources/Esri/*.cs` | `SolidGround.Core.Sources.Esri` |

`AddressGeocodeCandidate` carries `Latitude`, `Longitude`, `MatchedAddress`, `Attribution` (never blank), and
nullable `PrecisionLabel`/`Score`. `AddressGeocodeAcquisition` is a non-empty, best-match-first list: there is
no successful empty result -- a provider that found nothing throws its own `...NoCandidatesException` instead
(AC5). Ranking is conveyed purely by list order; no candidate carries a separate rank field.

### Exception hierarchy: one shared base, not three independent ones

Unlike `OpenTopographyException` (one self-contained base, because `OpenTopographyUsgs1mSource` is so far the
*only* `IElevationSource` implementation), every geocoder exception derives from one shared abstract
`AddressGeocoderException`, exposing `ProviderName` ("Census"/"Geocodio"/"Esri") and `RedactedRequestUri`.
`IAddressGeocoder` is SolidGround's first interface with several interchangeable implementations selected at
runtime by a settings value; a future caller (a dialog or CLI command) holds an `IAddressGeocoder`-typed
reference without statically knowing which provider is active, and needs one catchable type that means
"geocoding failed, show the message" while still being able to pattern-match a concrete subtype (for example
`catch (GeocodioGeocoderQuotaException ex)`) when it cares. Each provider still keeps its own fully
independent set of `sealed` leaf types and its own failure-reason enum where warranted -- Census simply has no
`Authorization`-category exception at all, since it has no key to reject.

## Settings and the validation boundary

`AddressGeocoderSettings.Provider` defaults to `AddressGeocoderProvider.Census`. Neither this type nor any
future `Validate()`-style method on it may check whether a keyed provider's environment variable is set --
that check belongs entirely to `GeocodioApiKeyProvider`/`EsriApiKeyProvider` at `GeocodeAsync` call time,
exactly mirroring how `OpenTopographyUsgs1mSource` (not `TerrainRequestSettings`) owns its own missing-key
check. `AddressGeocoderFactory.Create` never touches the environment or a user-secrets file; the key is
resolved lazily, only when `GeocodeAsync` is actually called (AC2's "before any HTTP call" requirement).

## Redaction and key-resolution reuse (AC3)

`EnvironmentGeocodioApiKeyProvider`/`EnvironmentEsriApiKeyProvider` are the first real callers of
`SolidGround.Core.Http.ApiKeyResolver.TryResolve` outside OpenTopography's own already-migrated provider.
Both wrap the resolved value directly in the shared `SolidGround.Core.Http.ApiKey` -- no `GeocodioApiKey`/
`EsriApiKey` wrapper type is introduced, per `ApiKey`'s own doc comment describing exactly this
future-provider case.

All three providers compute `RedactedRequestUri` via `SensitiveQueryRedactor.RedactUri`, and every
server-derived message string via `SensitiveQueryRedactor.RedactText`:

| Provider | Names passed to `RedactUri`/`RedactText` | Why |
| --- | --- | --- |
| Esri | `SensitiveQueryParameterNames.Esri` (`["token"]`) | Already exists, anticipating exactly this provider; defensive -- this design never actually puts `token=` in a URI (the key is header-only), but the preset guards a future query-parameter fallback. |
| Census, Geocodio | `SensitiveQueryParameterNames.KnownFamilies` (`["API_Key", "token"]`) | Neither has a query-string key parameter of its own (Census has no key at all; Geocodio's travels only in a header). `SensitiveQueryRedactor.RequireNames` throws on an empty collection, so some non-empty preset must be passed even when nothing is expected to match. No new `SensitiveQueryParameterNames.Geocodio` preset is added -- nothing to redact from a query string exists to name; add one only if a query-parameter fallback is ever implemented. |

Every server-derived message is redacted on the *raw* response body text first (the same order
`OpenTopographyUsgs1mSource.BuildServerMessage` already uses), before any attempt to parse it as structured
JSON for a cleaner display -- so a key echoed anywhere in the body can never survive even if that
structure-aware parse then fails for an unrelated reason. Geocodio's and Esri's `SendGetAsync` also mirror
`OpenTopographyUsgs1mSource.SendGetAsync`'s own established fix for a real key-leak path: every
`OperationCanceledException`/`HttpRequestException`/`IOException` catch redacts `ex.Message` and rebuilds a
*new* exception of the same concrete type from the redacted text before attaching it as `InnerException` --
never the original caught exception. This matters because a caller-attached `DelegatingHandler` could
realistically throw an exception whose own `Message` embeds the `Authorization` header value, and
`Exception.ToString()` recurses into `InnerException`, so redacting only the outer `Message` would not be
enough. `GeocodioGeocoderTests.WrapsATransportFailureAsANetworkExceptionWithoutLeakingTheKeyThroughTheInnerExceptionChain`
and its Esri equivalent are the regression tests, walking the full `InnerException` chain calling
`.ToString()` on every link -- mirroring `OpenTopographyUsgs1mSourceTests.AssertExceptionChainDoesNotContainTheKey`.

No new shared `Core.Http` "send a Bearer-authenticated GET" helper was added: each geocoder owns its own
private `SendGetAsync`/response handling, accepting the modest duplication between Geocodio and Esri's
near-identical shape. `OpenTopographyUsgs1mSource` is itself the only example of this shape in the codebase
today; introducing a shared helper for two call sites is unrequested scope (YAGNI -- revisit once a third
header-authenticated source exists).

## Attribution handling per provider

The one place the three providers genuinely diverge in shape:

- **Census** has no per-candidate or per-response attribution field at all. `CensusGeocoder.AttributionNotice`
  is a hardcoded constant, the Census Bureau Data API Terms of Service's own required notice, verbatim:
  source https://www.census.gov/data/developers/about/terms-of-service.html (retrieved 2026-09-25). Whether
  this general census.gov terms page contractually binds the separate `geocoding.geo.census.gov` host is
  unresolved by any primary source found; the notice is shown anyway, out of caution.
- **Geocodio**'s `source` field is the one genuinely wire-sourced attribution: real values include
  license-mandated text for non-US sources and a plain source name for US results. `GeocodioGeocoder` never
  substitutes a hardcoded constant; a response missing `source` fails as
  `GeocodioGeocoderUnexpectedResponseException` rather than silently supplying one.
- **Esri**'s response schema carries no attribution field. `EsriGeocoder.AttributionNotice` is a hardcoded
  constant, "Powered by Esri" -- source
  https://developers.arcgis.com/documentation/esri-and-data-attribution/no-map/ (retrieved 2026-09-25), whose
  own "no map" examples show exactly this phrase.

## Ranking and precision semantics

`Score` and `PrecisionLabel` are nullable because Census's schema has neither. The two keyed providers' scales
are not comparable: Geocodio's `accuracy` is 0.0-1.0; Esri's `score` is 1-100. Each provider's own ranking
guarantee is handled differently: Geocodio's docs explicitly guarantee `results[]` is always pre-sorted
most-accurate-first ("it is therefore always safe to pick the first result in the list"), so `GeocodioGeocoder`
preserves wire order exactly with no re-sort. Esri's docs never make the equivalent guarantee, so
`EsriGeocoder` sorts by `Score` descending (a stable sort) before returning -- `esri-example-site-synthetic.json`
deliberately lists its two candidates out of score order (86.4 before 100) so a bug that trusted wire order
would fail `EsriGeocoderTests.ParsesCandidatesSortingByScoreDescendingRegardlessOfServerOrder`. Census has no
ranking signal at all; "ranked candidates" is satisfied only by preserving array order, never fabricated.

## Why no county-proxy or public-Nominatim default

SolidGround defaults to the keyless US Census Bureau Geocoder and offers Geocodio and Esri's `forStorage=true`
path as keyed opt-ins. Two other options were deliberately rejected as defaults: a county government's own
Esri-hosted geocode proxy (a shared-credit locator with no published terms, where an anonymous call risks
spending that county's own paid credits) and the public OpenStreetMap Nominatim instance (whose own usage
policy, and the OSM Foundation's community geocoding guideline, both require a disclosed, deliberate
integration choice rather than a silent default). See the
[Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/) and the
[OSMF Geocoding Guideline (2017)](https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline)
for Nominatim's own terms, and `docs/architecture/phase-3-interactive-add-in-research.md`'s Goal (a) and owner
decision 1 for the full comparison this decision is based on. Neither is implemented here.

## Fixture plan and guard constraints

All fixtures live flat under `tests/SolidGround.Tests/Fixtures/`, named with the repository's `*-synthetic*`
convention: `census-example-site-synthetic.json`, `census-multi-candidate-synthetic.json`,
`geocodio-example-site-synthetic.json`, `esri-example-site-synthetic.json`. Every HTTP-error-status body is an
inline string literal inside the test method instead, mirroring `OpenTopographyUsgs1mSourceTests.cs`'s own
convention.

AGENTS.md forbids recording any street address, lot, plat, ZIP, or place name for the example site; the
Census example-site fixture instead uses a synthetic, non-address-shaped input string ("100 Example Loop")
and lands its one candidate exactly on the site's already-public coordinate (`41.591194, -93.603806`) -- it
never claims that string is the site's real address. Every fixture avoids the repository's own
`PersonalInformationGuardTests`/`FixtureSecurityTests` detectors deliberately:

- **Street-address shape**: every fixture uses "Loop" for its street suffix, which is not in the detector's
  recognized-suffix list. Earlier scratch drafts of the Geocodio and Esri fixtures used "Synthetic Way" and
  "Fixture Pkwy" respectively -- both real, recognized suffixes ("Way", "Pkwy") -- and were corrected to
  "Example Loop" before being committed.
- **State/ZIP shape**: fixtures use non-real two-letter codes (`ZZ`, `YY`, `XX`) that never match the
  detector's fixed list of 51 real state names/abbreviations, paired with `00000` ZIPs.
- **Coordinate-pair shape**: the Census example-site fixture's one coordinate is the public site coordinate
  itself (allowed by the guard's own tolerance carve-out). The Census multi-candidate fixture's other two
  entries use values with either too few fractional digits to match the detector's regex at all, or
  magnitudes outside its CONUS-like latitude/longitude ranges. The Geocodio and Esri fixtures use small
  values near `(0, 0)` for the same reason -- four fractional digits, but far outside both ranges.
- **Fixture-security markers**: no fixture contains `http://`, `https://`, `authorization:`, `bearer `,
  `api_key`, or `apikey` in any casing.

The Esri fixture also narrows every candidate's `attributes` object to exactly `{"Addr_type": "<value>"}`:
`EsriGeocoder.BuildRequestUri` sends `outFields=Addr_type` and nothing else, and Esri's own docs state
`attributes` is blank except for the field(s) actually named in `outFields` -- a real response to this exact
request never carries any of the ~50 other attribute keys a naive full-fixture sample might suggest.

## Test evidence

Ten new test files, zero existing test files modified:

| File | Covers |
| --- | --- |
| `CensusGeocoderTests.cs` | Request shape, no `Authorization` header, example-site and multi-candidate fixture parsing, zero-candidates, HTTP 400/5xx/malformed-200/transport-failure classification, and delegation (not reimplementation) of redaction. |
| `GeocodioGeocoderTests.cs` | Bearer-header request shape, fixture parsing (order/score/precision/attribution), missing-key-before-any-request, HTTP 403/422/429 (rate-limit headers)/500/malformed-200, key never in the query string, key redacted even when echoed in a server body, and the inner-exception-chain leak regression. |
| `GeocodioApiKeyProviderTests.cs` | `GeocodioEnvironmentCollectionDefinition` plus `Environment...`/`Static...` provider behavior against the real process environment. |
| `EsriGeocoderTests.cs` | `forStorage=true` request shape, score-descending sort against an out-of-order fixture, missing-key-before-any-request, an `error` object on an outer HTTP 200, error codes 400/403/499/500, malformed-200, key never in the query string, key redaction, and the inner-exception-chain leak regression. |
| `EsriApiKeyProviderTests.cs` | `EsriEnvironmentCollectionDefinition` plus `Environment...`/`Static...` provider behavior. |
| `AddressGeocoderFactoryTests.cs` | Default provider, provider-to-concrete-type mapping, and the undefined-provider guard. |
| `AddressGeocodeAcquisitionTests.cs` | `AddressGeocodeRequest`/`AddressGeocodeCandidate`/`AddressGeocodeAcquisition` constructor validation, especially the zero-candidate ban (AC5's core invariant, tested directly). |
| `CensusGeocoderLiveTests.cs`, `GeocodioGeocoderLiveTests.cs`, `EsriGeocoderLiveTests.cs` | Opt-in, flag- (and key-, for the two keyed providers) gated live calls; see below. |

## Live-test instructions

Each live test is skipped, never failed, unless its own environment variables are all set:

| Class | Flag | Key | Address |
| --- | --- | --- | --- |
| `CensusGeocoderLiveTests` | `SOLIDGROUND_CENSUS_LIVE=1` | (none) | `SOLIDGROUND_GEOCODER_LIVE_ADDRESS` |
| `GeocodioGeocoderLiveTests` | `SOLIDGROUND_GEOCODIO_LIVE=1` | `GEOCODIO_API_KEY` | `SOLIDGROUND_GEOCODER_LIVE_ADDRESS` |
| `EsriGeocoderLiveTests` | `SOLIDGROUND_ARCGIS_LIVE=1` | `ARCGIS_API_KEY` | `SOLIDGROUND_GEOCODER_LIVE_ADDRESS` |

One shared `SOLIDGROUND_GEOCODER_LIVE_ADDRESS` variable serves all three -- each test still needs its own flag,
so enabling one provider's live test never silently enables another's. Set the flag, the address (a real,
developer-supplied address; never committed), and a key for a keyed provider, then run the test project
normally. CI never sets any of these.

## Accuracy caveat

Every candidate this issue returns is approximate, never survey-grade (AGENTS.md "Accuracy and product
claims"): TIGER-line-interpolated for Census, industry-standard rooftop/interpolated tiers for Geocodio and
Esri. A geocoded point is a starting point for the operator to confirm, exactly the same posture SolidGround
already takes toward a fetched DEM -- "site-form tool, not a survey instrument."

## Open questions

1. Census has no ranking signal at all; `Score`/`PrecisionLabel` stay nullable rather than fabricated.
2. Whether the general census.gov API Terms of Service contractually binds the separate
   `geocoding.geo.census.gov` host is unresolved by any primary source -- the hardcoded notice is shown
   anyway, out of caution.
3. Geocodio's exact 403 message text for a bare missing/invalid key, and its exact default-format
   zero-result body (`{"results": []}`), are inferred, not verbatim-confirmed -- the contract tests assert
   status and envelope shape only, never exact message text, for these two cases.
4. Whether an Esri API key is used directly as the raw `Authorization: Bearer` value with no separate OAuth
   exchange is inferred from "an API key is a long-lived access token" language, not an explicit
   "paste the raw key into the header" sentence -- worth a one-time confirmation against Esri's own Get
   Started tutorial before a real key is ever used.
5. Whether `findAddressCandidates`'s outer HTTP transport status ever varies from 200, or Esri always answers
   `200 OK` with the real status only in `error.code`, is not confirmed by any Esri primary source for this
   exact operation -- `EsriGeocoder` parses defensively for both possibilities, so this is a documentation
   gap, not an implementation risk either way.
6. This issue's fixture and request both scope `outFields` to `Addr_type` alone, so `attributes`' behavior
   for a wider `outFields` list is untested here. Confirm the field subset again before any future issue
   widens `EsriGeocoderOptions`/`BuildRequestUri` beyond `Addr_type`.

## Citations

- [Census Geocoder API documentation](https://geocoding.geo.census.gov/geocoder/Geocoding_Services_API.html) (retrieved 2026-09-25)
- [Census Bureau Data API Terms of Service](https://www.census.gov/data/developers/about/terms-of-service.html) (retrieved 2026-09-25)
- [Geocodio API documentation](https://www.geocod.io/docs/) (retrieved 2026-09-25)
- [Geocodio Terms of Use](https://www.geocod.io/terms-of-use/) (retrieved 2026-09-25)
- [Esri World Geocoding Service: find address candidates](https://developers.arcgis.com/rest/geocode/find-address-candidates/) (retrieved 2026-09-25)
- [Esri API key authentication](https://developers.arcgis.com/documentation/security-and-authentication/api-key-authentication/) (retrieved 2026-09-25)
- [Esri data attribution ("no map")](https://developers.arcgis.com/documentation/esri-and-data-attribution/no-map/) (retrieved 2026-09-25)
- [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/)
- [OSMF Geocoding Guideline (2017)](https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline)
- `docs/architecture/shared-http-redaction-and-key-resolution.md` (Issue #27)
- `docs/architecture/phase-3-interactive-add-in-research.md` (Goal (a), owner decision 1)
