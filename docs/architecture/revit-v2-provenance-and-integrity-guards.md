# V2 provenance and terrain integrity guards

Issues #43, #44, and #48 add a single, self-contained Extensible Storage v2 entity and fail-closed
integrity checks around creation. The existing v1 schema (`bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e`) remains
immutable and readable; new writes use only v2.

## Revit 2027 API evidence

Before implementation, the installed Revit 2027 API XML at
`%ProgramFiles%\Autodesk\Revit 2027\RevitAPI.xml` (API 27.0.10.13) was inspected on 2026-09-30. It
contains these exact members used by this implementation:

- `P:Autodesk.Revit.DB.SlabShapeVertex.Position` and
  `P:Autodesk.Revit.DB.SlabShapeVertexArray.Item(System.Int32)`;
- `M:Autodesk.Revit.DB.FilteredElementCollector.#ctor(Autodesk.Revit.DB.Document)`;
- `P:Autodesk.Revit.DB.Document.CreationGUID` and `P:Autodesk.Revit.DB.Element.UniqueId`;
- `M:Autodesk.Revit.DB.ExtensibleStorage.SchemaBuilder.SetReadAccessLevel(Autodesk.Revit.DB.ExtensibleStorage.AccessLevel)`,
  `.SetWriteAccessLevel(...)`, and `.AddSimpleField(System.String,System.Type)`.

The Revit project compiled against those installed assemblies after the calls were added. This establishes
availability and accessibility, not runtime behavior.

## Per-vertex verification (#43)

`PostCreationVerification` reads every `SlabShapeVertex.Position` and uses the Core
`TerrainVertexMatcher`. It finds a tolerance-bounded bipartite assignment from expected input points to
actual vertices. An actual vertex can satisfy at most one expected point; extra actual vertices remain
allowed for profile-generated geometry. The result identifies the first unmatched expected index, its nearest
observed vertex, and its XYZ delta. A count/bounds pass cannot mask a swapped or shifted vertex.

A null bounding box is only an informational result: matching continues. A disabled slab-shape editor is a
failure because no per-vertex proof can then be produced. Any failed check returns the existing failed
`VerificationResult`, so the caller rolls back the same transaction.

## Schema v2 (#44 and #48)

V2 is `SolidGround_Provenance_Toposolid_V2`, GUID
`a1ca95f5-2bf4-4b9b-a7f1-2b8b4f0fcd3a`, version `2`, with `AccessLevel.Public` read and
`AccessLevel.Vendor` write. It has 63 fields, below Revit's 256-field ceiling:

- all 36 v1 fields, including build identity and reversible local frame;
- the 17 address/parcel and source-attribution fields specified in
  `address-parcel-provenance.md`;
- `coverageFloorFraction` (Number spec) and `collectionPeriodAvailability`;
- `terrainIdentityVersion`, `terrainIdentityKind`, `terrainIdentityStem`,
  `terrainContentSignatureAlgorithm`, `terrainContentSignature`, `terrainPointFrameHash`,
  `storedOriginalUniqueId`, and `storedOriginalDocumentCreationGuid`.

`ExtensibleStorageProvenanceValuesV2.From` composes the existing v1 `From` result rather than duplicating
its unit and reference conversion. The writer does four checks before commit: entity GUID recognition,
entity validity/read access, every typed field read back, and reconstructed first-sample source coordinates.
It stores only the existing provenance address/parcel identity and attribution values; API keys, headers, and
request URLs have no v2 field.

## Duplicate guard (#44)

`TerrainIdentity` hashes canonical non-secret material. A stable parcel uses source identity plus its durable
parcel id. Bbox, radius, and raw-polygon callers must pass a visible fallback kind and canonical AOI material;
the entity records the kind instead of silently omitting the guard. Content and point/frame hashes exclude
lookup dates and UI text and include the final expected point set and frame.

`ExistingTerrainScanner` is read-only and must run after the pipeline makes the point/frame hash available,
before opening a transaction. It returns plain Core decision data. A malformed/unreadable v2 or v1 entity
throws a named scan exception and is never skipped. V1 entities require explicit current-run acknowledgement.
For v2, no match creates, one fully equal same-history match reuses, and any changed content, point/frame,
document creation GUID, unique id, or more than one matching stem refuses. Save As retains history only if the
creation GUID, current unique id, and all signatures still agree.

## Pending manual evidence

Offline tests and a local Revit-assembly compilation cover the Core and API-access contract. A Revit 2027
manual session is still required to show a normal creation passes the vertex matcher, an intentionally
corrupted vertex rolls back, v2 survives save/reopen, and repeat/copy/stale/collision flows show the expected
decision without creating an unwanted terrain.
