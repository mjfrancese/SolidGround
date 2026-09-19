namespace SolidGround.Cli;

/// <summary>
/// Every external dependency <c>CliApplication</c> needs, injected so tests never touch the real process
/// environment, real secrets, or the real network. See docs/architecture/cli-workflow.md's "Testing
/// strategy" section for why each of these four members exists only to be replaced by a test double, and
/// its "Secrets and key resolution" section for how <see cref="GetEnvironmentVariable"/> alone drives both
/// the environment-variable and the user-secrets-file lookup.
/// </summary>
public sealed class CliHost
{
    public CliHost(
        Func<string, string?> getEnvironmentVariable,
        Func<HttpMessageHandler> httpMessageHandlerFactory,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        GetEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        HttpMessageHandlerFactory = httpMessageHandlerFactory ?? throw new ArgumentNullException(nameof(httpMessageHandlerFactory));
        StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    }

    public Func<string, string?> GetEnvironmentVariable { get; }
    public Func<HttpMessageHandler> HttpMessageHandlerFactory { get; }
    public TextWriter StandardOutput { get; }
    public TextWriter StandardError { get; }
}
