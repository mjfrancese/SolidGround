using SolidGround.Cli;

// Program.cs owns the single CancellationTokenSource this executable ever creates; it is cancelled only by
// Console.CancelKeyPress and its token flows into CliApplication.RunAsync unchanged -- see
// docs/architecture/cli-workflow.md's "Exit codes and error classes" section for why the CLI never
// interposes a linked or derived token source of its own.
using CancellationTokenSource cts = new();
ConsoleCancelEventHandler handler = (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
Console.CancelKeyPress += handler;

CliHost host = new(
    Environment.GetEnvironmentVariable,
    () => new SocketsHttpHandler(),
    Console.Out,
    Console.Error);

int exitCode = await CliApplication.RunAsync(args, host, cts.Token);
Console.CancelKeyPress -= handler;
return exitCode;
