# Parcel boundary sources

Issue #29 (PH3-2) adds a pluggable `IParcelBoundarySource` abstraction to `SolidGround.Core` -- sibling to
`IElevationSource` (Issue #6) and `IAddressGeocoder` (Issue #28) -- with two implementations: a county REST
registry keyed by a 5-digit Census county GEOID (`CountyParcelRegistrySource`), and a vendor-neutral local
parcel-file reader for a user-purchased export shaped like Regrid's Standard schema (`LocalParcelFileSource`).
It adds no new package: NetTopologySuite 2.6.0 and ProjNET 2.1.0 (both already referenced by
`SolidGround.Core.csproj`) are reused, and `System.Text.Json`/`HttpClient` are already BCL-available.

Per the owner's 2026-09-24 privacy adjustment (AGENTS.md), **no real county is ever committed to this
repository**: the registry format ships with zero built-in entries -- a host maintains its own real counties in
a machine-local JSON file, at a path the host supplies -- and every fixture and test in this codebase uses a
fabricated GEOID (`99999`), a `.invalid` host, and fabricated attribute values around the public example-site
coordinate (`41.591194, -93.603806`).

## Purpose and boundary

This issue turns a WGS 84 point or an address substring into zero or more resolved parcel boundaries --
mechanism, not policy. `IParcelBoundarySource` is the contract; `CountyParcelRegistrySource` and
`LocalParcelFileSource` are the two shipped implementations. Exactly like Issue #28's own stated non-goal, this
issue deliberately does not wire a selection into `TerrainRequestSettings`, `RevitSettingsIo`'s shipped
template, or `SolidGround.Cli`: that wiring belongs to a future dialog or CLI issue that consumes a resolved
`ParcelBoundaryCandidate`. A resolved candidate converts into today's `ParcelGeometryAoi` (owner decision 8,
2026-09-21, `docs/architecture/phase-3-interactive-add-in-research.md`); no new `AreaOfInterestKind` was added
or is needed.

## Source comparison

| | `CountyParcelRegistrySource` | `LocalParcelFileSource` |
| --- | --- | --- |
| Network | Yes -- one HTTPS GET per `FindAsync` call, to a registered county's own ArcGIS REST layer | Never -- reads only a local file |
| Auth | None (every registry entry is an unauthenticated public county service) | N/A |
| Input format | Esri JSON (`f=json`), never GeoJSON | GeoJSON `FeatureCollection` only |
| Config | A machine-local `CountyParcelRegistry` JSON file, keyed by GEOID | `LocalParcelFileOptions` (path, field map, labels) |
| License posture | Per-registry-entry `licenseDisclaimerText`, surfaced verbatim | `LocalParcelFileOptions.LicenseDisclaimerText`, host-supplied |
| Typical use | A public county GIS department's own open-data service | A purchased nationwide/statewide parcel export (Regrid Data Store is the reference purchase) |

## Core types and files

| Type(s) | File | Namespace |
| --- | --- | --- |
| `IParcelBoundarySource` | `Sources/IParcelBoundarySource.cs` | `SolidGround.Core.Sources` |
| `ParcelBoundaryQuery` (abstract), `ParcelPointQuery`, `ParcelAddressQuery` | `Sources/ParcelBoundaryQuery.cs` | `SolidGround.Core.Sources` |
| `ParcelBoundarySourceKind`, `ParcelBoundaryCandidate`, `ParcelBoundaryAcquisition` | `Sources/ParcelBoundaryAcquisition.cs` | `SolidGround.Core.Sources` |
| `ParcelBoundarySourceException` (shared abstract base) | `Sources/ParcelBoundarySourceException.cs` | `SolidGround.Core.Sources` |
| `OwnerFieldNameGuard` | `Sources/OwnerFieldNameGuard.cs` | `SolidGround.Core.Sources` |
| `ParcelBoundaryWgs84` (shared WGS 84 reference and area computation) | `Sources/ParcelBoundaryWgs84.cs` | `SolidGround.Core.Sources` |
| `CountyParcelRegistry`, `CountyParcelRegistryDocument`, `CountyParcelRegistryEntry`, `CountyParcelFieldMap`, `CountyParcelRegistryFormatException` | `Sources/CountyParcels/CountyParcelRegistry.cs` | `SolidGround.Core.Sources.CountyParcels` |
| `CountyParcelRegistrySource` | `Sources/CountyParcels/CountyParcelRegistrySource.cs` | `SolidGround.Core.Sources.CountyParcels` |
| `CountyParcelRegistryException` (abstract) + 5 sealed leaves | `Sources/CountyParcels/CountyParcelRegistryException.cs` | `SolidGround.Core.Sources.CountyParcels` |
| `EsriJsonPolygonReader` | `Sources/CountyParcels/EsriJsonPolygonReader.cs` | `SolidGround.Core.Sources.CountyParcels` |
| `LocalParcelFileSource` | `Sources/LocalParcelFile/LocalParcelFileSource.cs` | `SolidGround.Core.Sources.LocalParcelFile` |
| `LocalParcelFileOptions`, `LocalParcelFileFieldMap` | `Sources/LocalParcelFile/LocalParcelFileOptions.cs` | `SolidGround.Core.Sources.LocalParcelFile` |
| `LocalParcelFileException` (abstract) + 3 sealed leaves | `Sources/LocalParcelFile/LocalParcelFileException.cs` | `SolidGround.Core.Sources.LocalParcelFile` |
| `ParcelBoundaryAoiFactory` | `Processing/ParcelBoundaryAoiFactory.cs` | `SolidGround.Core.Processing` |

