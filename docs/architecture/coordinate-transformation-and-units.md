# Coordinate transformation and units

Issue #6 implements `SolidGround.Core.Transformations.ProjNetHorizontalCoordinateTransformFactory` (a managed
WKT1 horizontal-coordinate-transformation adapter), `SolidGround.Core.Transformations.LocalOriginSnapping`,
`SolidGround.Core.Aois.PolygonalRegionReprojection`, and
`SolidGround.Core.Transformations.HorizontalCoordinateTransforms.Reverse` (a generic reversal for any
`IHorizontalCoordinateTransform`, serving `AoiNormalizer`'s projected-to-geographic `parcelToWgs84` seam — see
"Reversing a transform" below), plus three small additive changes: `LengthConverter` gains
`DefaultOutputUnit`, `LocalCoordinateFrame`'s constructor defaults its `outputUnit` parameter to it, and
`WellKnownTextReferenceParser.InterpretLengthUnit` compares against `LengthConverter.MetersPerUnit(...)`
instead of restating the US survey foot and international foot literals.

## Dependency decision, verified 2026-09-19

`src/SolidGround.Core/SolidGround.Core.csproj` adds exactly one new `PackageReference`:
[ProjNET 2.1.0](https://www.nuget.org/packages/ProjNET/2.1.0) (LGPL-2.1-or-later). This is the repository's
**first copyleft dependency** — NetTopologySuite (Issue #5) is BSD-3-Clause. Unmodified binary
`PackageReference` consumption imposes no copyleft obligation on SolidGround's own code, but this is flagged
here for the owner's awareness, exactly as the csproj comment states.

The netstandard2.1 asset (the one net10.0 restores) has zero further dependencies, no `runtimes/` folder, and
no `DllImport` anywhere in the package — confirmed directly from the cached
`~/.nuget/packages/projnet/2.1.0/projnet.nuspec`:

```xml
<dependencies>
  <group targetFramework=".NETStandard2.0">
    <dependency id="System.Memory" version="4.6.0" exclude="Build,Analyzers" />
  </group>
  <group targetFramework=".NETStandard2.1" />
</dependencies>
```

and by inspecting both `lib/netstandard2.0` and `lib/netstandard2.1` folders, each of which contains only
`ProjNET.dll`/`ProjNET.xml` — no native binary. `CoordinateSystemFactory.CreateFromWkt` and
`CoordinateTransformationFactory.CreateFromCoordinateSystems` are the two operations SolidGround uses, matching
AGENTS.md's dependency policy and the operations `docs/architecture/phase-1-contracts.md`'s "Deferred managed
adapters" section anticipated for this package.

**Lock files:** the first `dotnet restore SolidGround.slnx` (no `--locked-mode`) regenerated all three
`packages.lock.json` files — `Core` gains a `"type": "Direct"` `ProjNET` entry, `Cli` and `Tests` each gain a
`"type": "Transitive"` entry plus an updated `solidground.core` project-dependency list — mirroring the
Issue #5/NetTopologySuite precedent recorded in `docs/architecture/phase-1-contracts.md`. A second
`dotnet restore SolidGround.slnx --locked-mode` then verified the regenerated locks are self-consistent.

One clarification for a future implementer comparing hashes by hand: the regenerated lock file's `ProjNET`
`contentHash` does **not** equal the value in `~/.nuget/packages/projnet/2.1.0/projnet.2.1.0.nupkg.sha512` on
this machine. This was checked directly against the pre-existing, already-committed `NetTopologySuite` entry
too — its lock `contentHash` (`1B1OTacTd4QtFyBeuIOcThwSSLUdRZU3bSFIwM8vk36XiZlBMi3K36u74e4OqwwHRHUuJC1PhbDx4hyI266X1Q==`)
likewise does not equal its own local `.nupkg.sha512`
(`+quQX1funVATd8NEZkuDL/m/WvFILKLg55eLZm9TQy4EDo8mPc0teC+snjlwLJfgSxjnNn+dRf15i7cf3l+JJA==`), even though that
pair has been correctly restoring and building since Issue #5. The two hashes are computed differently by
NuGet; only `nuget.org` is configured as a package source in this environment (`%APPDATA%\NuGet\NuGet.Config`),
so this is normal NuGet lock-file behavior, not a substituted or tampered package. `dotnet restore
--locked-mode` succeeding is the authoritative self-consistency check, not a manual hash comparison.

## The WKT flow

Raw WKT reaches `ProjNetHorizontalCoordinateTransformFactory.Create` as a plain `string` parameter, never
through a generic source contract. This is a deliberate scope boundary (see Disagreement 4 in the original
design synthesis): `IElevationSource`, `ElevationAcquisition`, and `ElevationData` still carry **no** generic
raw-WKT field today. Only `OpenTopographyUsgs1mSource.AcquireDetailedAsync()`'s returned
`OpenTopographyUsgs1mAcquisition.Evidence.WellKnownText` carries WKT at all, and it is source-specific and
already redacted (`OpenTopographyRedaction.RedactText(wellKnownText, apiKey, maximumLength: int.MaxValue)` at
the source's own call site) before it is stored. `ParcelGeometryAoi` similarly carries no raw WKT for a
caller-declared parcel CRS beyond its own `HorizontalReference`.

The explicit responsibility boundary this implies: any future caller feeding this factory a real
OpenTopography response's coordinate reference system **must** use the already-redacted
`Evidence.WellKnownText`, never a pre-redaction value. `Create` itself has no API-key concept and performs no
redaction of its own — it is not a safe place to route an unredacted string.

## The ProjNET adapter

`ProjNetHorizontalCoordinateTransformFactory.Create(sourceWellKnownText, targetWellKnownText)`:

1. Parses both strings with SolidGround's own `WellKnownTextReferenceParser`, to obtain a `HorizontalReference`
   for error messages and for the returned `HorizontalTransformationDefinition` (wrapping a `FormatException`
   into `HorizontalCoordinateTransformException`).
2. Parses both strings with ProjNET's `CoordinateSystemFactory.CreateFromWkt`. A full, unmodified compound
   WKT1 `COMPD_CS` string (for example the committed `example-site-synthetic.prj` fixture) returns a
   `ProjNet.CoordinateSystems.CompoundCoordinateSystem`; its `.HeadCoordinateSystem` is the horizontal part
   (`ProjectedCoordinateSystem` for that fixture) and `.TailCoordinateSystem` is the vertical part. ProjNET
   2.1.0 rejects the WKT2 `COMPOUNDCRS` keyword structurally, the same way it rejects `GEOGCRS` (see "ProjNET
   API facts" below) — confirmed directly against the pinned package with the same WKT2 text
   `WellKnownTextReferenceParserTests.ParsesAMinimalWkt2ProjcrsInsideACompoundCrs` uses, for which
   `CreateFromWkt` raises `System.ArgumentException: 'COMPOUNDCRS' is not recognized.`. `COMPD_CS`/`COMPOUNDCRS`
   together describe what SolidGround's own `WellKnownTextReferenceParser` recognizes when building a
   `HorizontalReference`/`VerticalReference` (step 1 above) — a separate, more permissive parse path than
   ProjNET's own WKT1-only `CreateFromWkt`. No substring extraction is needed or performed for the WKT1 case —
   `WellKnownTextReferenceParser` is untouched other than the
   `InterpretLengthUnit` literal de-duplication described below, and `WellKnownTextReference` gains no new
   property. This was independently re-run for this issue (2026-09-19, ProjNET 2.1.0, the actual committed
   fixture text) by reflecting on `CompoundCoordinateSystem.HeadCoordinateSystem`/`TailCoordinateSystem` (both
   typed `CoordinateSystem`) and by building and executing the real transform from the full compound text,
   reproducing every golden value below exactly. That the same transform built from a bare `PROJCS`-only text
   (with the `VERT_CS` part stripped) is bit-identical to one built from the head of the full compound text was
   verified by the orchestrator's own spike and is not independently re-run here, since this issue never
   constructs a `PROJCS`-only variant of the fixture at all.
3. Requires the source to resolve to a `GeographicCoordinateSystem` and the target to resolve to a
   `ProjectedCoordinateSystem` (see "Role contract" below) before calling ProjNET's transform-building API at
   all.
4. Builds the transform with `new CoordinateTransformationFactory().CreateFromCoordinateSystems(sourceCs,
   targetCs)`, takes its `.MathTransform` as `Forward`, and derives `Inverse` with `MathTransform.Inverse()`.
5. Returns an internal `ProjNetHorizontalCoordinateTransform` implementing
   `SolidGround.Core.Transformations.IHorizontalCoordinateTransform`. It is `internal`, so no ProjNET type is
   part of Core's public surface; `ArchitectureTests.ProjNetTypesNeverAppearInAnyPublicCoreSignature` enforces
   this at the assembly-surface level by reflecting over every exported type's public constructors, methods,
   and properties.

`CoordinateOperationDefinition.Definition` always stores the **original, caller-supplied WKT text verbatim**,
never a ProjNET re-serialization: `MathTransform.WKT` throws `NotImplementedException` for the
`ConcatenatedTransform` ProjNET builds for a geographic↔projected pair (confirmed empirically), so
re-serializing was never an option.

Every ProjNET call (`CreateFromWkt`, `CreateFromCoordinateSystems`, `MathTransform.Inverse()`,
`MathTransform.Transform(double, double)`) is wrapped in `catch (Exception ex) when (ex is not
OutOfMemoryException)`, deliberately broader than any specific ProjNET exception type — see "ProjNET API
facts" below for why. No ProjNET exception type and no `null` ever escapes `Create` or the transform it
returns; `HorizontalCoordinateTransformException` always carries the original exception as `InnerException`.

## Role contract: geographic source, projected target

`Create` requires the **source** to resolve to a geographic coordinate system (for example WGS 84 or NAD83)
and the **target** to resolve to a projected coordinate system (for example a raster's own UTM zone), and
rejects any other pairing — including the reverse orientation, and including geographic-to-geographic — with
a `HorizontalCoordinateTransformException` before ProjNET's transform-building API is ever called. Under this
contract, `Forward` always maps `(longitude, latitude)` to `(easting, northing)` and `Inverse` always maps
`(easting, northing)` back to `(longitude, latitude)`. This matches the codebase's own pre-existing
convention: `TerrainProvenance`'s constructor already requires
`horizontalTransformation.TargetReference == localFrame.ProjectedHorizontalReference`
(`src/SolidGround.Core/Provenance/TerrainProvenance.cs`), and `LocalCoordinateFrame` already requires a
**projected** horizontal reference — both only make sense if `HorizontalTransformationDefinition.TargetReference`
names the projected side.

## Reversing a transform: serving `AoiNormalizer`'s projected-to-geographic seam

`AoiNormalizer.NormalizeProjectedParcel` (`src/SolidGround.Core/Aois/AoiNormalizer.cs`, unchanged by this
issue) calls `parcelToWgs84.Forward` on vertices that are already in the parcel's own **projected** reference,
and treats the result as WGS 84 longitude/latitude directly — the opposite of the geographic-source/projected-
target contract fixed above. `Create(parcelWkt, wgs84Wkt)` (the pairing the parameter name `parcelToWgs84`
suggests) is rejected outright by the geographic-source check above, because `parcelWkt` resolves to a
projected system; the only accepted construction, `Create(wgs84Wkt, parcelWkt)`, has `Forward` mapping WGS 84
to the parcel's projected reference — backwards from what `NormalizeProjectedParcel`'s `.Forward` call needs.

`SolidGround.Core.Transformations.HorizontalCoordinateTransforms.Reverse(IHorizontalCoordinateTransform)`
closes this gap generically, for any `IHorizontalCoordinateTransform` — not only the ProjNET-backed one this
issue builds:

- **`Forward`/`Inverse` swap.** The reversed transform's `Forward` calls the original's `Inverse`, and its
  `Inverse` calls the original's `Forward`.
- **`Definition` swap.** The reversed transform's `Definition` is the original's `HorizontalTransformationDefinition`
  with `SourceReference`/`TargetReference` swapped and `ForwardOperation`/`InverseOperation` swapped, built
  through `HorizontalTransformationDefinition`'s own constructor rather than a `with` expression (its
  properties have no `init` accessor, so `with` cannot target them), which means the definition's own
  validation runs again on the swapped values. The engine name and version are unchanged.
- **Involution.** Reversing twice returns a transform whose `Definition` equals the original (record equality)
  and whose `Forward`/`Inverse` delegate to the exact same underlying calls as the original —
  `HorizontalCoordinateTransformReversalTests.ReversingTwiceRoundTripsToAnEqualDefinitionAndBitIdenticalOutputs`
  asserts both halves of this.
- **No engine leakage.** `Reverse` and the internal `ReversedHorizontalCoordinateTransform` it returns never
  reference ProjNET (or any other engine) in a public signature; `ArchitectureTests.ProjNetTypesNeverAppearInAnyPublicCoreSignature`
  covers this new public type automatically, the same reflection sweep that already covers `Create`.

For `NormalizeProjectedParcel`'s seam, the correctly oriented transform is
`HorizontalCoordinateTransforms.Reverse(Create(wgs84Wkt, parcelWkt))`: its `Forward` maps the parcel's own
projected easting/northing to WGS 84 longitude/latitude, exactly what that call site needs — the same
direction as routing a whole region through
`PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Inverse)` (this issue's
other bidirectional helper), just expressed as a reusable transform instead of a one-shot region conversion.

`HorizontalCoordinateTransformReversalTests.NormalizingAProjectedParcelWithAReversedTransformProducesAWgs84EnvelopeContainingEveryOriginalVertex`
is the integration evidence that this seam is actually served end to end: it builds the real ProjNET-backed
transform from `Wgs84WellKnownText` and the committed `example-site-synthetic.prj` fixture, reprojects the
committed `example-site-synthetic-parcel.geojson` parcel Forward into that projected reference with
`PolygonalRegionReprojection`, constructs a projected `ParcelGeometryAoi` from the result, and calls
`AoiNormalizer.Normalize` with `HorizontalCoordinateTransforms.Reverse(transform)` as the injected
`parcelToWgs84` argument. It asserts the call succeeds (no `AoiNormalizationException`), that the resulting
WGS 84 fetch envelope contains every original geographic parcel vertex, and that the envelope is not
absurdly large (each side under 0.01°, for this ~[withheld] m² lot plus a 2 m buffer).

`AoiNormalizer.NormalizeProjectedParcel` itself still performs no validation of `parcelToWgs84.Definition`'s
`SourceReference`/`TargetReference` against the parcel's own declared reference — it only null-checks
`parcelToWgs84` and calls `.Forward`, trusting the caller to supply the correctly oriented transform. Handing
it the un-reversed transform instead does not fail inside ProjNET itself (confirmed empirically with a
throwaway spike, not asserted as a committed test, since the outcome below is an incidental side effect of
unrelated numeric range validation rather than a contract this issue or that seam owns): `Forward` on an
already-projected easting/northing pair, misread as degrees, silently returns some finite but nonsensical
pseudo-coordinate (ProjNET's Transverse Mercator series has no domain check — the fixture's own UTM point
`([withheld], [withheld])` comes back as `(-174844493.76014572, 156563873.98870513)`). Fed onward with a
nonzero buffer or margin, a value that far out of range is large enough to fail `Wgs84Ellipsoid`'s own
latitude-range check while padding the envelope, surfacing as an `ArgumentOutOfRangeException` far from the
real `Forward`/`Inverse` mismatch that caused it — exactly the confusing failure mode the orientation contract
above, and `Reverse` itself, exist to spare a caller from rediscovering by hand.

This restriction is enforced by SolidGround itself, not inherited from a ProjNET failure, because ProjNET
2.1.0 does not reject every misuse of this contract on its own. An independent spike against the pinned
package (2026-09-19, the same fixture and constants this issue uses) found:

- `CreateFromCoordinateSystems(projectedCs, geographicCs)` — the reverse orientation — builds successfully
  (a `ConcatenatedTransform`), with no exception.
- `CreateFromCoordinateSystems(geographicCs1, geographicCs2)` for two **different** geographic definitions
  (differing datum name and ellipsoid, neither with a `TOWGS84`/`TOWGS84`-equivalent clause) also builds
  successfully and transforms a sample point without throwing — it silently performs an ellipsoid-only
  conversion with **no true datum shift**, which would be a misleading, semantically-wrong "transform" under
  SolidGround's Forward/Inverse convention above.
- `CreateFromCoordinateSystems(geographicCs, geographicCs)` for the **same** definition twice (SolidGround's
  own `Wgs84WellKnownText` against itself) likewise builds successfully.

None of these three cases threw in this spike, so SolidGround cannot rely on ProjNET to fail on its own behalf
for a geographic-to-geographic or reversed-orientation misuse; `Create` checks the resolved ProjNET coordinate
system types itself and rejects them with an actionable message naming both coordinate reference systems.
(Separately, and consistent with the exception taxonomy below: reading a parsed `GeographicCoordinateSystem`'s
own `NumConversionToWGS84` property throws `NullReferenceException` when the source WKT has no `TOWGS84`
clause — confirmed directly, and exactly why every ProjNET call is wrapped in a broad `catch (Exception ex)
when (ex is not OutOfMemoryException)` rather than a narrower ProjNET-specific exception list.)

## The hardcoded WGS 84 constant and the NAD83↔WGS84 zero-datum-shift

`ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText` is a hardcoded EPSG:4326 GEOGCS constant with
**no `TOWGS84` clause**, and the committed `example-site-synthetic.prj` fixture's NAD83/UTM-zone-15N definition
likewise has no `TOWGS84` clause. This is accepted as a **non-geodetic** approximation: NAD83 and WGS 84 are
treated as numerically identical for this transform. `AoiNormalizer.cs`'s own pre-existing comment already
establishes precedent for the magnitude of this approximation: "A non-WGS84 geographic datum such as NAD83
differs from WGS 84 by roughly 1-2 m." No vertical transformation is performed or claimed anywhere in this
issue, and `TerrainProvenance`'s own constructor invariant (requiring `sourceVerticalReference ==
localFrame.VerticalReference`) already prevents any code path from smuggling one past provenance.

**Update, Issue #10 (2026-09-19):** a fresh clone on Windows with `core.autocrlf=true` at commit `783e0dd`
failed `TerrainExportGoldenFileTests.GoldenPipelineRendersBytesIdenticalToTheCommittedGoldenFiles`, because the
compiled value of `Wgs84WellKnownText` — then a `public const string` multi-line raw string literal — silently
inherited the source file's own checkout line endings, so a CRLF checkout of
`ProjNetHorizontalCoordinateTransform.cs` baked `\r\n` into the constant even though the committed golden
export was LF-only. A multi-line raw string literal is checkout-dependent for exactly this reason: the
compiler preserves the literal's line endings as written in the source file, and the source file's own line
endings are themselves subject to `.gitattributes` and `core.autocrlf` at checkout time, so the same source
text can compile to two different string values on two different checkouts. The fix changes
`Wgs84WellKnownText` from `public const string` to `public static readonly string`, initialized from the same
raw string literal but piped through `.ReplaceLineEndings("\n")`, so the runtime value is always LF-only
regardless of how the source file was checked out. Two guard tests now cover this: `ProjNetHorizontalCoordinateTransformFactoryTests.Wgs84WellKnownTextHasNoCarriageReturnAndIsMultiLine` asserts the
constant itself contains no `\r` and remains multi-line, and `ArchitectureTests.NoPublicStaticStringInCoreContainsACarriageReturn` guards every public static string in Core against the same class of defect. See
"Determinism rules" in `docs/architecture/provenance-and-deterministic-exports.md` for the platform-neutral
goldens rule this defect motivated.

## `LengthConverter.DefaultOutputUnit`

`LengthConverter.DefaultOutputUnit` is `LengthUnit.UsSurveyFoot`, the one named constant decision #3 requires. It is
used as the default wherever Core chooses an output unit without an explicit caller override — today that is exactly
one place: `LocalCoordinateFrame`'s constructor (`outputUnit = LengthConverter.DefaultOutputUnit`) and nowhere else,
since `LocalOriginSnapping.SnapToWholeSourceUnit` (see below) deliberately does **not** take an output unit at all.
The shipped CLI `--unit` flag (Issue #9) does not read this constant: `ProcessCommand.cs` and `RunCommand.cs` each
fall back to the separately hardcoded string `us-survey-foot` when `--unit` is not supplied, and `OptionTable.cs`'s
`--unit` help text hardcodes the identical string a third time as "(the default)" — none of the three references
`LengthConverter.DefaultOutputUnit`, so `UsSurveyFoot` is restated as a second literal in exactly the way this
constant is meant to prevent. `LengthUnit`'s enum declaration order (`Meter, UsSurveyFoot, InternationalFoot`) is
unchanged.

## `LocalOriginSnapping`

`LocalOriginSnapping.SnapToWholeSourceUnit(Coordinate3D candidate, HorizontalReference
projectedHorizontalReference, VerticalReference verticalReference)` snaps a caller-supplied candidate local
origin to a whole number **in its own source units**, by flooring each axis independently: X and Y floor to a
whole number of `projectedHorizontalReference`'s linear unit (whole metres for a UTM zone), and Elevation
floors to a whole number of `verticalReference`'s unit. There is no `outputUnit` parameter and no unit
conversion inside the method at all — flooring happens directly on `candidate`'s own numbers, because a
`Coordinate3D` flowing into a `LocalCoordinateFrame`'s `Origin` is already expressed in the horizontal and
vertical references' own native units, exactly like every other `Coordinate3D` this issue's tests construct
(for example `CompleteChainRoundTripsFromUtmThroughALocalFrameAndTransformAndBackToUtmWithinTolerance`'s UTM
metre coordinates).

Snapping happens in the **source** coordinate reference's units, not a chosen output unit, because the origin
is carried in provenance in the authoritative source coordinate reference system (the projected horizontal
reference and the vertical reference) — a whole number of source units is exactly representable there and
reads cleanly, with no unit-conversion round trip needed to interpret it later. Flooring (rather than rounding
or ceiling) guarantees that a minimum-corner candidate's local coordinates stay non-negative.
`LocalCoordinateFrame`'s own `ToLocal`/`ToSource` round trip is bit-exact regardless of the origin's value, so
snapping the origin never affects that round trip's exactness — `LocalOriginSnappingTests.
SnappedOriginFeedsLocalCoordinateFrameForABitExactRoundTrip` ties the two together directly, and
`LocalOriginSnappingTests.SnapToWholeSourceUnitProducesAnIntegerValuedDoubleInMetres` separately confirms the
snapped result is a genuine integer-valued double, not merely a value close to one.

`SnapToWholeSourceUnit` is optional and does not choose **which** point becomes the origin (a southwest
corner, a centroid, or any other point remain the caller's choice) — choosing an origin point belongs to
Issue #9's CLI, per AGENTS.md's architecture table.

## `PolygonalRegionReprojection` / `HorizontalTransformDirection`

`SolidGround.Core.Aois.PolygonalRegionReprojection.Reproject(PolygonalRegion region, IHorizontalCoordinateTransform
transform, HorizontalTransformDirection direction)` is the external, bidirectional reader/rebuilder that
`PolygonalRegion.Geometry`'s own doc comment and `GridClipper`'s own exception message ("Transform the region
into the grid's reference first (SolidGround Issue #6)") both anticipate. It copies `region.Geometry`
(`NtsGeometry.Copy()`, never an in-place `Apply`, because `PolygonalRegion.Geometry` is a long-lived, publicly
exposed property other code such as `NormalizedAoi.Parcel` may already hold a reference to), applies the
transform's `Forward` or `Inverse` method to every shell and hole vertex through an `ICoordinateFilter`, and
rebuilds a validated `PolygonalRegion` in the resulting reference via `PolygonalRegion.FromGeometry`.

It does not decide **when** reprojection is needed, and it does not call `GridClipper` — assembling the real
acquire → reproject → clip pipeline is Issue #9's job. `HorizontalTransformDirection` is a plain two-case enum
(`Forward`, `Inverse`) rather than inferring direction by comparing `HorizontalReference`s, because
`HorizontalReference.Datum` is a free-text label: two references describing "the same" coordinate reference
system with a differently-spelled datum will not compare equal. `Reproject` still validates that the region's
own `HorizontalReference` exactly equals the reference the transform expects on the requested side
(`SourceReference` for `Forward`, `TargetReference` for `Inverse`), so callers should derive a region's
reference from the same `WellKnownTextReferenceParser.Parse` call used to build the transform, or the
comparison will spuriously fail — `PolygonalRegionReprojectionTests.
ReprojectsTheRealExampleSiteGeojsonParcelForwardThenInverseRoundTripsToItsOriginalWgs84Vertices` does exactly
that (parsing the parcel's declared WGS 84 reference from the same `Wgs84WellKnownText` constant the
transform's `Definition.SourceReference` uses).

## Round-trip tolerances: numeric reversibility, not survey accuracy

Two separate, explicitly documented tolerance constants live on `ProjNetHorizontalCoordinateTransformFactory`:

```csharp
public static readonly LinearDistance ProjectedRoundTripTolerance = LinearDistance.Meters(0.02);
public const double GeographicRoundTripToleranceDegrees = 2e-7;
```

`ProjectedRoundTripTolerance` bounds a round trip that starts and ends in the **projected** (UTM) reference —
`Forward(Inverse(utmPoint))` — in that reference's own linear unit (metres for the fixture). It replaces an
earlier single `HorizontalRoundTripTolerance = LinearDistance.Meters(1d)` design once a wider measurement sweep
(below) showed a tighter, still generously-margined constant was warranted. `GeographicRoundTripToleranceDegrees`
bounds the opposite-direction round trip, `Inverse(Forward(lonLatPoint))`, in decimal degrees.

**Measured basis** (ProjNET 2.1.0, fixture `PROJCS NAD_1983_UTM_Zone_15N` / `example-site-synthetic.prj`,
2026-09-19). The fixture-scale figures below were independently reproduced against the pinned package for
this implementation; the wider zone-wide sweep (301 points, latitude 0–84° in 2° steps, longitude offsets 0,
±1.5, ±3.0, ±3.5° from the zone's central meridian −93°) is the orchestrator's own reported measurement and
was not independently re-run point-for-point here:

- Round-trip proj→geo→proj (`Forward(Inverse(x))`): max **8.633e-3 m** at the ExampleSite fixture's own footprint
  (six UTM points spanning it — independently reproduced exactly) and **9.292e-3 m** across the zone-wide
  sweep (worst case at latitude 46°, offset −1.5°).
- Geo→proj→geo (`Inverse(Forward(x))`): max **7.776e-8 deg** at the fixture (independently reproduced exactly,
  including against all four real corners of the committed `example-site-synthetic-parcel.geojson` fixture, whose
  own worst case is likewise **7.776e-8 deg**) and **8.362e-8 deg** across the zone-wide sweep (worst case at
  latitude 46°, offset 0°).

Per-10-degree-latitude-band maximum residual for the projected leg (metres), from the same zone-wide sweep
(orchestrator-reported, not independently re-run point-for-point here):

| Latitude band (deg) | Max residual (m) |
| --- | --- |
| 0–10 | 1.933e-4 |
| 10–20 | 1.876e-3 |
| 20–30 | 5.277e-3 |
| 30–40 | 8.482e-3 |
| 40–50 | 9.292e-3 |
| 50–60 | 8.903e-3 |
| 60–70 | 6.073e-3 |
| 70–80 | 2.490e-3 |
| 80–84 | 3.757e-4 |

`0.02 m` and `2e-7 deg` both carry a comfortable margin over every measured value above — about 2.15x the
worst zone-wide projected-leg figure (`0.02 / 9.292e-3`) and about 2.39x the worst zone-wide geographic-leg
figure (`2e-7 / 8.362e-8`) — without being so loose that a real regression (for example an accidental axis
swap, which would fail by many orders of magnitude more than this) would go unnoticed.

**These are numeric reversibility figures for the geographic↔projected leg of the engine, not survey
accuracy.** They bound ProjNET's own forward/inverse floating-point self-consistency for that one leg, nothing
else. `LocalCoordinateFrame`'s own leg (projected source ↔ local) is **bit-exact** and is tested as such
(`ContractModelTests.LocalFrameRoundTripsMixedHorizontalAndVerticalUnits`,
`LocalOriginSnappingTests.SnappedOriginFeedsLocalCoordinateFrameForABitExactRoundTrip` — both assert exact
equality, not a tolerance). Neither tolerance is, or is meant to be compared with, the ~1-2 m NAD83/WGS84
datum approximation above or the ~10 cm QL2 vertical RMSE AGENTS.md's Accuracy section already documents —
SolidGround is a site-form tool, not a survey instrument.

## Independent-engine agreement check

In addition to ProjNET's own golden values (asserted to a tight, same-engine tolerance in
`ProjNetHorizontalCoordinateTransformFactoryTests.ForwardTransformsTheExampleSiteCentroidToItsMeasuredUtmCoordinate`),
`ForwardAgreesWithAnIndependentProjEngineReferenceValue` checks `Forward([withheld], [withheld])` against a
reference value computed by a **completely independent implementation**, PROJ (via `pyproj`'s `Transformer`,
`EPSG:4269 -> EPSG:26915`, `always_xy=True`), on 2026-09-19:

| Engine | Easting (m) | Northing (m) |
| --- | --- | --- |
| PROJ (`pyproj`, EPSG:4269→EPSG:26915) | [withheld] | [withheld] |
| ProjNET 2.1.0 (this adapter, `Wgs84WellKnownText` → fixture) | [withheld] | [withheld] |

The difference is about **4.4 mm**, mostly in northing — plausible given PROJ's reference pair uses the
geodetically-correct NAD83 ellipsoid (GRS80) while this adapter's `Wgs84WellKnownText` constant uses the WGS84
ellipsoid (a sub-millimeter-scale flattening difference from GRS80) under the accepted zero-datum-shift
approximation above, combined with each engine's own Transverse Mercator series-expansion implementation. The
test asserts Euclidean distance within `0.01 m`, comfortably wider than the observed 4.4 mm, and is documented
in the test itself as an independent-engine agreement check — the kind of check that catches an axis swap or a
grossly misread projection parameter, which a same-engine golden value alone cannot catch (a bug that swaps
`Forward`'s inputs or flips a sign would fail this check by many meters, not millimeters). Per the
orchestrator's own zone-wide sweep (same grid as above; not independently re-run against PROJ for this
implementation, since no PROJ/`pyproj` installation is available in this environment), the maximum
ProjNET-vs-PROJ difference across the whole sweep was `4.755e-3 m`, at latitude 46°, offset +3.5° — still
comfortably under the `0.01 m` test bound.

## ProjNET API facts (verified by an independent spike against the pinned package, 2026-09-19)

Confirmed by direct reflection and execution against `~/.nuget/packages/projnet/2.1.0/lib/netstandard2.1/ProjNET.dll`
under net10.0 (0 build warnings):

- `CoordinateSystemFactory.CreateFromWkt(string)` returns `CoordinateSystem`. The assembly carries no
  `Nullable`/`NullableContext` attribute at the assembly level, and `CreateFromWkt`'s own return parameter
  carries neither either (both confirmed by reflection) — the package predates, or was not compiled with,
  C#'s nullable reference annotations. The adapter therefore null-checks its result explicitly rather than
  relying on the compiler's nullable-reference analysis to catch a missed check.
- `CoordinateTransformationFactory.CreateFromCoordinateSystems(CoordinateSystem, CoordinateSystem)` returns
  `ICoordinateTransformation`; `.MathTransform` is a `MathTransform`; `MathTransform.Inverse()` returns
  `MathTransform`.
- The single-point overload used here is `(double x, double y) Transform(double x, double y)` — a
  `ValueTuple<double, double>` — not the array-based `double[] Transform(double[])` overload; for a geographic
  coordinate system, `x` is longitude and `y` is latitude. Repeated calls with identical input are bit-exact
  deterministic (confirmed: `ForwardIsBitExactAcrossRepeatedCalls`).
- `EngineVersion` is the hardcoded literal `"2.1.0"`, never
  `typeof(CoordinateSystemFactory).Assembly.GetName().Version`: the assembly itself reflects as `"2.0.0.0"`,
  which would misleadingly disagree with the pinned NuGet package version
  (`EngineVersionIsThePinnedPackageVersionNotTheReflectedAssemblyVersion` guards this).
- Failure modes wrapped by this adapter: WKT2 text (for example a `GEOGCRS[...]` definition SolidGround's own
  parser accepts) raises `System.ArgumentException` from `CreateFromWkt`, message `"'GEOGCRS' is not
  recognized."`; a syntactically valid but unsupported `PROJECTION["Not_A_Real_Projection"]` parses fine but
  `CreateFromCoordinateSystems` raises `System.NotSupportedException`, message `"Projection
  Not_A_Real_Projection is not supported."`; reading `GeographicCoordinateSystem.NumConversionToWGS84` on a
  system with no `TOWGS84` clause raises `System.NullReferenceException`. None of these is a type the adapter
  lets escape unwrapped — every ProjNET call is wrapped in `catch (Exception ex) when (ex is not
  OutOfMemoryException)`, deliberately including `NullReferenceException`, and rethrown as
  `HorizontalCoordinateTransformException` with CRS-identifying context and the original exception attached as
  `InnerException`.
- Per the orchestrator's own verified spike (not independently re-run for this implementation): GDAL-style OGC
  WKT1 (`AXIS`/`AUTHORITY` nodes, lowercase parameter names, for example `WellKnownTextReferenceParserTests`'s
  `ParsesAnOgcStyleProjcsWithAnAuthorityCode` fixture text) parses to a transform bit-identical to the
  ESRI-style dialect the committed fixture uses; no dialect morphing is performed or needed anywhere in this
  adapter.
- ProjNET 2.1.0 does **not** itself reject a geographic-to-geographic pair or a reversed
  (projected-to-geographic) pair — see "Role contract" above for why SolidGround enforces this itself rather
  than depending on ProjNET to fail on its behalf.

## Boundary: what #7, #8, and #9 still own

| Concern | Owner | Notes |
| --- | --- | --- |
| Wiring `Create`/`PolygonalRegionReprojection`/`LocalOriginSnapping`/`HorizontalCoordinateTransforms.Reverse` into `AoiNormalizer`/`GridClipper`, or the CLI | Issue #9 | This issue adds the building blocks only; no `AoiNormalizer.cs`, `GridClipper.cs`, or `src/SolidGround.Cli/Program.cs` line changes. **See "Reversing a transform" above:** `AoiNormalizer.NormalizeProjectedParcel`'s existing `parcelToWgs84.Forward` call needs the projected-to-geographic direction, served by `HorizontalCoordinateTransforms.Reverse(Create(wgs84Wkt, parcelWkt))` (or, equivalently for a whole region, `PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Inverse)`); `HorizontalCoordinateTransformReversalTests`'s integration test already proves a real transform built this way succeeds end to end for the ExampleSite fixture, but Issue #9 still owns actually constructing and injecting it from the CLI/host. |
| A generic raw-WKT field on `IElevationSource`/`ElevationAcquisition`/`ElevationData`, or on `ParcelGeometryAoi` for a caller-declared parcel CRS | Future issue (not #6) | Deliberately not added — see "The WKT flow" above and Disagreement 4 in the original design synthesis (a generic, always-reachable raw-WKT carrier risked re-exposing a redacted CRS-name substring). |
| Choosing which point becomes a local origin (southwest corner, centroid, or otherwise) | Issue #9 | `LocalOriginSnapping` only snaps a caller-supplied candidate; it never chooses one. |
| Decimation / simplification | Issue #7 | Operates upstream of any local-frame conversion, in source-CRS terms; a NODATA cell structurally cannot become a `TerrainSample`, since `Coordinate3D`'s constructor already requires every ordinate finite (unchanged, pre-existing). |
| Export payload construction | Issue #8 | A one-line `frame.ToLocal(sample.Position)` per retained sample — already exact, already tested by `ContractModelTests.LocalFrameRoundTripsMixedHorizontalAndVerticalUnits`. |
| Provenance schema changes | Not touched by #6 | `TerrainProvenance`, `HorizontalTransformationDefinition`, `CoordinateOperationDefinition`, `HorizontalReference`, `VerticalReference` are all unchanged; `CreatesATransformAndLocalFrameThatTogetherSatisfyTerrainProvenancesInvariants` proves a real `Create(...)` transform and a real `LocalCoordinateFrame` already satisfy `TerrainProvenance`'s existing invariants without any schema change. |
