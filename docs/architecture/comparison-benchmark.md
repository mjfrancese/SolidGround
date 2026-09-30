# Comparison benchmark

This is the append-only, offline comparison yardstick introduced for Issues #45 and #54. It never fetches data: the reference files remain under git-ignored `artifacts/comparison-reference/`, and a score requires an explicit local invocation.

## Method

`score` triangulates the supplied surface and measures every valid reference-grid cell with barycentric interpolation. It reports maximum absolute and RMS vertical residual, measured cells, and every coverage gap. NODATA cells are not values. A triangle is eligible only when its entire area is covered by the union of valid clipped-cell footprints and all of its vertices belong to one connected support component. This blocks NTS Delaunay's convex-hull bridge across a hole, concavity, or disconnected island.

Scores name one exact report unit: metre = exactly 1 m; U.S. survey foot = exactly 1200/3937 m; international foot = exactly 0.3048 m. A SolidGround bundle is reconstructed through its local frame before scoring. An external `x,y,z` CSV must declare `EPSG`, axis order, horizontal unit, and vertical unit. Its CRS must presently match the reference exactly; the scorer rejects a projected mismatch because Core's managed transform contract does not claim arbitrary projected-to-projected support. A declared vertical-datum mismatch is rejected. An omitted external vertical datum is permitted only as a score-as-is result labeled `undeclared and not aligned`.

## 2026-09-30 initial scorecard

Reference acquisition: `2026-09-30T19:58:50.957Z`; public example site only. No location name, credential, or request URL is recorded here. `reference.asc` SHA-256 `F5D71C8D496628144626BF3588C107D55EFEA5E8476404BB82BD36A5103BD3D5`; `reference.prj` SHA-256 `9423E96198C5EB06477BA59B0C6E758B7D19D06030CA7BB5C80FD5D98BE37D17`; `reference.source.json` SHA-256 `90A7A0D018B07FB4CA9D708C34D79326BFE028EA6FF8D53AF8D83755E1195E5D`.

| Surface | Retained points | Max / RMS vertical error (m) | Compared / uncovered reference cells | Result |
| --- | ---: | ---: | ---: | --- |
| SolidGround curvature-aware, requested 15,000 | 12,099 | 0 / 0 | 12,099 / 0 | The reference contains fewer valid cells than the default budget, so all samples were retained. |
| SolidGround curvature-aware, requested 8,000 | 7,615 | 0.013635253906272737 / 0.0013126492280712805 | 12,099 / 0 | Measured. |
| SolidGround curvature-aware, requested 4,000 | 3,770 | 0.05400828826122961 / 0.0074652241614992535 | 12,099 / 0 | Measured. |
| SolidGround uniform, requested 4,000 | 4,000 | 0.26278076171877274 / 0.016075620270515494 | 12,069 / 30 | Measured; 30 hull-edge gaps are reported, not filled. |
| Groundit | — | not measured | — | Its surface was not exported to a declared CSV outside this repository. |

The seven comparison dimensions from the 2026-09-27 research remain: terrain detail, data-gap safety, model safety, ease of use, beyond-terrain layers, coverage, and automated tests. This first entry provides a reproducible numeric terrain-detail yardstick; it does not claim head-to-head tool equivalence from feature research.

## Watch list (started 2026-09-30)

The owner may use GitHub **Watch → Releases** for Groundit, Mantle Place, BlenderGIS, and Heron. Archi Topography has no public repository, so it needs an owner-selected web-page change monitor. Mantle Place is also watched for its first Revit release. No repository automation, workflow trigger, or account action is created by this note.
