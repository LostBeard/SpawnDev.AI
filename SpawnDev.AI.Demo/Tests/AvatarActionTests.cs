using SpawnDev.Reachy;

namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// A reply's stage directions reach a body, in order, and the one physical robot is never contended.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - no model. And deliberately NOT a test of <c>GestureClassifier</c>: which words map to
/// which gesture is the SDK's business, tested against real captured model output there. Re-asserting its
/// table here would pin the demo to today's phrasings and break every time the SDK learned a new one.
/// What is tested is what this layer actually does: preserve order, drop what no body can perform, and
/// arbitrate the single robot.
/// </remarks>
public sealed class AvatarActionTests
{
    /// <summary>Recognised gestures come out in the order they were written.</summary>
    /// <remarks>
    /// 🔴 ORDER IS THE PART THAT MATTERS AND THE PART THAT LOOKS FINE WHEN WRONG. A character that nods
    /// and then shakes its head means something specific; performing those the other way round is a
    /// different reply, and nothing on screen would indicate it happened.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task GesturesComeOutInWrittenOrder()
    {
        // Phrasings taken from the SDK's own real-model-output fixtures, so this leans on cues that are
        // known to classify rather than on wording invented to match a table.
        var got = AvatarActions.RecogniseAll("*nods enthusiastically* Sure. *shakes her head* Actually, no.");
        if (got.Count != 2)
            throw new Exception($"expected two gestures, got {got.Count}: [{string.Join(",", got)}]");
        if (got[0] != Gesture.Nod || got[1] != Gesture.Shake)
            throw new Exception($"order or mapping wrong: [{string.Join(",", got)}]");

        // Nothing physical written = nothing performed. A body that moves on every reply is noise.
        if (AvatarActions.RecogniseAll("Just talking, nothing physical at all.").Count != 0)
            throw new Exception("plain dialogue must produce no motion");
        if (AvatarActions.RecogniseAll("").Count != 0) throw new Exception("empty text must produce none");
        if (AvatarActions.RecogniseAll(null).Count != 0) throw new Exception("null must produce none");

        // The filter must never emit None - it is the "no body can perform this" value, and animating it
        // would show a motion for text nothing understood.
        foreach (var g in AvatarActions.RecogniseAll("*tilts head* Hm. *antennas droop sadly* Oh."))
            if (g == Gesture.None) throw new Exception("Gesture.None leaked through as a performable motion");
        return Task.CompletedTask;
    }

    /// <summary>Un-asterisked prose that is really a stage direction still reaches the body.</summary>
    /// <remarks>
    /// Models write "His head bobs up and down" as ordinary narration, with no markers at all. The SDK's
    /// splitter pulls those out; this pins that the demo actually benefits from it rather than only
    /// handling the asterisked form - the whole reason to use the SDK splitter over a local one.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task ProseStageDirectionsAreRecognisedToo()
    {
        var got = AvatarActions.RecogniseAll("Her head tilts to one side.");
        if (got.Count == 0)
            throw new Exception("an un-asterisked prose stage direction produced no gesture - the demo is "
                + "not getting the SDK splitter's prose extraction");
        return Task.CompletedTask;
    }

    /// <summary>Only one character drives the single physical robot; the rest still get a body.</summary>
    /// <remarks>
    /// 🔴 THERE IS EXACTLY ONE REACHY MINI. Two characters both driving it would issue conflicting moves to
    /// the same head, and the result is not a merge - it is whichever command landed last, with BOTH
    /// characters appearing to act wrongly. The fallback matters as much: a character that loses the robot
    /// must still be drawn, or half the cast silently stops acting mid-scene.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task OnlyOneCharacterDrivesTheRobot()
    {
        var uzi = new ChatAgent("uzi", "Uzi", "m", "p", null, null, AvatarKind.Reachy);
        var n = new ChatAgent("n", "N", "m", "p", null, null, AvatarKind.Reachy);
        var doll = new ChatAgent("doll", "Doll", "m", "p", null, null, AvatarKind.Screen);
        var room = new[] { uzi, n, doll };

        var holder = AvatarActions.SoleRobotHolder(room);
        if (holder != "uzi")
            throw new Exception($"the first character in speaking order should hold the robot, got '{holder}'");

        if (AvatarActions.EffectiveAvatar(uzi, holder) != AvatarKind.Reachy)
            throw new Exception("the holder must actually get the robot");
        if (AvatarActions.EffectiveAvatar(n, holder) != AvatarKind.Screen)
            throw new Exception("a second character wanting the robot must fall back to an on-screen body, "
                + "not lose its body entirely - otherwise half the cast stops acting mid-scene");
        if (AvatarActions.EffectiveAvatar(doll, holder) != AvatarKind.Screen)
            throw new Exception("a screen character is unaffected by who holds the robot");

        // A character with no body keeps none - the fallback is for contention, not a promotion.
        var voiceOnly = new ChatAgent("v", "V", "m", "p");
        if (AvatarActions.EffectiveAvatar(voiceOnly, holder) != AvatarKind.None)
            throw new Exception("a text-only character must not be given a body it was never configured with");

        if (AvatarActions.SoleRobotHolder(new[] { doll, voiceOnly }) != null)
            throw new Exception("with nobody set to Reachy, nothing holds the robot");
        return Task.CompletedTask;
    }

    /// <summary>A body is enough to make asterisks mean action, with or without a scene.</summary>
    /// <remarks>
    /// 🔴 THE CONDITION GATES A DESTRUCTIVE OPERATION. In role-play a *span* is lifted out and never
    /// spoken; outside it the same span is markdown emphasis, and removing the words in "I'm *not* doing
    /// that" makes the voice say the OPPOSITE of the text on screen. So the switch must be ON for an
    /// embodied character - its actions are the entire reason it has a body - and OFF for a plain chat.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task ABodyOrASceneMakesItRolePlay()
    {
        var plain = new AgentRoom();
        plain.Agents.Add(new ChatAgent("a", "A", "m", "p"));
        if (plain.RolePlay)
            throw new Exception("a plain chat must NOT be role-play - emphasis would be stripped from "
                + "speech and the voice would say the opposite of the text");

        var scened = new AgentRoom { Scene = "A dark corridor." };
        scened.Agents.Add(new ChatAgent("a", "A", "m", "p"));
        if (!scened.RolePlay) throw new Exception("a scene must make it role-play");

        var embodied = new AgentRoom();
        embodied.Agents.Add(new ChatAgent("a", "A", "m", "p", null, null, AvatarKind.Screen));
        if (!embodied.RolePlay)
            throw new Exception("a character with a BODY must make it role-play even with no scene - "
                + "otherwise the avatar it was given never moves");

        var robot = new AgentRoom();
        robot.Agents.Add(new ChatAgent("a", "A", "m", "p", null, null, AvatarKind.Reachy));
        if (!robot.RolePlay) throw new Exception("a character driving the robot must make it role-play");

        // And the instruction only goes out when it applies, or every ordinary answer grows markup.
        var sys = plain.BuildPromptFor(plain.Agents[0]).First(m => m.Role == "system").Content;
        if (sys.Contains("asterisk", StringComparison.OrdinalIgnoreCase))
            throw new Exception("a plain chat was told to write actions in asterisks");
        var sysRp = embodied.BuildPromptFor(embodied.Agents[0]).First(m => m.Role == "system").Content;
        if (!sysRp.Contains("asterisk", StringComparison.OrdinalIgnoreCase))
            throw new Exception("an embodied character was never told the action convention, so it will "
                + "not produce any and the body will never move");
        return Task.CompletedTask;
    }
}
