# Parcel and terrain extent Core contract

Date: 2026-09-30. Status: offline Core implementation evidence for Issue #60; this note makes no Revit runtime claim.

`ParcelExtentGeometry` is the opt-in input for a Revit parcel workflow. It carries a projected, unbuffered legal `PolygonalRegion`, a terrain-only horizontal margin, and an optional host-provided minimum legal edge length. Passing it as the final optional argument to `TerrainProcessingPipeline.RunAsync` leaves every existing CLI call unchanged.

The pipeline resolves a `TerrainExtentPlan` in the same local frame as its payload. Its legal region, local legal boundary, legal plane Z, terrain clip region, projected fetch envelope, clip results, and typed warnings are bounded Core values. The legal boundary comes from the original parcel's rings, holes, and polygons. `LegalBoundaryFactory` removes only exactly coincident vertices and vertices exactly between collinear neighbors; it rejects a separately supplied short edge rather than moving, snapping, or buffering it. The Revit host remains responsible for its API-specific loop validation.

For the opt-in path, southwest and centroid origins derive from the legal parcel's unbuffered envelope before clipping. Explicit origins remain exactly as supplied. Legal plane Z is the minimum valid elevation from the unsimplified legal-area clip. If that clip has no valid sample, `ParcelExtentPlanningException` names the pre-transaction failure; it never borrows an elevation from the terrain margin. A positive margin must fit inside the elevation grid's finite cell-corner envelope before terrain clipping; otherwise the same named exception blocks creation.

The plan emits typed counts for legal multipart geometry, legal holes, topology changes introduced by buffering, and legal/terrain NODATA cells. NODATA stays absent data throughout; no elevation is synthesized for a legal plane or terrain sample.

Offline evidence is in `TerrainProcessingPipelineTests` and `LegalBoundaryFactoryTests`. It covers unbuffered origin/boundary invariance across zero, one, three, and six-cell margins with both a fully retained and fixed point budget; source-coordinate reconstruction of a legal local vertex; multipart/hole and NODATA warning counts; uncovered-margin and no-valid-legal-sample failures; and lossless collinear cleanup/short-edge rejection. These synthetic checks do not observe a Revit toposolid, PropertyLine, or surface elevation. The planned Revit 2027 edge experiment and its measurement method remain required before any edge-quality or host-shape claim.
