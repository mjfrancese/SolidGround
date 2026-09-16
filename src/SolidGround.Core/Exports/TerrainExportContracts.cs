using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;

namespace SolidGround.Core.Exports;

/// <summary>A terrain sample already expressed in its provenance local frame.</summary>
public sealed record LocalTerrainSample(LocalCoordinate Position);

/// <summary>The complete, self-describing payload sent through an export adapter.</summary>
public sealed class TerrainExportPayload
{
    private readonly LocalTerrainSample[] samples;

    public TerrainExportPayload(IEnumerable<LocalTerrainSample> samples, TerrainProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(provenance);
        this.samples = samples.ToArray();
        if (this.samples.Any(sample => sample is null))
        {
            throw new ArgumentException("Export samples cannot contain null values.", nameof(samples));
        }

        if (this.samples.Length != provenance.RetainedPointCount)
        {
            throw new ArgumentException("Export sample count must match the retained provenance count.", nameof(samples));
        }

        Samples = Array.AsReadOnly(this.samples);
        Provenance = provenance;
    }

    public IReadOnlyList<LocalTerrainSample> Samples { get; }
    public TerrainProvenance Provenance { get; }
}

/// <summary>Destination-neutral seam for development and fallback exports.</summary>
public interface ITerrainExporter
{
    ValueTask<TerrainExportReceipt> ExportAsync(TerrainExportPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>Reports the stable identifier returned by an export destination.</summary>
public sealed record TerrainExportReceipt
{
    public TerrainExportReceipt(string destinationIdentifier)
    {
        if (string.IsNullOrWhiteSpace(destinationIdentifier))
        {
            throw new ArgumentException("A destination identifier is required.", nameof(destinationIdentifier));
        }

        DestinationIdentifier = destinationIdentifier;
    }

    public string DestinationIdentifier { get; }
}
