using System.Runtime.CompilerServices;

// Issue #25 (see docs/architecture/cli-workflow.md's "Units" section): SolidGround.Tests.LengthUnitTokensTests
// exercises `Options.LengthUnitTokens` -- internal, per AGENTS.md's Phase-1 scope, because it is a CLI-only
// helper with no reason to be part of the CLI's public surface -- directly rather than only indirectly
// through `CliApplication.RunAsync`'s stdout/stderr, so its round-trip and rejection behavior can be asserted
// without depending on any one command's option wiring.
[assembly: InternalsVisibleTo("SolidGround.Tests")]