14 new Core files. **One existing Core file is modified, additively:** `Aois/ParcelGeometryParser.cs` gets its
private `crs`-recognition rule extracted into a new `internal static bool IsRecognizedWgs84Crs(JsonElement,
HorizontalReference)` member, so `LocalParcelFileSource` can reuse the identical, already-hardened WGS 84
recognition logic when validating a `crs` member on a FeatureCollection/Feature object it only ever inspects as
an isolated fragment (never re-parsing the whole document through `ParcelGeometryParser.Parse` itself). Every
existing call site's control flow, thrown exception type, and message text is unchanged -- `ValidateCrs` now
just calls the extracted method and throws on `false`, exactly the boolean it always computed inline -- so
`ParcelGeometryParserTests.cs` needed no change. No other existing Core file is modified; no
`SolidGround.Cli`/`SolidGround.Revit` wiring is added.

`ParcelBoundaryWgs84` is `public`, not `internal`: `SolidGround.Core` grants no `InternalsVisibleTo` to
`SolidGround.Tests` (only `SolidGround.Cli` gets that grant, `src/SolidGround.Cli/InternalsVisibleTo.cs:9`), and
`ParcelBoundaryAreaTests.cs` calls `ParcelBoundaryWgs84.ComputeAreaSquareMeters` directly -- the same
public-not-internal reasoning already established for `OwnerFieldNameGuard` and `EsriJsonPolygonReader`. Its
`Reference` field stays `internal` (no test needs it directly). Neither `ParcelBoundaryWgs84` nor any other new
type in the exactly-matched `SolidGround.Core.Sources`/`SolidGround.Core.Processing` namespaces exposes a
NetTopologySuite type in a public constructor, method, property, or field signature
(`ArchitectureTests.NetTopologySuiteTypesNeverAppearInAnyNewPublicCoreSignature` covers this and passes
unmodified).

### `IParcelBoundarySource` -- one method, mirroring `IAddressGeocoder`

```csharp
public interface IParcelBoundarySource
{
    ValueTask<ParcelBoundaryAcquisition> FindAsync(
        ParcelBoundaryQuery query,
        CancellationToken cancellationToken = default);
}
```

One method, one request object, one shared exception ancestor -- the same shape as
`IElevationSource.AcquireAsync` and `IAddressGeocoder.GeocodeAsync`. A single `FindAsync(query)` was chosen over
two methods (`FindByPointAsync`/`FindByAddressAsync`) because `AreaOfInterest` already establishes this
codebase's pattern for "one operation, several input shapes selected by the caller's own concrete type, not a
`kind` flag." `ParcelPointQuery`'s constructor takes `(latitude, longitude)` -- not the task's originally
suggested `(longitude, latitude)` -- matching this codebase's own dominant convention for a lat/lon constructor
parameter pair (`AddressGeocodeCandidate`, `Wgs84RadiusAoi`); only wire-format-driven DTOs (Esri's `x`/`y`,
GeoJSON positions) stay longitude-first, and that convention stays fully internal to their own parsers.

Unlike `AddressGeocodeAcquisition` (a non-empty, best-match-first list -- a provider with zero matches throws
its own exception instead), `ParcelBoundaryAcquisition` explicitly **allows zero candidates**: most WGS 84
points are not inside the currently-registered parcel fabric (street rights-of-way, water, a point outside the
loaded county), and most address substrings simply will not match. `ParcelBoundaryCandidateTests.cs` tests this
divergence directly.

### Owner-field policy (AC2/AC4)

Three independent layers, both sources:

1. **Structural allow-list.** The REST source's `outFields` is always the field map's own mapped values, never
   `*`. The file source's reader only ever looks up the 11 named field-map slots on a feature's `properties`
   object -- never a generic enumeration -- so an owner/mailing property (or a hypothetical future
   `enhanced_ownership`-shaped property) physically present in a response or file can never reach a
   `ParcelBoundaryCandidate`.
2. **Closed candidate shape.** `ParcelBoundaryCandidate` has a fixed set of named properties; there is no "extra
   attributes" bag an owner value could ride along in.
3. **Configuration-time denylist.** `OwnerFieldNameGuard.IsOwnerLike` rejects, at registry-load time
   (`CountyParcelRegistry.Load`) or field-map-construction time (`LocalParcelFileFieldMap.Validate`, called from
   `LocalParcelFileSource`'s constructor), any field-map value that names or resembles a known owner/mailing
   field -- so a misconfigured mapping is caught before it could ever populate layer 1.

`OwnerFieldNameGuard.KnownOwnerFieldNames` lists AC2's six named tokens (`OWNER_NAME`, `OWN_ADD`, `OWN_CITY`,
`OWN_STATE`, `OWN_ZIP`, `CAREOF`) plus Regrid's own named ownership and mailing-address fields, and its Enhanced
Ownership join key/product name. Beyond the exact list, `"own"`/`"mail"` are also flagged as *fragments*, but
only when they begin a "word" within the field name (not immediately preceded by an ASCII letter): an
unanchored substring match would wrongly reject a legitimate standard PLSS cadastral field such as `TOWNSHIP`
or `TOWNSHIP_RANGE`, which merely contains `"own"` mid-word. `"care_of"`/`"careof"`/`"c/o"`/`"attn"` keep a
plain, unanchored substring match (longer, more specific fragments with no demonstrated collision). A separate,
normalized/alnum-stripped `"co"` fragment was rejected during design because it would false-positive on
`"county"` the same way an unanchored `"own"` false-positives on `"township"`.

