using System.Net;
using SolidGround.Core.Workflow;

namespace SolidGround.Tests;

public sealed class ReachabilityProbeTests
{
    [Fact]
    public async Task ProbeSendsExactlyOneGetAndReturnsSuccessWithoutRetry()
    {
        RecordingHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using HttpClient client = new(handler);

        string? error = await ReachabilityProbe.ProbeAsync(client, new Uri("https://example.invalid/ping"), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Get, handler.Method);
    }

    [Fact]
    public async Task ProbeReportsHttpFailureWithoutASecondRequest()
    {
        RecordingHandler handler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using HttpClient client = new(handler);

        string? error = await ReachabilityProbe.ProbeAsync(client, new Uri("https://example.invalid/ping"), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal("Reachability check returned HTTP 503.", error);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProbeReportsTimeoutAndCancellationWithoutThrowing()
    {
        RecordingHandler delay = new(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using HttpClient timeoutClient = new(delay);
        string? timeout = await ReachabilityProbe.ProbeAsync(timeoutClient, new Uri("https://example.invalid/ping"), TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        Assert.Equal("Reachability check timed out.", timeout);
        Assert.Equal(1, delay.CallCount);

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        RecordingHandler cancelledHandler = new(token => Task.FromCanceled<HttpResponseMessage>(token));
        using HttpClient cancelledClient = new(cancelledHandler);
        string? cancellation = await ReachabilityProbe.ProbeAsync(cancelledClient, new Uri("https://example.invalid/ping"), TimeSpan.FromSeconds(1), cancelled.Token);
        Assert.Equal("Reachability check was cancelled.", cancellation);
        Assert.InRange(cancelledHandler.CallCount, 0, 1);
    }

    [Fact]
    public async Task ProbeReturnsSafeErrorForTransportAndUnexpectedFailures()
    {
        using HttpClient transportClient = new(new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("secret endpoint detail"))));
        Assert.Equal("Reachability check could not reach the service.", await ReachabilityProbe.ProbeAsync(transportClient, new Uri("https://example.invalid/ping"), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        using HttpClient unexpectedClient = new(new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("secret endpoint detail"))));
        Assert.Equal("Reachability check could not complete safely.", await ReachabilityProbe.ProbeAsync(unexpectedClient, new Uri("https://example.invalid/ping"), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    private sealed class RecordingHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        internal int CallCount { get; private set; }
        internal HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            return response(cancellationToken);
        }
    }
}
