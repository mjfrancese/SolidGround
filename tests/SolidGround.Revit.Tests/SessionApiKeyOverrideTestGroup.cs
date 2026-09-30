namespace SolidGround.Revit.Tests;

/// <summary>Serializes tests that change the process-wide, non-persisted API-key override state.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SessionApiKeyOverrideTestGroup
{
    public const string Name = "Session API-key overrides";
}