`ParcelFixtureSecurityTests.cs` scans every committed `.json`/`.geojson` fixture's JSON property names with
this exact same `IsOwnerLike` check (fragments included, not just the exact list), so all three call sites use
one detection list uniformly.

## `CountyParcelRegistrySource` -- the REST source

### Registry format

Loaded strictly (camelCase, unknown members rejected at every nesting level, `//` comments and trailing commas
tolerated -- mirrors `TerrainRequestSettings.JsonOptions`'s own contract exactly):

```jsonc
{
  "schemaVersion": 1,
  "counties": [
    {
      "geoid": "99999",
      "displayName": "Synthetic County (fixture only)",
      "serviceBaseUrl": "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer",
      "layerIndex": 0,
      "fieldMap": {
        "parcelId": "PARCEL_ID",
        "situsAddress": "SITUS_ADDR",
        "subdivision": "SUBDIVISION",
        "lot": "LOT_NUMBER",
        "block": "BLOCK",
        "plat": "PLAT_NUMBER",
        "book": "DEED_BOOK_PAGE",
        "page": "DEED_BOOK_PAGE",
        "legalDescription": "LEGAL",
        "reportedAcres": "ACRES",
        "zoning": "ZONING",
        "stableParcelId": null
      },
      "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: this data is provided \"as is\" for testing only, with no warranty of any kind, express or implied, and is not an official record of any government entity."
    }
  ]
}
```

`CountyParcelRegistry.Load(path)` collects every problem (never stops at the first) and throws one
`CountyParcelRegistryFormatException` naming all of them: a wrong `schemaVersion`, an empty `counties` array, a
`geoid` that is not exactly 5 ASCII digits, a duplicate `geoid`, a non-`https` or unparseable `serviceBaseUrl`, a
`serviceBaseUrl` that ends with a trailing slash or whose final path segment already looks like a `layerIndex`
(so `CountyParcelRegistrySource.BuildLayerQueryPathAndFixedParameters` can append `/{layerIndex}/query` without
ever producing a doubled slash or a duplicated layer segment), a negative `layerIndex`, a blank
`displayName`/`licenseDisclaimerText`/`fieldMap.parcelId`/`fieldMap.situsAddress`, or any non-null `fieldMap`
value that is owner-like.

`book`/`page` mapped to the same combined field (as in the example above) is deliberate: when a county only
publishes one combined "deed book/page" column, the same raw string is honestly surfaced under both `Book` and
`Page` rather than guessed-split on a delimiter, and `CountyParcelRegistrySource` always sets
`BookPageAreUnconfirmedProxies = true` whenever either is non-null -- a conservative, always-on label, since no
county ArcGIS service has a schema this codebase can verify real book/page semantics against.
`FieldMap.SitusAddress` also doubles as the address-search field (not a separate `AddressSearchField`
property): the situs address is both what gets searched and what gets displayed in the overwhelming majority
of real county schemas, and a separate property would let the two drift with no test able to catch the
mismatch except by construction.

**The registry document is never a committed fixture file:** `FixtureSecurityTests` bans `http://`/`https://`
anywhere under `Fixtures/`, and a registry entry's `serviceBaseUrl` is inherently `https://`. Every
registry-loader/`CountyParcelRegistrySource` test instead builds the registry JSON as an inline C# raw string
literal and writes it to a uniquely named file under `Path.GetTempPath()`, deleted in a `finally` block --
mirroring how `EsriGeocoderOptions.DefaultEndpointUri`'s own real `https://...` literal already lives directly
in Core source, not a fixture file.

### Request construction and response handling

`BuildRequestUri` composes the request through `Uri` itself, never by raw string concatenation of
`entry.ServiceBaseUrl`: the layer/operation path (`/{layerIndex}/query`) is appended to `serviceBaseUrl`'s own
scheme+host+path (`Uri.GetLeftPart(UriPartial.Path)`), and any query string `serviceBaseUrl` already carries is
folded in as distinct, well-formed leading parameters ahead of this method's own fixed ones. `CountyParcelRegistry.Load`
only requires `serviceBaseUrl` to be an absolute `https` URL, not one bare of its own query string, so this keeps
a hypothetical future per-entry `?token=...` (see the no-authentication note below) from corrupting the request
into the wrong resource and a garbled parameter, which a naive `$"{entry.ServiceBaseUrl}/{layerIndex}/query?..."`
concatenation would otherwise silently produce.
`AServiceBaseUrlContainingItsOwnQueryStringStillComposesAWellFormedLayerQueryRequest`
(`CountyParcelRegistrySourceTests.cs`) proves this against the exact request `AbsoluteUri`.

Always `f=json` (Esri JSON), never `f=geojson`: `f=geojson` returns RFC 7946-conformant (WGS 84 lon/lat)
coordinates only when `outSR` is unset or `4326`, and silently returns projected, non-conformant coordinates
inside a GeoJSON envelope otherwise. Using `f=json` unconditionally, always paired with an explicit `outSR=4326`,
sidesteps that pitfall entirely. Every request sends `outFields` (the field map's own mapped values,
deduplicated, comma-joined -- never `*`), `returnGeometry=true`, `outSR=4326`, and an explicit
`spatialRel=esriSpatialRelIntersects`. A point query adds `geometryType=esriGeometryPoint`,
`geometry={longitude},{latitude}`, `inSR=4326`, `where=1%3D1`. An address query adds `resultRecordCount=25`
(`CountyParcelRegistrySource.MaximumAddressMatches`, bounding a single request's own cost against a
county-wide layer) and a `where` clause built by `BuildAddressWhereClause`:

1. Reject a search text over 200 characters (`CountyParcelRegistrySource.MaximumAddressSearchTextLength`) before
   building anything -- `CountyParcelRegistryRequestValidationException`, no request sent.
2. Escape order matters: first double every literal `\` (the chosen `ESCAPE` character), then escape every
   literal `%`/`_`, **then** double every single quote last, so an escaped `\'`-shaped sequence is never
   re-mangled by the quote-doubling step: `UPPER(field) LIKE UPPER('%escaped%') ESCAPE '\'`.
3. Percent-encode the fully composed value exactly once, via `Uri.EscapeDataString`, mirroring
   `EsriGeocoder.BuildRequestUri`'s own identical precedent (`src/SolidGround.Core/Sources/Esri/EsriGeocoder.cs`).

The response body's top-level `error` property is always checked before `features`, regardless of HTTP status
-- the same "transport success does not imply semantic success" posture `AddressGeocoderException` already
establishes. An empty `features` array is a normal empty result, never an exception. `exceededTransferLimit`
surfaces as `ParcelBoundaryAcquisition.ResultSetTruncated`; every returned feature still becomes a candidate --
it is never used to silently pick "the first" one. Redaction reuses `SensitiveQueryParameterNames.Esri`
(`["token"]`) via `SensitiveQueryRedactor` -- the same underlying ArcGIS REST platform Esri's own geocoder uses
(Issue #27's `docs/architecture/shared-http-redaction-and-key-resolution.md` names this issue as an anticipated
caller of exactly this preset-reuse pattern). No `...AuthorizationException` leaf exists: every registry entry
is an unauthenticated public service; a future issue can add a per-entry optional token, following the
`ApiKeyResolver` precedent, once a real entry needs one.

### `EsriJsonPolygonReader` -- rings, holes, multipart, orientation

Turns one feature's flat `rings` array into a validated `PolygonalRegion`, per Esri's documented convention:
exterior rings are wound clockwise, holes counterclockwise, and a multipart polygon is just more rings in the
same flat array, disambiguated by winding, not explicit nesting.

1. Each ring's signed area is computed with the standard shoelace formula. Negative means clockwise (a new
   shell); positive means counterclockwise (a hole).
