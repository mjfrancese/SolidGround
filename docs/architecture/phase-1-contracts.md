# Phase 1 public contracts

Issue #2 establishes the Core boundary without selecting a geospatial implementation. The public contracts use only SolidGround types and BCL collections: `AreaOfInterest` has WGS 84 box, radius, and WKT/GeoJSON parcel forms; `IElevationSource` returns abstract `ElevationData`; and `ElevationGrid` and `TerrainSampleSet` are distinct normalized representations. A grid represents every missing cell with `null`, so a source sentinel cannot cross the contract boundary as an elevation.

`HorizontalReference` records whether X/Y ordinates are geographic decimal degrees or a projected linear unit, its explicit axis order, and a required datum. `VerticalReference` separately requires its datum and linear unit. `HorizontalTransformationDefinition` records source and target references plus versioned engine and forward/inverse operation definitions. `LocalCoordinateFrame` accepts only the transformation target's projected horizontal reference, records its vertical reference and source origin, converts horizontal and vertical ordinates through their respective source units, and exposes an inverse mapping into one explicit output unit. Together, the stored inverse operation and local-frame inverse describe the complete local-to-original-source path. U.S. survey foot is exactly `1200 / 3937` meters, while international foot is exactly `0.3048` meters. A parcel buffer is measured in meters only after projection into a suitable metric CRS; it is never applied to angular coordinates. Simplification, provenance, and export are contracts only; this issue does not choose an algorithm, parse a geometry, transform a CRS, or write an export.

The interfaces do not expose types from a geospatial package. That leaves source adapters free to use a grid or a future classified point-cloud representation, while keeping Core callers and offline tests package-neutral.

## Deferred managed adapters

No package reference was added in this issue. An isolated `net10.0` probe confirmed that the following exact packages resolve without native runtime assets; the repository's current Core project remains package-free. They are candidates for later, scoped adapter work:

- [ProjNET 2.1.0](https://www.nuget.org/packages/ProjNET/2.1.0), with its [source repository](https://github.com/NetTopologySuite/ProjNet4GeoAPI), for a managed horizontal-coordinate adapter.
- [NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0), with its [source repository](https://github.com/NetTopologySuite/NetTopologySuite), for robust topology and clipping.
- [NetTopologySuite.IO.GeoJSON 4.0.0](https://www.nuget.org/packages/NetTopologySuite.IO.GeoJSON/4.0.0), with its [source repository](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON), for GeoJSON I/O.

ProjNET is only a candidate for horizontal transformations. Its selection would not demonstrate, provide, or validate a vertical datum transformation. NetTopologySuite and its GeoJSON reader remain deferred to the AOI and clipping issue, where WKT/GeoJSON parsing, holes, multipolygons, buffering, and clipping can be tested together. Any later package addition must pin versions and update lock files.
