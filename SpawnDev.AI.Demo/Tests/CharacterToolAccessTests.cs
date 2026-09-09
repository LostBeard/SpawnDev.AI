namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A character is handed exactly the tools it is allowed to call, and no others.
/// </summary>
/// <remarks>
/// 🔴 THIS IS A CAPABILITY BOUNDARY, NOT A PREFERENCE. These tools act on the world - they generate
/// images and, once Reachy is wired, MOVE A PHYSICAL ROBOT. "One or more personas may drive the robot"
/// is a different statement from "every character in the room may", and the difference has to be enforced
/// by what the model is handed rather than by asking it nicely in a prompt: a tool a model never receives
/// is one it cannot be talked into calling.
/// <para>
/// ⚠️ NOT heavy - pure filtering, no model. The failure it guards against is a character silently gaining
/// an ability nobody granted it, which no amount of watching a conversation would reveal.
/// </para>
/// </remarks>
public sealed class CharacterToolAccessTests
{
    private static readonly List<string> ServerTools = new()
    {
        """{"type":"function","function":{"name":"generate_image","description":"Draw","parameters":{}}}""",
        """{"type":"function","function":{"name":"reachy_move","description":"Move the robot","parameters":{}}}""",
        """{"type":"function","function":{"name":"reachy_snapshot","description":"Take a photo","parameters":{}}}""",
    };

    private static ChatAgent Agent(params string[] allowed)
        => new("c", "Cast", "m", "persona", null, allowed.Length == 0 ? null : allowed);

    /// <summary>Only the named tools are handed over.</summary>
    [AiTest(Timeout = 30_000)]
    public Task ACharacterGetsOnlyTheToolsItWasGranted()
    {
        var one = CharacterLibrary.ToolsFor(Agent("reachy_move"), ServerTools);
        if (one.Count != 1 || !one[0].Contains("reachy_move"))
            throw new Exception($"expected just reachy_move, got {one.Count}: {string.Join(" ", one)}");
        if (one.Any(t => t.Contains("reachy_snapshot") || t.Contains("generate_image")))
            throw new Exception("a tool the character was NOT granted was handed to it");

        var two = CharacterLibrary.ToolsFor(Agent("reachy_move", "reachy_snapshot"), ServerTools);
        if (two.Count != 2) throw new Exception($"expected two tools, got {two.Count}");

        // 🔴 THE DEFAULT MUST BE NONE. A character created before tools existed reads back with a null
        // list, and it must not quietly acquire the ability to move a robot because of that.
        if (CharacterLibrary.ToolsFor(Agent(), ServerTools).Count != 0)
            throw new Exception("a character granted NO tools was handed some - the default must be none");

        // Naming a tool the server does not offer is not an error; it simply is not there.
        if (CharacterLibrary.ToolsFor(Agent("no_such_tool"), ServerTools).Count != 0)
            throw new Exception("an unknown tool name must resolve to nothing rather than to everything");

        // Nothing to hand over when the server published nothing.
        if (CharacterLibrary.ToolsFor(Agent("reachy_move"), new List<string>()).Count != 0)
            throw new Exception("with no server tools a character must get none");

        // A malformed definition is skipped, not treated as a wildcard match.
        var withJunk = new List<string>(ServerTools) { "{not json" };
        if (CharacterLibrary.ToolsFor(Agent("reachy_move"), withJunk).Count != 1)
            throw new Exception("an unparseable tool definition must be ignored, not granted");
        return Task.CompletedTask;
    }

    /// <summary>Tool grants survive a save and reload, and an old character stays tool-less.</summary>
    [AiTest(Timeout = 60_000)]
    public async Task ToolGrantsPersistAndDefaultToNone()
    {
        var withTools = new SavedCharacter("t1", "Driver", "p", "m", null, DateTime.UtcNow,
            new[] { "reachy_move" });
        var agent = CharacterLibrary.ToAgent(withTools, "room-model", null);
        if (agent.AllowedTools is not { Count: 1 } || agent.AllowedTools[0] != "reachy_move")
            throw new Exception("the grant was lost mapping a character to a room agent");

        // The shape a character saved before tools existed deserialises to: no AllowedTools at all.
        var legacy = new SavedCharacter("t2", "Old", "p", "m", null, DateTime.UtcNow);
        if (CharacterLibrary.ToAgent(legacy, "room-model", null).AllowedTools is { Count: > 0 })
            throw new Exception("a character saved before tools existed must NOT gain tool access");

        // 🔴 A character saved before MotionScale existed deserialises to 0, and 0 means "never moves" -
        // a body that silently stopped acting, with nothing reporting why. It must normalise to normal.
        var legacyScale = new SavedCharacter("t3", "Old", "p", "m", null, DateTime.UtcNow,
            null, AvatarKind.Screen, 0);
        var normalised = CharacterLibrary.ToAgent(legacyScale, "m", null);
        if (Math.Abs(normalised.MotionScale - 1.0) > 0.001)
            throw new Exception($"a stored MotionScale of 0 became {normalised.MotionScale} - 0 scales "
                + "every gesture to nothing, so the character would appear to stop acting");

        // A deliberate value survives untouched.
        var chosen = CharacterLibrary.ToAgent(legacyScale with { MotionScale = 0.4 }, "m", null);
        if (Math.Abs(chosen.MotionScale - 0.4) > 0.001)
            throw new Exception($"a chosen MotionScale of 0.4 came back as {chosen.MotionScale}");

        await Task.CompletedTask;
    }
}
