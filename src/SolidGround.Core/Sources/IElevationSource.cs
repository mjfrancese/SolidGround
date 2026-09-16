namespace SolidGround.Core.Sources;

/// <summary>
/// Acquires elevation data from a source without prescribing its representation.
/// </summary>
public interface IElevationSource
{
    ValueTask<ElevationAcquisition> AcquireAsync(
        ElevationSourceRequest request,
        CancellationToken cancellationToken = default);
}
