using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using SpawnDev.SpawnJS;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// The SpawnDev.AI browser test suite runner. Discovers <see cref="AiTestAttribute"/> methods, runs them,
/// and writes one machine-readable line per test plus a summary - the same console contract the
/// SpawnDev.SpawnJS.WebWorkers harness uses, so the Playwright runner reads both.
/// </summary>
/// <remarks>
/// Test classes are constructed through the app's <see cref="IServiceProvider"/>, so a test class can take
/// any registered service (notably <c>AiWorkerClient</c>) as a constructor parameter.
/// </remarks>
public static class AiTestSuiteRunner
{
    /// <summary>The test classes that make up the suite. Add new test classes here.</summary>
    public static Type[] TestTypes { get; } =
    {
        typeof(AiServerTests),
        typeof(AiChatTests),
        typeof(AiSpeechTests),
        typeof(AiVoiceTests),
    };

    /// <summary>Milliseconds a test may run before it is reported as timed out. Overridable per test.</summary>
    public static int DefaultTimeoutMs { get; set; } = 60_000;

    /// <summary>Whether this run includes tests marked <see cref="AiTestAttribute.Heavy"/>.</summary>
    public static bool IncludeHeavy { get; private set; }

    /// <summary>
    /// Runs every test whose name contains <paramref name="filter"/> (null or empty runs all).
    /// Returns the number of FAILED tests.
    /// </summary>
    /// <param name="services">App service provider; test classes are resolved from it.</param>
    /// <param name="filter">Substring filter over "Class.Method", or null for all.</param>
    /// <param name="includeHeavy">Include model-downloading tests.</param>
    /// <returns>Number of failed tests.</returns>
    public static async Task<int> RunAllAsync(IServiceProvider services, string? filter = null,
        bool includeHeavy = false)
    {
        IncludeHeavy = includeHeavy;
        ConfigureClientFromLocation(services);
        int passed = 0, failed = 0, skipped = 0;
        var tests = Discover(filter);

        Console.WriteLine($"READY: {tests.Count} tests"
            + (string.IsNullOrEmpty(filter) ? "" : $" (filter '{filter}')")
            + (includeHeavy ? " (heavy included)" : ""));

        foreach (var (type, method) in tests)
        {
            var name = $"{type.Name}.{method.Name}";
            var attr = method.GetCustomAttribute<AiTestAttribute>();
            var timeoutMs = attr?.Timeout > 0 ? attr.Timeout : DefaultTimeoutMs;
            var sw = Stopwatch.StartNew();
            string result;
            string detail = "";

            if (attr is { Heavy: true } && !includeHeavy)
            {
                result = "SKIP";
                detail = "heavy (downloads a model) - pass --heavy to include";
                skipped++;
            }
            else
            {
                try
                {
                    var instance = ActivatorUtilities.CreateInstance(services, type);
                    var task = (Task?)method.Invoke(instance, null)
                               ?? throw new InvalidOperationException("test did not return a Task");
                    // .NET WASM is single threaded, so this catches a test that AWAITS forever, not one
                    // stuck in a tight synchronous loop.
                    var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
                    if (completed != task) throw new TimeoutException($"timed out after {timeoutMs} ms");
                    await task;
                    result = "PASS";
                    passed++;
                    (instance as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    var inner = Unwrap(ex);
                    if (inner is SkipTestException)
                    {
                        result = "SKIP";
                        detail = inner.Message;
                        skipped++;
                    }
                    else
                    {
                        result = "FAIL";
                        detail = $"{inner.GetType().Name}: {inner.Message}";
                        failed++;
                    }
                }
            }

            sw.Stop();
            Console.WriteLine($"TEST: {name}|{result}|{sw.ElapsedMilliseconds}|{Sanitize(detail)}");
        }

        Console.WriteLine($"RESULTS: Failed: {failed} Passed: {passed} Skipped: {skipped} Ran: {tests.Count}");
        return failed;
    }

    private static List<(Type Type, MethodInfo Method)> Discover(string? filter)
    {
        var found = new List<(Type, MethodInfo)>();
        foreach (var type in TestTypes)
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.GetCustomAttribute<AiTestAttribute>() == null) continue;
                if (!string.IsNullOrEmpty(filter)
                    && !$"{type.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                found.Add((type, m));
            }
        return found;
    }

    /// <summary>Unwrap the reflection/aggregate layers so the reported error is the one the test threw.</summary>
    private static Exception Unwrap(Exception ex)
    {
        while (true)
        {
            if (ex is TargetInvocationException { InnerException: { } tie }) { ex = tie; continue; }
            if (ex is AggregateException { InnerExceptions.Count: 1 } ae) { ex = ae.InnerExceptions[0]; continue; }
            return ex;
        }
    }

    /// <summary>
    /// Apply query-string switches to <c>AiWorkerClient</c> before any test runs.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>?worker=dedicated</c> is read by <c>Home.razor.cs</c>, which sets
    /// <c>PreferSharedWorker = false</c> as part of the START BUTTON flow. These tests never click that
    /// button - they drive <c>AiWorkerClient</c> directly - so a switch handled only by the page would
    /// silently do nothing here. The suite therefore reads it itself.
    /// <para>
    /// Why it matters: a SHARED worker's console output does NOT reach <c>page.Console</c>, so Playwright
    /// cannot see model-load progress and a slow load is indistinguishable from a hang. A DEDICATED worker
    /// shares its console with the window, which makes the load visible to the runner.
    /// </para>
    /// </remarks>
    /// <param name="services">App service provider.</param>
    private static void ConfigureClientFromLocation(IServiceProvider services)
    {
        if (QueryValue("worker") is not { } worker) return;
        var client = services.GetService(typeof(SpawnDev.AI.Server.AiWorkerClient))
            as SpawnDev.AI.Server.AiWorkerClient;
        if (client == null) return;

        if (worker.Equals("dedicated", StringComparison.OrdinalIgnoreCase))
        {
            client.PreferSharedWorker = false;
            Console.WriteLine("[AiTestSuiteRunner] worker=dedicated - console output will reach the window");
        }
        else if (worker.Equals("shared", StringComparison.OrdinalIgnoreCase))
        {
            client.PreferSharedWorker = true;
            Console.WriteLine("[AiTestSuiteRunner] worker=shared");
        }
    }

    /// <summary>The <c>filter</c> query-string value, or null.</summary>
    public static string? FilterFromLocation() => QueryValue("filter");

    /// <summary>True when the query string opts into heavy (model-downloading) tests.</summary>
    public static bool HeavyFromLocation() => QueryValue("heavy") is "1" or "true";

    /// <summary>True when the query string asks for a test run at all.</summary>
    public static bool RequestedFromLocation() => QueryValue("tests") is "1" or "true";

    private static string? QueryValue(string key)
    {
        var js = SpawnJSRuntime.Instance;
        var search = js?.Get<string?>("location.search");
        if (string.IsNullOrEmpty(search)) return null;
        foreach (var pair in search.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key) return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static string Sanitize(string value)
        => value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
}
