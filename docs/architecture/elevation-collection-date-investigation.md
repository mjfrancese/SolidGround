# Elevation collection-date investigation

**Issue:** #47 (PH4-5, investigation half only)
**Date:** 2026-09-30
**Status:** Decision memo; no collection-date implementation is authorized by this note.

## Decision

Do **not** add an automatic `/otCatalog` request to `OpenTopographyUsgs1mSource`, and do not populate `ElevationSourceMetadata.CollectionPeriod` from it. For a USGS 1 m `usgsdem` acquisition, the source-reported collection period remains explicitly unavailable. A later schema/provenance decision may make that unavailability more visible than today's nullable representation, but it must not invent a date or infer one from an HTTP `Date` header.

This is a safety decision, not a finding that OpenTopography has no date information. The catalog can expose project-level temporal coverage, but the current public contract does not bind one of those catalog projects to the pixels returned by a particular `usgsdem` raster request.

## Evidence

OpenTopography's current [OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json), retrieved 2026-09-30, documents these separate operations:

| Operation | What the documented contract establishes | Collection-date consequence |
| --- | --- | --- |
| `GET /API/usgsdem` | Requires `datasetName`, WGS 84 bounds, output format, and `API_Key`; `USGS1m` is a permitted dataset name. Its documented parameters and response descriptions include no catalog-project identifier or collection-period field. | The two requests SolidGround already makes for AAIGrid data and matching GeoTIFF GeoKeys cannot identify a collection project or its survey dates. |
| `GET /API/otCatalog` | A separate bounding-box or polygon catalog search. `productFormat` can be `PointCloud` or `Raster`; `detail` requests detailed metadata; `include_federated` includes federated datasets such as the USGS 3DEP Catalog. No API-key parameter or rate/quota statement is documented for this operation. | Any use in the acquisition path is a distinct, third live request. The specification does not establish that it is quota-free, nor does it connect an individual result to an `usgsdem` response. |

The current [USGS 1 meter DEM collection record](https://portal.opentopography.org/datasetMetadata?otCollectionID=OT.012021.4269.3) labels USGS 1 m as a project-based lidar DEM and publishes one collection-wide `Survey Date` range (2015-05-30 through 2025-02-07 as retrieved). That range is metadata for the entire nationwide collection, not an AOI-specific date. The collection page also says one-meter surfaces are seamless within collection projects but not necessarily across projects. It therefore cannot safely be attached to every returned AOI as its collection period.

A read-only `otCatalog` request using only the OpenAPI specification's public example bounds, with `productFormat=PointCloud`, `detail=true`, and `include_federated=true`, was made solely to inspect the current public response shape. It returned multiple records, including federated `USGS_3DEP_ID` point-cloud projects, each with a distinct `temporalCoverage` range and its own spatial coverage. This confirms that detailed catalog results can contain date ranges. It does **not** prove which record, if any, supplied an overlapping USGS 1 m raster pixel. The observation used no API key and no operator or customer AOI; it is not treated as a stable API schema beyond the OpenAPI contract.

`dateCreated` in such catalog records is catalog-record creation metadata, distinct from `temporalCoverage`; it must not be substituted for an acquisition/survey period.

## Why an automatic third call is unsafe

1. It exceeds the established two-request acquisition contract. The OpenAPI definition documents `/otCatalog` as a separate endpoint, and says nothing that lets the existing AAIGrid or GeoTIFF response carry the catalog result forward.
2. A bounding-box search can return several overlapping project polygons and several date ranges. The public catalog response has the project IDs and spatial coverage needed to describe candidates, but the `usgsdem` contract exposes no reciprocal project ID, tile ID, or provenance field that proves the generated raster used a particular candidate.
3. Recording the earliest, latest, or union of catalog candidates would describe the search result, not an established property of the returned raster. That violates the repository's fail-rather-than-assume rule.
4. The catalog contract does not document whether calls are metered or rate-limited. Adding it to every fetch would create an unquantified availability and quota dependency even if no `API_Key` is supplied.

## Provenance guidance and follow-up

For now, retain `collectionPeriod: null` / `HasCollectionPeriod: false` as the existing explicit optional-state representation. If the owner wants a more legible value, scope a separate schema-versioned change whose text means **"not reported by this source response"**; it must remain distinguishable from a known date range and from an operator assertion.

A future collection-date feature would need primary documentation or a source response that supplies a stable identifier linking the exact `usgsdem` raster to exactly one project (or a formally defined multi-project composition rule), plus a separately reviewed third-request/quota policy. Neither condition is established by the evidence above.

The owner has not made that follow-on decision. This memo opens no implementation work and makes no source-code or schema change.

## Sources

- [OpenTopography API OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json), retrieved 2026-09-30.
- [OpenTopography USGS 1 meter collection metadata](https://portal.opentopography.org/datasetMetadata?otCollectionID=OT.012021.4269.3), retrieved 2026-09-30.
- Existing source contract and fail-rather-than-assume policy: `docs/architecture/opentopography-usgs1m-source.md`, especially its collection-period statement.
