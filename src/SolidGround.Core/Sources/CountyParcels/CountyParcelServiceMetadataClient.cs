using System.Text.Json;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>Reads public ArcGIS service metadata so an operator can select an advertised parcel layer/field.</summary>
public sealed class CountyParcelServiceMetadataClient
{
    private readonly HttpClient httpClient;

    public CountyParcelServiceMetadataClient(HttpClient httpClient) => this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<CountyParcelServiceMetadata> FetchAsync(Uri serviceBaseUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceBaseUri);
        if (!serviceBaseUri.IsAbsoluteUri || serviceBaseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(serviceBaseUri.Query) || !string.IsNullOrEmpty(serviceBaseUri.Fragment))
        {
            throw new FormatException("County service URL must be an absolute https URL without a query or fragment.");
        }
        JsonDocument service = await GetJsonAsync(BuildMetadataUri(serviceBaseUri), cancellationToken).ConfigureAwait(false);
        using (service)
        {
            if (!service.RootElement.TryGetProperty("layers", out JsonElement layersElement) || layersElement.ValueKind != JsonValueKind.Array) throw new FormatException("County service metadata did not contain a layers array.");
            List<CountyParcelServiceLayerMetadata> layers = [];
            foreach (JsonElement entry in layersElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("id", out JsonElement id) || !id.TryGetInt32(out int index) || !entry.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String) throw new FormatException("County service metadata contains a layer without numeric id and name.");
                using JsonDocument layer = await GetJsonAsync(BuildMetadataUri(new Uri(serviceBaseUri.AbsoluteUri.TrimEnd('/') + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))), cancellationToken).ConfigureAwait(false);
                if (!layer.RootElement.TryGetProperty("fields", out JsonElement fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array) throw new FormatException($"County layer {index} metadata did not contain fields.");
                HashSet<string> fields = new(StringComparer.Ordinal);
                foreach (JsonElement field in fieldsElement.EnumerateArray())
                {
                    if (field.TryGetProperty("name", out JsonElement fieldName) && fieldName.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(fieldName.GetString())) fields.Add(fieldName.GetString()!);
                }
                layers.Add(new CountyParcelServiceLayerMetadata(index, name.GetString()!, fields));
            }
            return new CountyParcelServiceMetadata(serviceBaseUri, layers);
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static Uri BuildMetadataUri(Uri baseUri)
    {
        UriBuilder builder = new(baseUri) { Query = "f=json" };
        return builder.Uri;
    }
}
