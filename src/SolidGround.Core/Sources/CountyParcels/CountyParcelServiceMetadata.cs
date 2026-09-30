namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>
/// Non-secret metadata read from a county's ArcGIS service before an operator creates a registration. It is
/// deliberately separate from a usable registration: metadata discovery never authorizes a live provider.
/// </summary>
public sealed record CountyParcelServiceMetadata(
    Uri ServiceBaseUri,
    IReadOnlyList<CountyParcelServiceLayerMetadata> Layers);

/// <summary>One selectable parcel layer and the fields advertised by that layer.</summary>
public sealed record CountyParcelServiceLayerMetadata(
    int LayerIndex,
    string DisplayName,
    IReadOnlySet<string> Fields)
{
    public bool HasField(string fieldName) => !string.IsNullOrWhiteSpace(fieldName) && Fields.Contains(fieldName);
}

/// <summary>Validates that an operator-selected registration matches downloaded service metadata.</summary>
public static class CountyParcelServiceMetadataValidator
{
    /// <exception cref="FormatException">The selected layer or any registered field is not advertised.</exception>
    public static void ValidateRegistration(CountyParcelServiceMetadata metadata, CountyParcelRegistryEntry registration)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(registration);
        CountyParcelServiceLayerMetadata layer = metadata.Layers.SingleOrDefault(candidate => candidate.LayerIndex == registration.LayerIndex)
            ?? throw new FormatException($"The selected parcel layer {registration.LayerIndex} is not present in the service metadata.");
        foreach (string field in RegisteredFields(registration.FieldMap))
        {
            if (!layer.HasField(field)) throw new FormatException($"Registered parcel field '{field}' is not present in layer {registration.LayerIndex} metadata.");
        }
    }

    private static IEnumerable<string> RegisteredFields(CountyParcelFieldMap fields) =>
        new string?[] { fields.ParcelId, fields.SitusAddress, fields.Subdivision, fields.Lot, fields.Block, fields.Plat, fields.Book, fields.Page, fields.LegalDescription, fields.ReportedAcres, fields.Zoning, fields.StableParcelId }
            .OfType<string>();
}
