namespace SolidGround.Tests;

/// <summary>
/// A test double that never touches the network. It records every request it sees, in order, and returns
/// a caller-scripted response (or throws a caller-scripted exception) for each one. Used to verify
/// <see cref="Core.Sources.OpenTopography.OpenTopographyUsgs1mSource"/> sends exactly the request it
/// claims to, and to script every response shape its tests need without any real HTTP call.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;
    private readonly List<HttpRequestMessage> requests = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        this.responder = responder;
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    /// <summary>Every request this handler has seen, in the order it saw them.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return await responder(request, cancellationToken).ConfigureAwait(false);
    }
}