2. A counterclockwise ring is assigned to a shell by a **boundary-inclusive** containment test (`Covers`, not
   `Contains`): Esri's own documentation states rings "can touch at a vertex," so a hole whose first vertex
   legitimately touches its shell must not be wrongly rejected under an interior-only test. This mirrors this
   codebase's own existing precedent for exactly this interior-or-boundary distinction,
   `GridClipper.IsCellIncluded`'s `prepared.Covers(centerPoint)` call
   (`src/SolidGround.Core/Clipping/GridClipper.cs`). `Covers` is tried first against the most-recently-opened
   shell, then every earlier shell in reverse order; no covering shell found throws
   `CountyParcelRegistryUnexpectedResponseException` naming the feature and ring index.
3. One NTS `Polygon` per shell (with its assigned holes); more than one shell becomes a `MultiPolygon`.
4. `PolygonalRegion.FromGeometry` re-validates everything (finite/geographic-range coordinates, `IsValidOp`
   topology) -- a service that violates its own documented orientation convention badly enough to produce
   nonsense topology is caught here, not silently accepted. This codebase does not implement a full even-odd
   polygon reconstruction fallback for the (undocumented-as-guaranteed) case where a service violates the
   orientation convention outright.

`EsriJsonPolygonReader` is `public`, not `internal`: `SolidGround.Core` grants no `InternalsVisibleTo` to
`SolidGround.Tests`, and `EsriJsonPolygonReaderTests.cs` calls it directly and non-reflectively. Its public
signature (`Read(IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings, HorizontalReference reference, int
featureIndex)`) never exposes an NTS type; `CountyParcelRegistrySource` builds those `(X, Y)` tuples from the
raw Esri JSON `rings` array itself, reading only each point's first two ordinates -- a third (Z) ordinate, if a
service ever returns one, is dropped by construction (this codebase never requests `returnZ`/`returnM`).
Because the reader's own tuple type structurally excludes a Z ordinate, the Z-dropping regression is instead
proven at the `CountyParcelRegistrySourceTests` level, feeding a raw three-element-point JSON body through the
whole pipeline and confirming it parses to the expected area rather than failing.

## `LocalParcelFileSource` -- the local file source

### Field map and reading

