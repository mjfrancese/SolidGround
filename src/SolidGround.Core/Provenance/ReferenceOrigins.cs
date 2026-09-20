using SolidGround.Core.Metadata;

namespace SolidGround.Core.Provenance;

/// <summary>
/// Where an assembled payload's horizontal and vertical references came from, carried unchanged into
/// <see cref="TerrainProvenance.SourceHorizontalReferenceOrigin"/> and
/// <see cref="TerrainProvenance.SourceVerticalReferenceOrigin"/> by <see cref="TerrainExportPayloadAssembler.Assemble"/>.
/// See docs/architecture/provenance-and-deterministic-exports.md's "Export document manifest, schema version
/// 2" section.
/// </summary>
public sealed record ReferenceOrigins(ReferenceOrigin Horizontal, ReferenceOrigin Vertical);
