# OpenTopography USGS 1 m source

Issue #4 implements the first `IElevationSource`: `SolidGround.Core.Sources.OpenTopography.OpenTopographyUsgs1mSource`, which calls OpenTopography's `usgsdem` endpoint for the `USGS1m` dataset as an `AAIGrid` response. It adds no package reference; it uses only `System.Net.Http`, `System.IO.Compression`, `System.Xml.Linq`, and `System.Text`.

## Endpoint contract, verified 2026-09-15/16

Checked against the [OpenTopography OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json) and [API documentation](https://portal.opentopography.org/apidocs/) on 2026-09-16:

- Endpoint: `GET https://portal.opentopography.org/API/usgsdem`.
- Query parameters: `datasetName` (enum `USGS30m`/`USGS10m`/`USGS1m`, required), `south`/`north` (`-90..90`), `west`/`east` (`-180..180`), `outputFormat` (enum `GTiff`/`AAIGrid`/`HFA`; the documented default is `GTiff`, so this source always sends `outputFormat=AAIGrid` explicitly), and `API_Key` (required, a **query parameter** named exactly `API_Key` — the API has no header transport for the key).
- Documented responses: `200 OK`, `204 No Data`, `400 Bad request`, `401 Unauthorized`, `500 Internal error`. The OpenAPI definition does not document `403` or `429`, and does not document response content types.
- USGS1m carries a documented 250 km² per-request area limit and undocumented rate limits (200 calls/24h for academic accounts, 50/24h otherwise; no documented HTTP shape for exceeding them). USGS 1 m access itself is restricted to academic users or an enterprise API key.
- Observed live with an invalid key: HTTP 401, `Content-Type: application/xml`, body exactly `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><error>Error: Not a valid format API Key: invalid. Please register for an API key at www.opentopography.org</error>`. The server echoes the submitted key back into the body, which is why every server-supplied message this source surfaces is redacted before it reaches an exception.

## Key transport and redaction

Because `API_Key` is a query parameter, the raw key value necessarily appears in the outgoing request URI. `OpenTopographyApiKey` wraps the raw string, exposes it only through an `internal` accessor the request builder uses, and its `ToString()` and `[DebuggerDisplay]` both read `[REDACTED]`. No public member of `SolidGround.Core` returns the raw value; only one HTTP call is made per acquisition, and the key is used solely to build that one request URI.

Every URI this source surfaces outside that single request — in an exception property, a log-worthy string, or a report — is passed through `OpenTopographyRedaction.RedactUri`, which replaces the value of any `API_Key` query parameter (any position, case-insensitive, even with a missing value) with `REDACTED`. Any server-supplied text (an error body, an `HttpRequestException.Message`, and so on) is passed through `OpenTopographyRedaction.RedactText`, which removes the raw key value, its `Uri.EscapeDataString` form, and any literal `API_Key=<value>` pattern (independent of whether a key is even available), replacing each with `[REDACTED]`, then bounds the result to 512 characters. `OpenTopographyException.RedactedRequestUri` is therefore the only request-URI form this source ever exposes, and every exception's `ServerMessage` (where present) is pre-redacted and has its XML/HTML tags stripped.

`Exception.ToString()` recurses into `InnerException`, so an exception's own outer `Message` being redacted is not enough: `AcquireDetailedAsync`'s `HttpRequestException`/`IOException`/timeout-style `OperationCanceledException` catches never attach the original caught exception as `OpenTopographyNetworkException`'s `InnerException` (its `Message` could itself embed the request URI, for example from a caller-attached `DelegatingHandler`). Each instead redacts that exception's `Message` first and rebuilds a new exception of the same runtime type from the redacted text, the same pattern the WKT-parsing catches below already use for `FormatException`/`ArgumentException`.

## Failure taxonomy

| Exception | Trigger |
| --- | --- |
| `OpenTopographyRequestValidationException` | The AOI is not a `Wgs84BoundingBoxAoi`; invalid bounds (defensive — `Wgs84BoundingBoxAoi` cannot itself construct this); approximate area (equirectangular, 111.32 km/degree × cos(mean latitude)) above `MaximumAreaSquareKilometers`; or an HTTP 400 response. |
| `OpenTopographyAuthorizationException` | No API key configured (`ApiKeyMissing`, thrown before any HTTP call); HTTP 401 (`ApiKeyRejected`, or `DatasetAccessDenied` when the body mentions dataset/access/restricted); HTTP 403 (`DatasetAccessDenied`). |
| `OpenTopographyQuotaException` | HTTP 429; or a 401/403/400 body that mentions a rate limit, a limit being exceeded, or a quota. OpenTopography does not document a rate-limit HTTP shape, so this classification is best effort. |
| `OpenTopographyNoDataException` | HTTP 204. |
| `OpenTopographyServerException` | HTTP 5xx. |
| `OpenTopographyNetworkException` | `HttpRequestException`, `IOException`, or a `TaskCanceledException`/`OperationCanceledException` that fires while the caller's own token is **not** cancelled (a timeout). A caller-cancelled token instead propagates as `OperationCanceledException`, unwrapped. |
| `OpenTopographyUnexpectedResponseException` | An undocumented status code (with or without a truncated body); a 200 response whose body is neither a zip archive nor a bare AAIGrid text body; a **200 response** over `MaximumResponseBytes`; or 200 gzip magic bytes (`1F 8B`). A documented non-200 status (401/403/429/400/5xx) that exceeds `MaximumResponseBytes` does **not** raise this exception — see "Response body truncation" below. |
| `OpenTopographySourceMetadataException` | A 200/zip response with no `.prj`/`.aux.xml` sidecar; a bare AAIGrid body with no sidecar at all; more than one `.prj` entry; more than one `.aux.xml` entry; an `.aux.xml` sidecar present but lacking an `<SRS>` element; a blank/whitespace-only coordinate reference sidecar; a zip with valid zip magic bytes that could not be read as a valid archive; a WKT parse failure (including an unsupported unit); WKT with no vertical coordinate system; a parsed coordinate reference system name, horizontal datum, or vertical datum that unexpectedly matches the configured API key (see "Coordinate reference identifiers that echo the API key" below); zero/multiple `.asc` entries; or reference metadata that parsed successfully but whose `.asc` entry content then failed `AaiGridParser.Parse` (for example a malformed header, a wrong `NODATA_value` count, or missing/extra cell values). |

Every message states what was observed (status code, content type, entry names, or the redacted server text) and what the caller can do next. This source never retries with a different dataset or output format and never sends more than one HTTP request per call, on any path, success or failure.

### Response body truncation

`ReadBodyAsync` never throws when a response body exceeds `MaximumResponseBytes`; it returns `(Body, Truncated)` and leaves the decision to its caller, which already knows the response's HTTP status code:

- For a **200 (OK)** response, a truncated body is never parsed as a zip or bare AAIGrid; `HandleSuccessAsync` throws `OpenTopographyUnexpectedResponseException` directly, with no body-derived text in the message.
- For every **documented non-200 status** (401, 403, 429, 400, 5xx), a truncated body does not change which exception type is thrown: the status code is classified exactly as it would be for a complete body, and a fixed note — "(response truncated after it exceeded the configured `<n>` byte limit)" — is appended after the redacted, tag-stripped, bounded server text in `ServerMessage`.
- Only an **undocumented status code** with a truncated body falls through to `OpenTopographyUnexpectedResponseException`.

This keeps a status-appropriate exception (for example `OpenTopographyAuthorizationException` for a 401) from being pre-empted by a generic "unexpected response" purely because a caller-configured `MaximumResponseBytes` happened to be smaller than that response's error body.

Because a truncated body is always read as a byte-for-byte prefix of the real response, a chunk boundary can land in the middle of a key OpenTopography echoes back (see above): `OpenTopographyRedaction.RedactText`'s ordinary whole-value match cannot see a value that was cut in half, so when a non-200 body might be truncated, it additionally strips the longest trailing substring that matches a strict prefix of the key (or its escaped form) *after* applying the ordinary whole-value redaction, closing that gap without weakening redaction of a complete, untruncated body. Running the whole-value redaction first (rather than before, as an earlier revision did) matters for a key with a self-overlapping "border" — a proper suffix equal to a proper prefix, for example a key whose first and last characters match — because a complete, untruncated occurrence of such a key is now always removed as a whole before the trailing-fragment search can mistake its short border for the only surviving fragment. If the truncation cut lands inside a multi-byte UTF-8 character, `Encoding.UTF8.GetString` renders it as a single `U+FFFD` replacement character; the trailing-fragment search excludes a trailing `U+FFFD` from its comparison window (it cannot equal any character of the key) so the genuine key characters that decoded successfully immediately before it are still found and redacted.

## Coordinate reference identifiers that echo the API key

A `.prj`/`.aux.xml` sidecar is server-controlled text, and OpenTopography has been observed to echo a rejected key back in other response shapes (see above), so a WKT coordinate reference system name, datum name, or vertical datum name that unexpectedly equals or contains the configured API key is treated the same as any other untrustworthy echo, not as legitimate metadata. After `WellKnownTextReferenceParser.Parse` succeeds, `ParseZipResponse` checks `Horizontal.CoordinateReferenceSystem`, `Horizontal.Datum`, and `Vertical.Datum` against the key's raw and escaped forms and fails with `OpenTopographySourceMetadataException` (naming only the field, never the value) if any of them match. This runs before the parsed `WellKnownTextReference` can reach the returned `ElevationGrid`'s public `HorizontalReference`/`VerticalReference` — plain records with no `ToString()` override — so a matching value can never reach a caller through the successful, non-throwing acquisition path. It cannot reject legitimate data: a real coordinate reference system or datum name cannot coincidentally equal a random high-entropy API key.

## "Fail rather than assume" metadata policy

AGENTS.md forbids assuming the USGS 1 m mosaic uses the projected CRS and vertical datum documented for the underlying point-cloud collection (for example, `[withheld]`'s UTM/NAD83/NAVD88 metadata). This source therefore never supplies a default horizontal or vertical reference. It reads whatever reference metadata the response actually carries — a `.prj` sidecar's WKT, or an `.aux.xml` sidecar's `<SRS>` element — parses it with the new generic `SolidGround.Core.Metadata.WellKnownTextReferenceParser`, and fails with `OpenTopographySourceMetadataException` when no usable metadata is present, when the WKT does not parse, or when it describes no vertical coordinate system.

**Open question the live test must answer:** the OpenAPI definition and API documentation do not state whether an `AAIGrid` response body is a bare `.asc` text stream or a zip archive containing `.asc` plus a `.prj` (or `.aux.xml`) sidecar, nor what reference metadata (if any) it carries. This source handles both packagings it can conceive of — bare text (which fails, because bare AAIGrid has no reference metadata section) and a zip with a `.prj` or `.aux.xml` sidecar (which succeeds) — but only a real, opted-in call to the live endpoint (see below) can confirm which packaging OpenTopography actually returns for `USGS1m`/`AAIGrid`, and what its sidecar actually contains. Until that test is run against a real account, this remains an open question; if the live packaging differs from both handled shapes, this design note and the source will need a follow-up revision.

## Contract adjustment (deliverable A)

`ElevationSourceMetadata.CollectionPeriod` and `QualityLevel` are now `CollectionPeriod?` and `string?`, defaulting to `null`. The `usgsdem` response carries neither a collection period nor a catalog quality level, and fabricating catalog values (for example, borrowing the point-cloud collection's 2017-02-17..2017-02-27 dates) would violate the same "fail rather than assume" rule. A supplied, non-null `QualityLevel` that is blank or whitespace-only is still rejected. `OpenTopographyUsgs1mSource` always builds `ElevationSourceMetadata("OpenTopography", "USGS1m", null, null)`.

## User secrets deferral

.NET user secrets is a host/CLI concern: it needs a package reference (`Microsoft.Extensions.Configuration.UserSecrets`) and a configured `UserSecretsId`, neither of which belongs in the Revit-free, package-free Core assembly. `IOpenTopographyApiKeyProvider` keeps key resolution pluggable; Core supplies only `EnvironmentOpenTopographyApiKeyProvider` (reads `OPENTOPOGRAPHY_API_KEY`, trimmed, empty treated as absent) and `StaticOpenTopographyApiKeyProvider` (fixed value, for tests and hosts with their own configuration). Reading the key from user secrets is deferred to the CLI issue, which can compose its own provider over `Microsoft.Extensions.Configuration`.

## Running the live test

An opt-in live test (in a separate file, not part of the default offline suite) calls the real endpoint for a small bounding box around the reference parcel scenario (`[withheld], [withheld]`, roughly ±0.0007°) and asserts a successful acquisition. It never prints the key or the request URI. It skips (via `Assert.Skip`) unless **both**:

- the environment variable `SOLIDGROUND_OPENTOPOGRAPHY_LIVE` is exactly `"1"`, and
- `OPENTOPOGRAPHY_API_KEY` is set to a non-empty value.

Example (PowerShell, from the repository root):

```powershell
$env:SOLIDGROUND_OPENTOPOGRAPHY_LIVE = "1"
$env:OPENTOPOGRAPHY_API_KEY = "<your key>"
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release
```

USGS 1 m access requires academic authorization or an enterprise API key; without one, the live test will observe `OpenTopographyAuthorizationException` rather than a successful acquisition even with the opt-in flag set. Do not set `SOLIDGROUND_OPENTOPOGRAPHY_LIVE` or export a real key in CI; the self-hosted workflow runs only the default offline suite.