`LocalParcelFileFieldMap.RegridStandardDefault` maps to Regrid's own Standard-schema field names
(`parcelnumb`, `address`, `subdivision`, `lot`, `block`, `plat`, `book`, `page`, `legaldesc`, `ll_gisacre`,
`zoning`, `ll_uuid`); every non-null value is checked against `OwnerFieldNameGuard.IsOwnerLike` when
`LocalParcelFileSource`'s constructor calls `LocalParcelFileFieldMap.Validate()`. Owner/mailing and
`enhanced_ownership`-shaped Regrid fields are never mapped by that default, and structurally cannot be, per the
allow-list read path above.

**Implementation note (a documented, permitted simplification):** the design record for this issue described an
optional, more complex streaming `Utf8JsonReader` reader (bounded peak memory for a very large file). This
implementation instead reads the whole file with one `JsonDocument.Parse` call -- an explicitly acceptable
fallback the design record itself names ("a conscious memory/complexity trade-off, not a correctness one,
since [this issue's acceptance criteria] test fixture-scale files, not real-world county-scale ones"). Every
other behavior (the allow-list read path, the `crs` validation described below, the point/address predicates)
is unaffected by this choice; a future issue can switch to a streaming reader without changing this source's
public contract if a real file's size ever makes that necessary.

Every geometry is assumed WGS 84 by default (RFC 7946, and confirmed directly for Regrid's own GeoJSON delivery
format). An explicit, disagreeing legacy `crs` member -- the pre-RFC7946 GeoJSON convention some older exports
still carry -- is detected and rejected (`LocalParcelFileFormatException`) at both levels it could legally
appear on a `FeatureCollection`: the root and each Feature. This reuses
`ParcelGeometryParser.IsRecognizedWgs84Crs` (the member this issue's one Core edit extracts), rather than
re-deriving the recognition rule, so a `crs` member naming `CRS84`/`EPSG:4326` (in any of `ValidateCrs`'s
already-recognized forms) is still accepted, and anything else is rejected rather than silently trusted. A
feature's `geometry` object is then handed, as its own isolated JSON text, to the existing, already-hardened
`ParcelGeometryParser.Parse(GeoJson, ...)` -- reusing 100% of its ring/hole/closure/topology logic; no second
GeoJSON geometry parser exists in this codebase.

### Point-in-polygon predicate

`FindAsync(ParcelPointQuery)`'s exact test is **boundary-inclusive** (`Geometry.Intersects`, not `Contains`),
matching the REST source's own explicit `spatialRel=esriSpatialRelIntersects` choice -- so the two sources never
disagree about a point that falls exactly on a parcel line. A cheap envelope bounding-box reject runs first, so
the exact NTS test only runs for a feature whose envelope could plausibly contain the point.

### Per-feature relevance before validation

`TryBuildCandidate` checks whether a feature is even relevant to the query *before* enforcing anything about its
shape. For `ParcelAddressQuery`, the situs-address substring check runs first: a missing or non-object
`properties` object is read best-effort (`hasProperties`) and simply yields a `null` situs address, treated as
non-matching rather than thrown. For `ParcelPointQuery`, geometry must be parsed to know relevance at all, so
`TryParseGeometry` parses it best-effort too -- a missing `geometry` object or a caught `ParcelGeometryException`
yields `null`, treated the same as "does not intersect" rather than thrown. A non-object array element can never
be relevant to *any* query (there is no `properties` to read and no geometry to test), so it is unconditionally
skipped, never thrown, regardless of query type. Only once a feature is confirmed relevant -- by address match or
by point intersection -- does `TryBuildCandidate` validate its feature-level `crs`, require that `properties`
actually parsed as an object, require that geometry actually parsed, and require the mapped
`ParcelId`/`SitusAddress` fields to be present; any failure at that point is a real, reportable error, because
this exact feature is now known to be the one the caller asked for. This means a feature with any structural
defect -- not an object, a missing/`null` `properties`, an unrecognized `crs`, or unparsable geometry -- that is
not what the caller is looking for can never abort resolution of a different, matching feature in the same file
-- a realistic shape for a county-wide export, where administrative/water/right-of-way rows commonly carry
incomplete or malformed attributes. (An earlier version of this design deferred only the required-field check,
leaving every structural check unconditional; adversarial review found this left the same abort-on-unrelated-row
failure mode open for a `null`/missing `properties` object on either query type, and for missing or unparsable
geometry on `ParcelPointQuery` specifically -- closed by `TryParseGeometry`'s best-effort parse and moving the
remaining structural checks behind both relevance filters.)
`APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasABlankRequiredField`,
`AnAddressQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasABlankRequiredField`,
`APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasNullProperties`,
`AnAddressQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasNullProperties`, and
`APointQueryStillResolvesAMatchingFeatureWhenAnUnrelatedFeatureHasUnparsableGeometry`
(`LocalParcelFileSourceTests.cs`) prove this with two-feature files.

### Error mapping

`LocalParcelFileException` (abstract, `SourceName` fixed to `"LocalParcelFile"`) has three sealed leaves:
`LocalParcelFileNotFoundException` (the configured path does not exist), `LocalParcelFileFormatException` (the
file exists but is not well-formed per this reader's contract -- invalid JSON, not a `FeatureCollection`, a
disagreeing `crs` member, a required mapped field missing, or a rewrapped `ParcelGeometryException`), and
`LocalParcelFileAccessException` (the path exists but could not be opened or read for another reason -- a
permission error or a sharing violation; kept distinct because the remedy differs). The third leaf is exercised
by opening the temp file with `FileShare.None` from the test itself immediately before calling `FindAsync`,
forcing a real Windows sharing violation.

## Area computation

`ParcelBoundaryCandidate.ComputedAreaSquareMeters` is always computed by SolidGround itself from the resolved
`Boundary`, never trusted from a source's own "reported acreage" field (kept separately, nullable, as
`ReportedAcres`). `ParcelBoundaryWgs84.ComputeAreaSquareMeters` uses the boundary's own true polygon area
(`PolygonalRegion.Area`, already public and package-neutral) scaled by the meters-per-degree factors
(`Wgs84Ellipsoid.MetersPerDegreeLongitude`/`MetersPerDegreeLatitude`) at the boundary's center latitude -- never
a bounding-box (envelope width times height) approximation, which systematically overstates area for any
non-rectangular parcel (the norm for cadastral data). `ParcelBoundaryAreaTests.cs` proves this distinction
directly with an inline, notched ("L-shaped") polygon whose true area and bounding-box area differ by ~19%,
well over the 2% tolerance this codebase otherwise uses for area assertions -- a shape a near-rectangular
fixture could not have exposed.

## Fixtures

All new fixtures live under `tests/SolidGround.Tests/Fixtures/`, `*-synthetic*` named, and use the address
literal `"100 Example Loop"` throughout -- continuing this repository's own existing convention (`"Loop"` is
absent from `PersonalInformationGuardTests.StreetAddressPattern`'s recognized-suffix list, unlike a house
number followed by a recognized street-type word, which would match it). Coordinates stay within
`PersonalInformationGuardTests`' tolerance of the public example site.

| File | Shape |
| --- | --- |
| `county-parcel-registry-point-hit-synthetic.json` | An Esri JSON query response, one feature, ring in clockwise order (the Esri convention -- the reverse of `example-site-synthetic-parcel.geojson`'s existing counterclockwise ring), `attributes` narrowed to exactly this fixture's `outFields` list, all-fabricated values, plus a top-level `"licenseDisclaimerText"` carrying the fabricated no-warranty text verbatim. |
| `county-parcel-registry-address-hit-synthetic.json` | The same shape, two features (two independently fabricated PARCEL_ID/address pairs over the same footprint), proving multiple address matches parse independently. |
| `county-parcel-registry-zero-results-synthetic.json` | A well-formed envelope with `"features": []`, proving the empty-result-is-not-an-exception contract. |
| `county-parcel-registry-exceeded-transfer-limit-synthetic.json` | One feature plus `"exceededTransferLimit": true`, proving `ResultSetTruncated` without dropping the returned candidate. |
| `local-parcel-file-standard-schema-synthetic.geojson` | A Regrid-Standard-shaped `FeatureCollection`, WGS 84, one feature reusing `example-site-synthetic-parcel.geojson`'s exact ring coordinates (already guard-approved, already known-1,600 m² area) with Regrid-named properties and no owner/mailing property anywhere, plus a `"note"` property stating it is synthetic/illustrative. |

Each REST fixture's own `"licenseDisclaimerText"` property is inert to parsing (`EsriQueryResponseDto`'s lenient
decode -- no strict options -- silently ignores an unmapped top-level property, the same precedent
`EsriGeocoder`'s own response DTO already establishes) and exists solely so
`CountyParcelRegistrySourceTests.cs` can assert the registry entry's disclaimer, the fixture's own embedded
property, and the resulting candidate's `LicenseDisclaimerText` all agree exactly -- "verbatim," proven
end-to-end, not merely by construction.

The owner-field-dropped-on-read proof (`LocalParcelFileSourceTests`) and the legacy-`crs`-rejection proofs never
touch a committed fixture, per the task's own hard rule: they build small, in-test-only GeoJSON payloads
(containing, respectively, `"owner": "SYNTHETIC OWNER"` and a legacy `crs` member) and write them to a temporary
file, deleted after the test.

## Test evidence

13 new test files, zero existing test files modified (`FixtureSecurityTests.cs`, `PersonalInformationGuardTests.cs`,
and `ArchitectureTests.cs` are untouched; every new fixture and source file is designed to pass them unchanged):

| File | Covers |
| --- | --- |
| `ParcelBoundaryQueryTests.cs` | `ParcelPointQuery` range validation; `ParcelAddressQuery` null/blank rejection. |
| `ParcelBoundaryCandidateTests.cs` | Constructor validation (non-geographic boundary, blank/non-positive fields, optional-string blank-when-given, each blank optional string's thrown `ArgumentException.ParamName` naming its own real constructor parameter rather than a shared wrapper's local); `AccuracyLabel`'s fixed value; the zero-candidates `ParcelBoundaryAcquisition` divergence from `AddressGeocodeAcquisition`. |
| `OwnerFieldNameGuardTests.cs` | Every named token and a representative sample of Regrid's named fields; the `TOWNSHIP`/`county`/`ll_gisacre`/`ll_uuid` false-positive-risk names; the word-start anchor's effect on both directions. |
| `CountyParcelRegistryTests.cs` | Valid load; strict decode; a non-5-digit/duplicate GEOID; a non-`https`, trailing-slash, or already-layer-segmented `serviceBaseUrl`; an owner-like field map value; the wrong `schemaVersion`; multiple simultaneous problems reported in one exception. |
| `CountyParcelRegistrySourceTests.cs` | Unregistered GEOID before any request; the exact point/address query strings (including percent-encoding); fixture parsing (point hit, address hit, zero results, exceeded transfer limit); 400/500/malformed-body classification; the 200-character address bound; a `serviceBaseUrl` containing a fake token never leaking into a redacted URI or message; a `serviceBaseUrl` containing its own query string still composing a well-formed `/{layerIndex}/query` request; a three-ordinate ring position's Z dropped. |
| `EsriJsonPolygonReaderTests.cs` | A single shell; a shell plus a contained hole; a hole touching its shell at exactly one vertex (the `Covers` case); two disjoint shells (multipart); a lone hole with no enclosing shell; an unclosed ring; a too-short ring. |
| `ParcelBoundaryAreaTests.cs` | The true-polygon-area-vs-bounding-box-area distinction, via an inline notched polygon. |
| `LocalParcelFileFieldMapTests.cs` | `RegridStandardDefault`'s exact field names; `Validate()`'s owner-like and blank rejections. |
| `LocalParcelFileSourceTests.cs` | Point hit, address hit (case-insensitive), a point outside every envelope, a point exactly on the boundary, an owner field physically present but never surfaced, a root- and Feature-level legacy `crs` rejection, a missing file, a malformed file, a locked file, and a point/address query each still resolving a matching feature when a second, unrelated feature has a blank required field, `null` `properties`, or (point query only) unparsable geometry. |
| `LocalParcelFileArchitectureTests.cs` | `LocalParcelFileSource`'s public constructor never takes an `HttpClient`/`System.Net.Http` parameter. |
| `ParcelFixtureSecurityTests.cs` | Every committed JSON/GeoJSON fixture's property names, scanned with `OwnerFieldNameGuard.IsOwnerLike`. |
| `ParcelBoundaryAoiFactoryTests.cs` | `FromCandidate`'s WKT round trip into an equivalent-area `PolygonalRegion`, and its buffer pass-through. |
| `CountyParcelRegistryLiveTests.cs` | Opt-in (see below). |

Verified against the checked-out tree: `dotnet restore SolidGround.slnx --locked-mode`,
`dotnet build SolidGround.slnx --configuration Release --no-restore` (0 warnings, 0 errors across all four
projects), and `dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release
--no-build` (1,298 total, 1,293 succeeded, 0 failed, 5 skipped -- 157 more tests than the pre-issue baseline
of 1,141 total/1,137 succeeded/4 skipped, with the one additional skip being this issue's own opt-in live test
declining offline). The count grew from the 1,280/1,275 first recorded here by 3 during an earlier adversarial
review round (the well-formed-request-URI test and the two "unrelated blank field" tests named above), then by a
further 6 during a later round: `RejectsAServiceBaseUrlEndingWithATrailingSlash` and
`RejectsAServiceBaseUrlThatAlreadyIncludesALayerSegment` (`CountyParcelRegistryTests.cs`),
`ConstructorReportsTheRealParameterNameWhenAnOptionalStringIsBlank` (`ParcelBoundaryCandidateTests.cs`), and the
three further "unrelated feature" tests named in "Per-feature relevance before validation" above
(`LocalParcelFileSourceTests.cs`) -- closing, respectively, a `serviceBaseUrl` trailing-slash/layer-segment
validation gap, a lost `ArgumentException.ParamName` in `ParcelBoundaryCandidate`'s optional-string checks, and
the `null`/missing-`properties` and unparsable-point-query-geometry cases the relevance-before-validation
reordering had not yet covered. The final review round added the remaining 9 (registry-validation, GEOID-format,
transport-failure, and per-feature `crs` cases).

## Live-test instructions (AC5)

REST source only, mirroring `EsriGeocoderLiveTests`'s exact skip-never-fail style (read-only environment-variable
access, no mutation of the real process environment):

| Flag | Registry path | GEOID | Point |
| --- | --- | --- | --- |
| `SOLIDGROUND_COUNTY_PARCEL_LIVE=1` | `SOLIDGROUND_COUNTY_PARCEL_REGISTRY_PATH` | `SOLIDGROUND_COUNTY_PARCEL_GEOID` | `SOLIDGROUND_COUNTY_PARCEL_LIVE_LATITUDE`, `SOLIDGROUND_COUNTY_PARCEL_LIVE_LONGITUDE` |

All five must be set (the flag exactly `"1"`; the two coordinate variables must parse as `double`), or the test
skips, naming every variable -- never fails. No key/token variable exists, because this MVP registry format has
none: every entry is assumed unauthenticated. CI never sets any of these five variables.

## How a user adds their own county

1. Create or extend the machine-local registry JSON file at a path your host application supplies (never a path
   under this repository).
2. Find the county's ArcGIS `MapServer`/`FeatureServer` base URL and the parcel layer's index -- usually from
   the county GIS department's own open-data page or REST services directory.
3. Request that layer's `?f=json` metadata to find its real field names, and build a `fieldMap` entry from them.
4. Confirm `serviceBaseUrl` is `https`, and that the service is intended for public/programmatic use.
5. Never map a field that names or resembles owner/mailing data; `CountyParcelRegistry.Load` rejects one anyway,
   but confirm your intent before adding it.

## Not a survey

Every `ParcelBoundaryCandidate`, from both sources, carries the same fixed
`ParcelBoundaryCandidate.NotASurveyDisclaimer`: "This boundary is a cadastral/assessor tax-map representation,
not a survey." This is not a constructor parameter -- it cannot be overridden per candidate. Consistent with
AGENTS.md's accuracy rule, SolidGround never claims a resolved parcel boundary is suitable for foundation-
perimeter grading or any other survey-grade use; it is a starting point for the operator to confirm.

## Regrid Data Store as the reference purchase

Regrid Data Store's Standard tier is described here only factually, as the commercial nationwide parcel-data
product `LocalParcelFileFieldMap.RegridStandardDefault` targets by field-name shape: a purchased, licensed
export whose license is understood to restrict use to the purchaser's own internal tools. This document does
not quote or reproduce Regrid's actual license/terms-of-service text anywhere (the same hard rule this issue's
fixtures and tests already follow) -- an operator configuring `LocalParcelFileSource` in a real deployment must
supply their own accurate license/disclaimer text via `LocalParcelFileOptions.LicenseDisclaimerText`.

