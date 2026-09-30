namespace SolidGround.Core.Workflow;

/// <summary>Runs one bounded, keyless reachability request. A success does not prove USGS entitlement.</summary>
public static class ReachabilityProbe
{
    public static async ValueTask<string?> ProbeAsync(HttpClient client, Uri endpoint, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(endpoint);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"Reachability check returned HTTP {(int)response.StatusCode}.";
        }
        catch (OperationCanceledException) { return "Reachability check timed out or was cancelled."; }
        catch (HttpRequestException) { return "Reachability check could not reach the service."; }
    }
}
