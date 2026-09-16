using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;

namespace SolidGround.Core.Transformations;

/// <summary>A versionable engine-specific operation definition retained for reproducibility.</summary>
public sealed record CoordinateOperationDefinition
{
    public CoordinateOperationDefinition(string format, string definition)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException("Operation definition format is required.", nameof(format));
        }

        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new ArgumentException("Operation definition is required.", nameof(definition));
        }

        Format = format;
        Definition = definition;
    }

    public string Format { get; }
    public string Definition { get; }
}

/// <summary>Describes both directions of a horizontal transformation without choosing an engine.</summary>
public sealed record HorizontalTransformationDefinition
{
    public HorizontalTransformationDefinition(
        HorizontalReference sourceReference,
        HorizontalReference targetReference,
        CoordinateOperationDefinition forwardOperation,
        CoordinateOperationDefinition inverseOperation,
        string engineName,
        string engineVersion)
    {
        SourceReference = sourceReference ?? throw new ArgumentNullException(nameof(sourceReference));
        TargetReference = targetReference ?? throw new ArgumentNullException(nameof(targetReference));
        ForwardOperation = forwardOperation ?? throw new ArgumentNullException(nameof(forwardOperation));
        InverseOperation = inverseOperation ?? throw new ArgumentNullException(nameof(inverseOperation));
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Transformation engine name is required.", nameof(engineName));
        }

        if (string.IsNullOrWhiteSpace(engineVersion))
        {
            throw new ArgumentException("Transformation engine version is required.", nameof(engineVersion));
        }

        EngineName = engineName;
        EngineVersion = engineVersion;
    }

    public HorizontalReference SourceReference { get; }
    public HorizontalReference TargetReference { get; }
    public CoordinateOperationDefinition ForwardOperation { get; }
    public CoordinateOperationDefinition InverseOperation { get; }
    public string EngineName { get; }
    public string EngineVersion { get; }
}

/// <summary>Package-neutral reversible seam for a horizontal coordinate operation.</summary>
public interface IHorizontalCoordinateTransform
{
    HorizontalTransformationDefinition Definition { get; }
    Coordinate2D Forward(Coordinate2D source);
    Coordinate2D Inverse(Coordinate2D target);
}
