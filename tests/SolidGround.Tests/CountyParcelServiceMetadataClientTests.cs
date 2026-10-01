using System.Net;
using System.Net.Http;
using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

public sealed class CountyParcelServiceMetadataClientTests
{
    [Fact]
    public async Task FetchesLayersThenTheirAdvertisedFields()
    {
        using HttpClient client = new(new Handler());
        CountyParcelServiceMetadataClient service = new(client);

        CountyParcelServiceMetadata metadata = await service.FetchAsync(new Uri("https://county.example.invalid/FeatureServer"), TestContext.Current.CancellationToken);
        CountyParcelServiceLayerMetadata layer = Assert.Single(metadata.Layers);

        Assert.Equal(4, layer.LayerIndex);
        Assert.Contains("PARCEL_ID", layer.Fields);
    }

    [Fact]
    public async Task RejectsAServiceUrlWithAQueryBeforeSendingIt()
    {
        using HttpClient client = new(new Handler());
        CountyParcelServiceMetadataClient service = new(client);

        FormatException error = await Assert.ThrowsAsync<FormatException>(() => service.FetchAsync(new Uri("https://county.example.invalid/FeatureServer?metadata=placeholder"), TestContext.Current.CancellationToken));

        Assert.Contains("query", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = request.RequestUri!.AbsolutePath.EndsWith("/4", StringComparison.Ordinal)
                ? "{\"fields\":[{\"name\":\"PARCEL_ID\"},{\"name\":\"SITUS\"}]}"
                : "{\"layers\":[{\"id\":4,\"name\":\"Synthetic parcels\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
