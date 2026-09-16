using System.Reflection;

namespace SpawnDev.AI.Demo;

/// <summary>
/// Says which build is actually running, in the browser console, on startup.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 A STALE BUILD EXPLAINS MORE FAILURES THAN ANY THEORY, and a browser is the easiest place to be
/// served one: a cached <c>index.html</c>, a service worker holding an old boot manifest, a CDN that has
/// not caught up with a deploy, or an automated run that published somewhere other than the page it then
/// drove. Every one of those looks like the code being wrong. The sibling SpawnDev.ILGPU.ML demo prints
/// this for exactly that reason; this app had nothing, so "did my change reach the page" was unanswerable
/// without guessing.
/// </para>
/// <para>
/// ⚠️ The stamp is written by MSBuild into an assembly attribute at BUILD time, not read from a file date
/// or computed at startup. A value derived at runtime cannot distinguish a fresh page running old code
/// from a fresh page running new code, which is the only question this exists to answer.
/// </para>
/// </remarks>
public static class BuildStamp
{
    /// <summary>When this assembly was built, UTC, or "unknown" if the stamp is missing.</summary>
    public static string BuiltUtc { get; } =
        typeof(BuildStamp).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildStampUtc")?.Value ?? "unknown";

    /// <summary>
    /// Print the build line. Prefixed <c>[BUILD]</c> so a gate can read it off the console and refuse to
    /// trust a run against a build it did not just deploy.
    /// </summary>
    public static void Print()
    {
        var v = typeof(BuildStamp).Assembly.GetName().Version?.ToString() ?? "?";
        Console.WriteLine($"[BUILD] SpawnDev.AI.Demo {v} built {BuiltUtc}");
    }
}
