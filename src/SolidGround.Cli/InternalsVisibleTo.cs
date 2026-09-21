using System.Runtime.CompilerServices;

// Originally added for Issue #25 so SolidGround.Tests.LengthUnitTokensTests could exercise the CLI's own
// `Options.LengthUnitTokens` -- internal at the time -- directly rather than only indirectly through
// CliApplication.RunAsync's stdout/stderr. SolidGround Issue #15's Core lift moved LengthUnitTokens (and
// several sibling types) into SolidGround.Core, public, so that original reason no longer applies; this grant
// is left in place as a general-purpose seam for any other SolidGround.Cli-internal member a future test
// needs to exercise directly, per the same reasoning.
[assembly: InternalsVisibleTo("SolidGround.Tests")]
