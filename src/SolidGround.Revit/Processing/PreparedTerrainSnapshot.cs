using SolidGround.Core.Processing;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Revit.Processing;

/// <summary>Immutable acquired terrain reused by the floor preview and the final creation gate.</summary>
internal sealed record PreparedTerrainSnapshot(
    ElevationGrid Grid,
    TerrainProcessingOutcome Outcome,
    IHorizontalCoordinateTransform Transform,
    ProjectionCharacteristicsMeasurement? Projection,
    string InputSignature = "");
