namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Marks a method as a SpawnDev.AI browser test.<br/>
/// The method must return <see cref="Task"/> and take no parameters. Pass by returning normally, fail by
/// throwing, skip by throwing <see cref="SkipTestException"/>.
/// </summary>
/// <remarks>
/// Deliberately the same shape as SpawnDev.SpawnJS.WebWorkers' harness, so the Playwright runner is a port
/// rather than a new design and the console contract (<c>READY:</c> / <c>TEST:</c> / <c>RESULTS:</c>) is
/// identical. These tests run in the WINDOW scope and drive <c>AiWorkerClient</c> exactly as the UI does,
/// so they exercise the real worker transport rather than calling the engine in-process.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class AiTestAttribute : Attribute
{
    /// <summary>
    /// Milliseconds before the test is reported as timed out. 0 uses the runner default.
    /// </summary>
    /// <remarks>
    /// ⚠️ Anything that LOADS A MODEL needs a large value: the first run downloads hundreds of MB from
    /// HuggingFace before it can answer. Subsequent runs hit the OPFS cache.
    /// </remarks>
    public int Timeout { get; set; }

    /// <summary>
    /// When true the test is skipped unless the run explicitly opts in (runner <c>--heavy</c>, or
    /// <c>heavy=1</c> in the query string). For tests that download a model.
    /// </summary>
    public bool Heavy { get; set; }
}

/// <summary>
/// Throw from a test to report it as SKIPPED rather than failed - used when a capability the test needs
/// is genuinely unavailable in this browser or context.
/// </summary>
/// <remarks>
/// ⚠️ Not a general escape hatch. A precondition the test itself is ABOUT must be asserted, not skipped:
/// a KV-cache test that skips when the cache is absent is a test that can never fail, which is exactly how
/// six ILGPU.ML tests sat green for months while the code under them rotted.
/// </remarks>
public class SkipTestException : Exception
{
    /// <summary>New instance.</summary>
    /// <param name="reason">Why the test cannot run here.</param>
    public SkipTestException(string reason) : base(reason) { }
}
