# Projection characteristics and State Plane process fixture

Date: 2026-09-30
Issues: #49 (PH4-7), #50 (PH4-8)

## Public State Plane fixture

`tests/SolidGround.Tests/Fixtures/nad83-texas-south-central-synthetic.prj` is the
NAD83 Texas South Central SPCS83 zone, FIPS 4204, represented in metres as
EPSG:32140. It contains no address, parcel, customer data, or downloaded terrain.
The matching `.asc` is a wholly synthetic 3-by-3 AAIGrid whose coordinates are
ordinary projected test ordinates.

The authoritative defining-parameter source is the National Geodetic Survey's
[NOAA Manual NOS NGS 5, *State Plane Coordinate Systems of 1983*](https://geodesy.noaa.gov/library/pdfs/NOAA_Manual_NOS_NGS_0005.pdf),
Texas South Central zone 4204, retrieved 2026-09-30. Its table identifies the
Lambert projection and gives the two standard parallels (28 degrees 23 minutes
and 30 degrees 17 minutes), central meridian (99 degrees west), latitude of
origin (27 degrees 50 minutes north), false easting (600,000 m), and false
northing (4,000,000 m). Those values are transcribed to decimal degrees in the
fixture; its GRS 1980 ellipsoid is the standard NAD83 ellipsoid.

The fixture is deliberately a horizontal `PROJCS`, so the offline `process`
test supplies `--vertical-datum NAVD88 --vertical-unit meter` explicitly. That
labels elevations for the synthetic grid; it does not perform, imply, or test a
vertical datum transformation.

ProjNET 2.1.0 remains the only horizontal-transformation dependency. The
fixture's end-to-end test runs `process`, reads its real export and points file,
reconstructs projected source coordinates from the local frame, and applies the
recreated transform through WGS 84. Each retained point's projected residual is
bounded by the existing UTM `ProjectedRoundTripTolerance` of 0.02 m. This is
numeric reversibility of the pinned engine and export frame, not geodetic or
survey accuracy.

Like the existing plain-NAD83 UTM fixture, this definition contains no
`TOWGS84` operation. SolidGround therefore applies its documented zero-datum-
shift NAD83-as-WGS84 approximation unchanged: it is a non-geodetic convenience
for the horizontal projection leg, not a claimed NAD83-to-WGS84 realization
transformation. The fixture is plain NAD83, not NAD83(2011), so it introduces no
additional realization-specific interpretation. See
`coordinate-transformation-and-units.md` for the established caveat and its
accuracy limits.

## Core measurement contract

`ProjectionCharacteristics.Measure(IHorizontalCoordinateTransform,
LocalCoordinateFrame)` is a Revit-free, transform-generic function. It reverses
the local frame's projected origin through the supplied transform to obtain its
longitude/latitude, then uses centered one-metre and half-metre horizontal
stencils through `Forward`, rejecting a material derivative change when the
stencil is halved. Both target-coordinate differences are converted from the target
CRS's declared linear unit to metres before the Jacobian is formed. This keeps
scale dimensionless for metre and foot projected CRSs.

`GridConvergenceRadians` is the clockwise signed rotation from true north to
grid north. `PointScaleFactor` is the average of the east and north Jacobian
norms only after the function confirms that the two agree within 1e-5; it
rejects a non-finite, zero, or non-conformal derivative rather than fabricating
a single scale factor. The function also requires a longitude-latitude WGS 84
(EPSG:4326) source, a projected target, and exact target/local-frame reference
agreement.

The offline cross-checks are independent of the numerical stencil:

- At UTM zone 15's central meridian, the output is convergence 0 and scale
  0.9996, the defining UTM central-meridian scale.
- One degree east of that meridian, the output matches
  `atan(tan(deltaLongitude) * sin(latitude))` for convergence.
- At the public LCC fixture's southern standard parallel, the output has unit
  scale and matches the analytic ellipsoidal two-standard-parallel LCC
  convergence `n * (longitude - centralMeridian)`.

The Revit host must call this only after the acquisition path has resolved and
validated authoritative CRS metadata (GeoTIFF metadata for `fetch`, the parsed
sidecar for `process`) and display the returned record in its post-acquisition
summary/log. No `ProjectPosition.Angle` write follows from this measurement: the
accepted zero-angle north-lock remains unchanged. A real-Revit display session
is still pending host integration and is not established by these offline tests.
