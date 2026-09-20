using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Rasters;

/// <summary>
/// The CLI-owned <c>.source.json</c> sidecar written beside a fetched raster's <c>.asc</c>/<c>.prj</c> pair,
/// and read back by <c>process</c> when it is present. See docs/architecture/cli-workflow.md's "Raster set
/// persistence" section for this document's fixed property order, and its "Secrets and key resolution" and
/// "Diagnostics and redaction" sections for why no field here ever carries a timestamp or a key: every value
/// is already redacted by the source that produced it.
/// </summary>
internal sealed record RasterSourceSidecar(
    string SourceName,
    string DatasetIdentifier,
    CollectionPeriod? CollectionPeriod,
    string? QualityLevel,
    RasterSourceVertical Vertical,
    ReferenceOrigin HorizontalReferenceOrigin,
    ReferenceOrigin VerticalReferenceOrigin,
    RasterSourceAcquisition Acquisition);

/// <summary>The vertical reference recorded for a fetched raster.</summary>
internal sealed record RasterSourceVertical(string Datum, LengthUnit Unit, string? GeoidModel);

/// <summary>
/// Redacted evidence about how a fetched raster's response was interpreted, carried through unchanged from
/// <c>OpenTopographyResponseEvidence</c>, plus (schema version 3, SolidGround Issue #23) the fetch envelope
/// actually requested and whether/how it was widened past OpenTopography's own undocumented minimum request
/// area.
/// </summary>
internal sealed record RasterSourceAcquisition(
    string RedactedRequestUri,
    int StatusCode,
    string? ContentType,
    string? ContentDispositionFileName,
    IReadOnlyList<string> ArchiveEntryNames,
    string ReferenceSource,
    long ResponseByteCount,
    RasterSourceMetadataRequest? MetadataRequest,
    RasterSourceFetchEnvelope FetchEnvelope);

/// <summary>
/// Redacted evidence about the GeoTIFF metadata request, carried through unchanged from
/// <c>OpenTopographyMetadataRequestEvidence</c>; present only when <see cref="RasterSourceAcquisition.ReferenceSource"/>
/// is <c>"GeoTiffGeoKeys"</c>.
/// </summary>
internal sealed record RasterSourceMetadataRequest(
    string RedactedRequestUri,
    int StatusCode,
    string? ContentType,
    string? ContentDispositionFileName,
    long ResponseByteCount,
    int ProjectedCoordinateSystemCode,
    string? Citation,
    string RasterType,
    long ImageWidth,
    long ImageLength);

/// <summary>
/// The WGS 84 fetch envelope actually requested (after any SolidGround Issue #23 minimum-side expansion) and
/// whether/how <c>AoiNormalizationOptions.MinimumFetchEnvelopeSide</c> widened it, carried through unchanged
/// from <c>FetchEnvelopeExpansion</c>. Every distance is metres. See
/// docs/architecture/aoi-normalization-and-clipping.md's "Minimum fetch envelope, verified 2026-09-20"
/// section: this is the only structured record of the padded envelope — the design deliberately does not add
/// a general AOI-provenance field, since the redacted request URI already carries the identical box verbatim.
/// </summary>
internal sealed record RasterSourceFetchEnvelope(
    double West,
    double South,
    double East,
    double North,
    double MinimumSideMeters,
    bool Expanded,
    double WidthBeforeMeters,
    double HeightBeforeMeters,
    double WidthAfterMeters,
    double HeightAfterMeters);