## Non-goals (explicit)

- No `SolidGround.Cli`/`SolidGround.Revit`/`TerrainRequestSettings` wiring -- a future issue selects and wires a
  concrete `IParcelBoundarySource`.
- No per-registry-entry authentication. A future issue can add an optional API-key field, following the
  `ApiKeyResolver` precedent, once a real registry entry needs one.
- No ArcGIS REST pagination (`resultOffset`/`resultPaginationToken`); `ResultSetTruncated` is surfaced instead of
  automatically walked.
- No unit conversion for a county registry's `reportedAcres` mapped field -- assumed to already be in acres. A
  county publishing the equivalent field in square feet is a known, documented limitation;
  `ComputedAreaSquareMeters` is unaffected, since it is always computed from geometry.
- No full even-odd polygon reconstruction fallback for a service that violates its own documented ring-winding
  convention outright -- `IsValidOp`'s topology check is the accepted backstop.
- No caching of a local parcel file's contents across `FindAsync` calls -- always read fresh from disk.
- No new `AreaOfInterestKind` and no new geospatial package.
- No GeoPackage/Shapefile support for the local file source -- GeoJSON only (neither NetTopologySuite nor
  ProjNET parses either format).
- No streaming `Utf8JsonReader` implementation for the local file source in this issue (see "Field map and
  reading" above) -- an explicitly permitted, documented simplification for this issue's fixture-scale scope.

## Open questions

1. Whether a real county's ArcGIS layer actually enforces `useStandardizedQueries: true` (and therefore the
   exact `LIKE`/quoting dialect `BuildAddressWhereClause` assumes) is a per-service fact a registry maintainer
   should verify at onboarding time, not a nationwide constant this codebase can assume.
