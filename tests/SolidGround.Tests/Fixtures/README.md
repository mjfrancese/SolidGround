# Offline raster fixture

`robandee-synthetic.asc` is a deliberately small synthetic AAIGrid near the projected coordinates of the Robandee Lane test scenario. Its elevations are illustrative parser inputs, not measured terrain and not an OpenTopography response. The fixture contains one explicit NODATA cell and no request URL, authorization header, or API credential.

`robandee-synthetic.prj` is a hand-written, ESRI-style `COMPD_CS` Well-Known Text definition (NAD83 / UTM zone 15N, `AUTHORITY["EPSG","26915"]`, with a `VERT_CS` NAVD88 height in meters) sized to match `robandee-synthetic.asc`. It is illustrative WKT for parser and OpenTopography-source tests, not a real OpenTopography sidecar response, and contains no request URL, authorization header, or API credential. Tests build zip archives in memory from these two files; no zip is committed.
