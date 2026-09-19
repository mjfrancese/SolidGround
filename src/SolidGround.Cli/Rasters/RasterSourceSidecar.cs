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
    RasterSourceAcquisition Acquisition);

/// <summary>The vertical reference recorded for a fetched raster.</summary>
internal sealed record RasterSourceVertical(string Datum, LengthUnit Unit, string? GeoidModel);

/// <summary>
/// Redacted evidence about how a fetched raster's response was interpreted, carried through unchanged from
/// <c>OpenTopographyResponseEvidence</c>.
/// </summary>
internal sealed record RasterSourceAcquisition(
    string RedactedRequestUri,
    int StatusCode,
    string? ContentType,
    string? ContentDispositionFileName,
    IReadOnlyList<string> ArchiveEntryNames,
    string ReferenceSource,
    long ResponseByteCount);
