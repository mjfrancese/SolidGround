# Building outlines and grade-point selection for a front-door datum

**Date:** 2026-10-01

**Status:** research and proposed product direction. This note authorizes no implementation, data download, live query, Revit API call, dependency, or hosting deployment.

## Decision

Give the normal operator an automatic, no-file-preparation building-outline preview. The proposed future default is the current Microsoft Global ML Building Footprints release, fetched as a few versioned public tiles. An outline is visual navigation context for an exterior ground pin. It is never a legal boundary, parcel boundary, terrain AOI, foundation perimeter, door location, floor-height input, or survey result.

An outline improves the question the operator can answer: "Where should I put the exterior ground pin beside the entrance?" It cannot answer "Where is the front door?" or "What is the first-floor elevation?" Imagery-derived geometry can be an outer footprint or roofprint; eaves, garages, porches, carports, canopy, current construction, and stale imagery can displace it from the foundation. [Overture's Buildings guide](https://docs.overturemaps.org/guides/buildings/) calls its geometry an outer footprint and possibly a roofprint. Microsoft calls its data ML-detected footprints from imagery. Neither provides door data.

The automatic fetch is location disclosure. Before it runs, the dialog must say which source will receive selected location context, why, and what is sent. Client-direct Global ML retrieval requests an L9 quadkey tile, revealing a coarse area rather than an address or parcel ID. A future county or SolidGround endpoint may receive a small bbox. Research, offline fixtures, logs, and documentation must contain no known private home, address, parcel, or building geometry. Query logs remain redacted.

## Recommended operator flow

1. After address and parcel confirmation, show a north-up plan with the confirmed parcel, terrain context, and muted building outlines. Show source, release/vintage where known, licence/attribution, and: "Outlines are approximate visual context, not legal or surveyed building boundaries."
2. Ask the operator to put a **front-door ground pin** outside the outline, beside the actual doorway. A pin inside an outline gives a non-blocking warning and can be moved. Typed coordinates are an equal alternative.
3. Sample SolidGround's bare-earth DEM at the pin. Ask for measured vertical rise from exterior ground to the **unfinished first-floor datum**, the owner's requested default. Describe the physical reference as top of the subfloor or structural slab. An explicit alternative may identify a different reference, but the product must never silently substitute a finished surface or door threshold. Show:

   ```text
   provisional unfinished-first-floor elevation = DEM ground elevation at pin
                                                  + operator-measured rise
   ```

   The result is a site-form estimate, not foundation-perimeter grading or a survey. A DEM can be dated or interpolated under vegetation and is not a contemporaneous wall measurement.
4. Record pin coordinate, DEM sample and vertical reference, entered rise and unit, chosen physical floor reference, calculated provisional elevation, outline-source/release metadata, and operator confirmation in provenance. Label it `provisional-operator-measured`.
5. Provide a distinct later action to replace the provisional datum with a surveyed elevation. It requires elevation, vertical datum, unit, source/measurement description, date, and acknowledgement if conversion is impossible. Preserve the original estimate and audit trail. Building outlines do not validate survey annotations.

The dialog must be keyboard usable. Tab order reaches source notice, outline toggle, pin mode, coordinates, measured-rise field, and confirmation; arrow keys nudge a focused pin by a disclosed increment; Escape cancels placement; and accessible names announce units, pin status, and the inside-outline warning. Missing outlines leave the parcel-and-terrain pin flow usable. An existing Revit building or temporary sketch can be optional visual context, never a requirement.

The installed Revit 2027 `RevitAPI.xml` (27.0.10.13, inspected 2026-10-01) documents `GeometryInstance.GetInstanceGeometry()` as model-coordinate geometry and `Face.GetEdgesAsCurveLoops()` as closed edge loops. This supports a later, separately designed existing-element fallback; it does not recommend adding a Revit API call now.

## Best automatic source: Microsoft Global ML tiles

The current official [Global ML Building Footprints repository](https://github.com/microsoft/GlobalMLBuildingFootprints) describes 1.4 billion imagery-detected buildings, a 2026-08-13 release with 30,340 tiles across 225 regions, and a release-specific [`dataset-links.csv`](https://bfppub.blob.core.windows.net/%24web/2026-08-13/dataset-links.csv). Each manifest row maps a region and **level-9 quadkey** to a public `.csv.gz` URL whose content is line-delimited GeoJSON. It is not a whole-state file and needs neither an API key nor a geocoding request.

The retrieved 2026-08-13 manifest is 6,382,043 bytes and its 30,340 rows all have nine-character quadkeys. Its reported compressed sizes show both why client-direct retrieval is practical for many small parcels and why it needs a hard cap:

| 2026-08-13 manifest result | Reported value |
| --- | ---: |
| Global tile count | 30,340 |
| United States tile count | 2,415 |
| United States compressed total | about 13.4 GiB |
| United States tile median | about 1.4 MiB |
| United States tile 90th percentile | about 13.6 MiB |
| United States tile 99th percentile | about 63.3 MiB |
| Largest United States tile | about 171.6 MiB |

These are aggregate metadata calculations from the provider's manifest, not real-property queries. A small parcel normally intersects one L9 tile and can intersect up to four at a tile boundary. Apply a per-tile and total compressed-byte cap before downloading. A proposed initial cap of 25 MiB per tile and 40 MiB total makes median cases quick but purposely declines dense or boundary cases; the UI then says "outline unavailable for this source size" and continues without one. Do not solve a cap failure by silently downloading a whole state or adding a paid source.

The client path can remain managed .NET 10: compute L9 quadkeys from the confirmed display bbox, look up release URLs in a pinned manifest index, use `HttpClient` and `GZipStream`, parse GeoJSON lines with `System.Text.Json`, and use the existing managed NetTopologySuite geometry capability to retain only polygons intersecting the display bbox plus a small fixed margin. Geometry is transient by default. A reviewed Core geometry adapter can own parsing and clipping; no Revit API, Python, GDAL, or native geospatial executable belongs in that path.

The source descriptor needs release date, manifest URL and digest, tile URL template/index, licence URL/text, required attribution, and retrieval date. A future release update is a reviewed source-version update, never an invisible URL switch.

### Accuracy limits

Global ML footprints are visual context only. The provider says confidence is building-detection confidence, not height confidence. Its height is an imagery-derived average over a polygon, with `-1` when absent; it must never feed the first-floor formula. The current documentation's evaluation tables are regional and do not provide a U.S.-specific 2026 outline-IoU result. Its older North America false-positive estimate is 1% from a 4,000-structure sample in October 2022, which is neither a current U.S. accuracy guarantee nor a floor/door measurement. Image vintage varies; record release and supplied metadata but never infer construction date or survey quality.

## Licence and attribution distinction

The Global ML release is currently licensed by Microsoft under **CDLA Permissive 2.0**, not ODbL. The repository README and current LICENSE were updated in 2026 and are controlling evidence for this recommendation. SolidGround should visibly credit Microsoft, show the licence, and carry both through exports/provenance. CDLA-Permissive does not impose ODbL's share-alike/public-derived-database-access condition; that obligation must not be incorrectly attributed to these Global ML tiles.

By contrast, Microsoft's older [US Building Footprints](https://github.com/microsoft/USBuildingFootprints) release is ODbL and static state GeoJSON, which makes it a poor direct runtime source. ODbL permits commercial use with its conditions. If SolidGround ever distributes a public cache or derived database from that data, OSM, or Overture Buildings, the product design must provide required attribution, ODbL/compatible licensing, and public derivative-database access. This is a compliance and delivery requirement, not a blanket rule against an ODbL default.

Current [Overture Buildings](https://docs.overturemaps.org/guides/buildings/) is ODbL because it includes OSM. Its Explorer can download visible data as GeoJSON, but documentation does not expose a stable product API for bounded GeoJSON retrieval. Documented programmatic bbox access is its Python client; direct cloud access is GeoParquet through DuckDB. That fails the repository's managed-only, no-Python/no-native boundary today. It remains a potential source only if a compliant managed ingestion or owned service is designed.

OSM itself is ODbL and can be commercially used under its conditions, but a public Overpass instance is a separate operating constraint: its guidance lists using it as an application backend as problematic, can load-shed, and directs higher demand to an operator's own service. No public Overpass endpoint is the shipped default. This is not a licence prohibition.

## Deployment choices after the direct-tile prototype

The direct Global ML path is the fastest route to dead-easy automatic outlines, but its L9 tile-size tail makes a product delivery decision necessary. Keep two explicit future choices:

| Choice | Operator experience | Product operations and compliance |
| --- | --- | --- |
| **Client-direct versioned Global ML tiles** | No setup or file preparation. Fast when the capped tile set fits. Large/multiple tiles yield no outline while the datum flow continues. | Pin and periodically review the public manifest/release. Each machine contacts Microsoft's static host with coarse tile location. No SolidGround data service is operated. Carry Microsoft/CDLA release provenance. |
| **SolidGround bounded feature endpoint or static fine-tile CDN** | Always returns only parcel-context polygons, so dense L9 tiles do not create a large client download. No user file handling. | Separate hosting decision: ingest a pinned release outside Revit; publish versioned small tiles or a bbox endpoint; cap requests; retain source version/attribution; monitor cost, availability, privacy notice, and deletion/update policy. Current Global ML/CDLA can support this without ODbL derivative-database-access obligations. If an ODbL source is later blended in, expose the derivative database under required ODbL-compatible terms and make it publicly accessible as required. |

This is an architecture choice, not a request to deploy a service now. Future work should measure coarse-tile cap failures on synthetic or authorized public test areas before choosing it. It must not use real private-home fixtures.

## Other sources and fallbacks

| Candidate | Value | Constraint and disposition |
| --- | --- | --- |
| Configured county ArcGIS Feature Service | A locally authoritative layer can have better geometry and freshness. Esri's feature-layer `Query` supports envelope geometry, spatial relation, and `f=geojson` when the layer supports that format. | Optional after exact publisher, terms, licence, attribution, request cap, and owner acceptance are recorded in the machine-local registry. Public visibility does not itself authorize automated query, caching, redistribution, or derivation. Never a silent default. |
| Advanced local GeoJSON | Useful where an organization already holds an authorized county or purchased dataset. | Advanced fallback only; no manual preprocessing in the normal flow. Preserve supplied licence/attribution in exports. |
| Microsoft US Building Footprints | National, no key, historical static state GeoJSON. | ODbL and not bbox-deliverable. Legacy/import provenance, not normal runtime acquisition. |
| OSM via public Overpass | Keyless, bounded geometry response. | Do not make a public community instance the product backend; a later owned endpoint has distinct operational and ODbL design requirements. |
| Overture Buildings | Strong global conflation and broad coverage. | No current product-ready bounded retrieval within managed-only/no-native constraints; Explorer visible downloads are not an integration contract. |
| USGS 3DEP/lidar | Useful site-elevation evidence. USGS says lidar starts as points from structures, vegetation, and ground, while bare-earth DEM creation removes structures and vegetation. | Not a building-footprint service. Point clouds do not provide a door or trustworthy footprint without a separate point-cloud product, outside scope. |

## Validation boundaries

* The outline never changes confirmed parcel, property line, DEM AOI, local origin, or terrain budget.
* Clip for display only after preserving source/release provenance. Do not imply that clipped outline is a legal building boundary.
* Reject invalid geometry and excessive feature counts. Fetch/parse/clip failure is a normal no-outline result before a terrain transaction.
* Do not use outline height, confidence, construction/vintage inference, or nearest polygon to fabricate a door, grade, vertical datum, or floor datum.
* Use invented geometry for fixtures. Validate source notice, automatic tile selection, transfer caps, no-outline recovery, outside-pin warning, keyboard interaction, provenance, and later surveyed-datum replacement separately.

## Evidence and retrieved sources

All web material below was retrieved on 2026-10-01 with Firecrawl and saved in ignored `.firecrawl/`. Saved copies are research evidence, not distributable dataset assets.

| Source | Saved retrieval |
| --- | --- |
| [Microsoft Global ML Building Footprints README](https://github.com/microsoft/GlobalMLBuildingFootprints) | `.firecrawl/source-microsoft-global-ml-building-footprints.md` |
| [Microsoft Global ML LICENSE](https://github.com/microsoft/GlobalMLBuildingFootprints/blob/main/LICENSE) | `.firecrawl/source-microsoft-global-ml-building-footprints-license.md` |
| [Global ML 2026-08-13 tile manifest](https://bfppub.blob.core.windows.net/%24web/2026-08-13/dataset-links.csv) | `.firecrawl/source-microsoft-global-ml-dataset-links.csv` |
| [CDLA Permissive 2.0](https://cdla.dev/permissive-2-0/) | `.firecrawl/source-cdla-permissive-2-0.md` |
| [Overture Buildings guide](https://docs.overturemaps.org/guides/buildings/) | `.firecrawl/source-https_3A_2F_2Fdocs.overturemaps.org_2Fguides_2Fbuildings_2F.md` |
| [Overture data access guide](https://docs.overturemaps.org/getting-data/) | `.firecrawl/source-https_3A_2F_2Fdocs.overturemaps.org_2Fgetting-data_2F.md` |
| [Overture attribution guidance](https://docs.overturemaps.org/attribution/) | `.firecrawl/source-overture-attribution.md` |
| [Microsoft US Building Footprints README](https://github.com/microsoft/USBuildingFootprints) | `.firecrawl/source-https_3A_2F_2Fgithub.com_2Fmicrosoft_2FUSBuildingFootprints.md` |
| [Microsoft US data licence](https://github.com/microsoft/USBuildingFootprints/blob/master/LICENSE-DATA) | `.firecrawl/source-https_3A_2F_2Fgithub.com_2Fmicrosoft_2FUSBuildingFootprints_2Fblob_2Fmaster_2FLICENSE-DATA.md` |
| [ODbL 1.0 legal code](https://opendatacommons.org/licenses/odbl/1-0/) | `.firecrawl/source-https_3A_2F_2Fopendatacommons.org_2Flicenses_2Fodbl_2F1-0_2F.md` |
| [OpenStreetMap copyright and licence](https://www.openstreetmap.org/copyright) | `.firecrawl/source-https_3A_2F_2Fwww.openstreetmap.org_2Fcopyright.md` |
| [Overpass public-instance guidance](https://dev.overpass-api.de/overpass-doc/en/preface/commons.html) | `.firecrawl/source-https_3A_2F_2Fdev.overpass-api.de_2Foverpass-doc_2Fen_2Fpreface_2Fcommons.html.md` |
| [ArcGIS feature-layer query reference](https://developers.arcgis.com/rest/services-reference/enterprise/query-feature-service-layer/) | `.firecrawl/source-esri-query-feature-service-layer.md` |
| [USGS lidar FAQ](https://www.usgs.gov/faqs/what-lidar-data-and-where-can-i-download-it) | `.firecrawl/source-https_3A_2F_2Fwww.usgs.gov_2Ffaqs_2Fwhat-lidar-data-and-where-can-i-download-it.md` |
| [USGS 3D Elevation Program](https://www.usgs.gov/3d-elevation-program) | `.firecrawl/source-https_3A_2F_2Fwww.usgs.gov_2F3d-elevation-program.md` |

Local Revit evidence is `C:\Program Files\Autodesk\Revit 2027\RevitAPI.xml`, version 27.0.10.13, members `GeometryInstance.GetInstanceGeometry()` and `Face.GetEdgesAsCurveLoops()`, inspected 2026-10-01.