2. A county reporting acreage in square feet, not acres, cannot be represented correctly by `ReportedAcres`
   today -- `ComputedAreaSquareMeters` is unaffected, since it is always computed from geometry, never from this
   field.
3. Esri's own documentation does not guarantee ring orientation is always followed by every service; this
   implementation trusts the documented convention with `IsValidOp` as a backstop, not a full even-odd
   reconstruction.
4. Regrid's own live schema-fetch endpoint could validate `LocalParcelFileFieldMap.RegridStandardDefault`
   against Regrid's current schema programmatically in a future issue; not used here, since the local file
   source never calls any network endpoint at all, by design (AC4).
5. Whether the streaming-reader design this issue's own record originally considered is ever needed depends on
   real-world file sizes an operator actually supplies; the whole-file `JsonDocument.Parse` implementation
   shipped here is simpler and fully correct at fixture scale, and can be revisited without changing this
   source's public contract.

## Citations

- [ArcGIS REST APIs: Query (Feature Service/Map Service)](https://developers.arcgis.com/rest/services-reference/enterprise/query-feature-service-layer/) (retrieved 2026-09-25)
- [ArcGIS REST APIs: Geometry objects](https://developers.arcgis.com/documentation/common-data-types/geometry-objects.htm) (retrieved 2026-09-25)
- [ArcGIS REST APIs: SQL reference for query expressions](https://developers.arcgis.com/rest/services-reference/enterprise/sql-syntax/) (retrieved 2026-09-25)
- [Esri data attribution ("no map")](https://developers.arcgis.com/documentation/esri-and-data-attribution/no-map/) (retrieved 2026-09-25)
- [Regrid Standard schema field reference](https://support.regrid.com/) (retrieved 2026-09-25) -- field names only; no license/ToS text reproduced
- `docs/architecture/address-geocoding.md` (Issue #28) -- the sibling source this design mirrors most closely
- `docs/architecture/shared-http-redaction-and-key-resolution.md` (Issue #27) -- the redaction/key-resolution helper reused here
- `docs/architecture/phase-3-interactive-add-in-research.md` -- owner decision 8 (parcel resolution converts into today's AOI shape)

No real county name, address, GEOID, service URL, or license text appears anywhere in this document or in any
file this issue adds.
