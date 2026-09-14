namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A choice made once is still there next visit.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE FAILURE THIS GUARDS IS SILENT. Every selection in this app lived in a component field, so a
/// reload restored the defaults - and for the voice that means a user who recorded and picked their own
/// voice comes back speaking as the bundled one, with nothing having said so. "It forgot" and "it
/// ignored me" look identical from the outside.
/// </para>
/// <para>
/// ⚠️ THE EMPTY-STRING CASE IS THE WHOLE POINT, not an edge. Empty is a REAL voice choice ("clone me
/// each turn"), so a store that treats empty as "nothing saved" silently converts that deliberate choice
/// back into the default on every single reload - which is the same class of bug as the one that made
/// cloning the default in the first place.
/// </para>
/// <para>
/// ⚠️ Real OPFS through the app's own <c>IAsyncFS</c>, and a SECOND instance reading it back, because
/// that is what a reload is. An in-memory-only preference passes any test that reuses one instance.
/// </para>
/// </remarks>
public sealed class AppPreferencesTests
{
    private readonly SpawnDev.AsyncFileSystem.IAsyncFS _fs;

    /// <summary>New instance over the app's OPFS filesystem.</summary>
    public AppPreferencesTests(SpawnDev.AsyncFileSystem.IAsyncFS fs) => _fs = fs;

    /// <summary>A stored choice survives a reload, and an empty one is a choice too.</summary>
    [AiTest(Timeout = 60_000)]
    public async Task AChoiceSurvivesAReload()
    {
        var key = "test-" + Guid.NewGuid().ToString("N")[..8];

        var prefs = new AppPreferences(_fs);
        await prefs.LoadAsync();
        if (prefs.Get(key) != null)
            throw new Exception("a key nobody has set came back with a value");

        await prefs.SetAsync(key, "bundled:something");

        // A fresh instance over the same storage IS a page reload.
        var reloaded = new AppPreferences(_fs);
        await reloaded.LoadAsync();
        if (reloaded.Get(key) != "bundled:something")
            throw new Exception($"the choice did not survive a reload (got '{reloaded.Get(key) ?? "null"}')");

        // 🔴 EMPTY IS A CHOICE. For the voice it means "clone me each turn"; a store that loses it puts
        // the user back on the default voice at every reload.
        await reloaded.SetAsync(key, "");
        var again = new AppPreferences(_fs);
        await again.LoadAsync();
        if (again.Get(key) != "")
            throw new Exception($"an empty choice came back as '{again.Get(key) ?? "null"}' - a deliberate "
                + "\"clone me each turn\" selection would be silently reset to the default voice");

        // And removal really removes, or a preference could never be un-set.
        await again.SetAsync(key, null);
        var cleared = new AppPreferences(_fs);
        await cleared.LoadAsync();
        if (cleared.Get(key) != null)
            throw new Exception("removing a preference did not stick");
    }

    /// <summary>One preference does not disturb another.</summary>
    /// <remarks>
    /// They share one file, so a write that serialises only the key it was given would drop every other
    /// setting - and the symptom would be "the app forgets the model whenever I change the voice", which
    /// reads as a completely unrelated defect.
    /// </remarks>
    [AiTest(Timeout = 60_000)]
    public async Task WritingOneKeyKeepsTheOthers()
    {
        var a = "test-a-" + Guid.NewGuid().ToString("N")[..8];
        var b = "test-b-" + Guid.NewGuid().ToString("N")[..8];

        var prefs = new AppPreferences(_fs);
        await prefs.LoadAsync();
        await prefs.SetAsync(a, "first");
        await prefs.SetAsync(b, "second");

        var reloaded = new AppPreferences(_fs);
        await reloaded.LoadAsync();
        if (reloaded.Get(a) != "first" || reloaded.Get(b) != "second")
            throw new Exception($"writing '{b}' lost '{a}': a='{reloaded.Get(a) ?? "null"}', "
                + $"b='{reloaded.Get(b) ?? "null"}'");

        await reloaded.SetAsync(a, null);
        await reloaded.SetAsync(b, null);
    }
}
