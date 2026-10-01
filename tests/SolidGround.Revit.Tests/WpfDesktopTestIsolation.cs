// Window.Show and keyboard focus share the Windows desktop, even when each test
// creates its own STA dispatcher. Parallel windows can steal focus from a test
// between validation and its assertion. Keep the actual focus checks intact.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
