# Census county lookup

Issue #32 (PH3-5) adds a small, standalone `SolidGround.Core` helper, `CensusCountyLookup`, that resolves the
5-digit Census county GEOID (`STATE`+`COUNTY`, zero-padded) containing a WGS 84 point, via the keyless
`geographies/coordinates` endpoint with `layers=Counties` pinned explicitly. It exists so the CLI's `parcel`
command (`docs/architecture/cli-workflow.md`'s "### parcel" subsection) can chain address/point -> geocoded
point -> automatically-resolved GEOID -> `CountyParcelRegistrySource` end to end, with no operator-supplied
GEOID required, while still letting an explicit `--geoid` override skip this lookup entirely. It adds no new
package: `HttpClient`/`System.Text.Json` are already BCL-available to Core, and this type reuses Issue #27's
shared `SolidGround.Core.Http` redaction/key-resolution component exactly as `CensusGeocoder` (Issue #28) and
`CountyParcelRegistrySource` (Issue #29) already do.

## Purpose and boundary

`CensusCountyLookup` is neither an `IAddressGeocoder` nor an `IParcelBoundarySource`: it does not geocode an
address and it does not resolve a parcel boundary. It is a single-purpose helper with one public method,
`FindCountyGeoidAsync(latitude, longitude, cancellationToken)`, returning a bare `string` GEOID. Its own
exception family, `CensusCountyLookupException` and four `sealed` leaves, is new and independent -- not a
subtype of `AddressGeocoderException` or `ParcelBoundarySourceException` -- because this type is not an
implementation of either of those interfaces.

## Request/response contract

| | |
| --- | --- |
| Method/endpoint | `GET https://geocoding.geo.census.gov/geocoder/geographies/coordinates` |
| Query parameters | `x` (longitude, `"R"` invariant), `y` (latitude, `"R"` invariant), `benchmark=Public_AR_Current`, `vintage=Current_Current`, `layers=Counties` (pinned explicitly -- the endpoint's own default layer set is documented as partial), `format=json` |
| Auth | None. No key parameter exists in Census's own documentation. |
| Success (200) shape | `{"result":{"geographies":{"Counties":[{"GEOID":"<5-digit string>", "STATE":"<2-digit string>", "COUNTY":"<3-digit string>", "NAME":"...", "BASENAME":"...", ...}]},"input":{...}}}`. `GEOID == STATE + COUNTY`, confirmed live, matching `CountyParcelRegistry`'s `\A\d{5}\z` requirement exactly -- the GEOID string needs no reformatting before it reaches `CountyParcelRegistrySource`'s `geoid` constructor parameter. |
| No-match shape | HTTP **200** (not an error status) with `{"result":{"geographies":{},...}}` -- `geographies` is an empty object with **no** `Counties` key at all, confirmed live with an out-of-US coordinate. This is a normal outcome (`CensusCountyLookupNoCountyException`), not a parse failure. |
| Error shape | Not independently verified for this endpoint. This type deliberately does not depend on the exact envelope: any non-200, non-5xx status classifies the same as a malformed 200 (`CensusCountyLookupUnexpectedResponseException`). |
| Attribution | `CensusCountyLookup` declares no attribution constant of its own because it has no candidate-shaped output to attach one to -- it returns a bare GEOID `string`. `parcel`'s JSON output (`geocodeInput`/`sourceDetail`) does not currently surface any Census attribution text either, including when `geoidOrigin` is `censusLookup`; only the separate `geocode` command's own `candidates[].attribution` field (`GeocodeCommand.cs`) surfaces a geocoder's `AttributionNotice` today. |

Every example above uses the synthetic GEOID `99999` and fabricated `STATE`/`COUNTY`/`NAME`/`BASENAME` values
this issue's own fixtures establish, never the real county this issue's live-verification research happened
to observe.

## Parsing discipline

`ParseSuccess` walks the response with `TryGetProperty` plus an explicit `JsonValueKind` check at every step
(root -> `result` -> `geographies` -> `Counties[0]` -> `GEOID`), mirroring
`LocalParcelFileSource`'s/`ParcelGeometryParser`'s own discipline, never a bare `GetProperty` call -- so a
malformed response always surfaces this type's own documented `CensusCountyLookupUnexpectedResponseException`
instead of letting a framework `KeyNotFoundException`/`InvalidOperationException` escape uncaught. `GEOID` must
be a JSON string (never a number -- a leading zero would be lost) and must match `\A\d{5}\z`; either failure is
`CensusCountyLookupUnexpectedResponseException`, not silently truncated or padded.

## The `--geoid` override

The CLI's `parcel` command finds the county-registry source's 5-digit GEOID automatically from the resolved
point through this lookup, unless an operator supplies `--geoid` explicitly -- which skips this lookup, and
therefore its one HTTP call, entirely. See `docs/architecture/cli-workflow.md`'s "### parcel" subsection for
the full option table and the exact request-ordering guarantees this produces (an explicit `--geoid` sends
only the county-registry query; an omitted one sends this lookup first, then the county-registry query).

## Test fixtures and guard constraints

`tests/SolidGround.Tests/Fixtures/census-county-lookup-point-hit-synthetic.json` and
`census-county-lookup-nomatch-synthetic.json` are the two committed fixtures, both shape-verified live but
carrying only synthetic values: GEOID `99999` (matching `CountyParcelRegistry`'s own fixture convention), the
public example-site coordinate (`41.591194, -93.603806`, the one coordinate `PersonalInformationGuardTests`
explicitly allows) for the point-hit fixture, and an out-of-US `(0, 0)` coordinate for the no-match fixture.
Neither fixture contains `http://`/`https://`/`api_key`/`bearer`/`authorization:` (`FixtureSecurityTests`) or an
owner-like property name (`ParcelFixtureSecurityTests`); neither test file nor fixture was modified to make
this true.

## Live-test instructions

`CensusCountyLookupLiveTests` is opt-in, skipped (never failed) unless `SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE=1`
and both `SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LATITUDE`/`SOLIDGROUND_CENSUS_COUNTY_LOOKUP_LIVE_LONGITUDE` are
set to a real coordinate (never committed), mirroring `CensusGeocoderLiveTests`'/`CountyParcelRegistryLiveTests`'
exact skip-never-fail style. This endpoint takes no key, so there is no key variable to check. CI never sets
any of these three variables.

## Open questions

1. The `geographies/coordinates` error envelope (a non-200, non-5xx status) was not independently
   live-verified during this issue's own research (budget-limited to a few keyless calls); this type
   classifies every such status the same as an undocumented 200 body rather than guessing at a shape.
2. Whether Census ever returns more than one entry in `Counties[]` for a single point (for example at a
   boundary-adjacent coordinate) is unconfirmed; only `Counties[0]` is read, matching how a WGS 84 point
   normally falls inside exactly one county.

## Citations

- [Census Geocoder API documentation](https://geocoding.geo.census.gov/geocoder/Geocoding_Services_API.html) (retrieved 2026-09-26)
- [Census Bureau Data API Terms of Service](https://www.census.gov/data/developers/about/terms-of-service.html) (retrieved 2026-09-25)
- `docs/architecture/address-geocoding.md` (Issue #28) -- `CensusGeocoder`, this type's closest sibling
- `docs/architecture/parcel-boundary-sources.md` (Issue #29) -- `CountyParcelRegistry`'s GEOID contract this lookup feeds directly
- `docs/architecture/shared-http-redaction-and-key-resolution.md` (Issue #27) -- the redaction helper reused here
- `docs/architecture/cli-workflow.md` -- the `parcel` command that chains this lookup in

No real county name, address, GEOID, service URL, or license text appears anywhere in this document or in any
file this issue adds.
