namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// What a character writes becomes an action a body can perform - the same one, whichever body it is.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - pure mapping, no model. It is the join between two very different backends (an on-screen
/// avatar and the physical Reachy Mini), and its failure mode is a scene that plays differently depending
/// on whether the robot happens to be plugged in. That is not something watching either one alone reveals.
/// </remarks>
public sealed class AvatarActionTests
{
    /// <summary>Written actions map to the shared vocabulary, and unknown ones map to nothing.</summary>
    [AiTest(Timeout = 30_000)]
    public Task WrittenActionsBecomeMotions()
    {
        Maps("tilts her head", AvatarAction.Tilt);
        Maps("nods slowly", AvatarAction.Nod);
        Maps("shakes his head", AvatarAction.Shake);
        Maps("waves", AvatarAction.Wave);
        Maps("sighs and slumps", AvatarAction.Droop);
        Maps("grins", AvatarAction.Perk);
        Maps("recoils in alarm", AvatarAction.Startle);
        Maps("giggles", AvatarAction.Laugh);
        Maps("shrugs", AvatarAction.Shrug);

        // 🔴 ORDERING WITHIN THE VOCABULARY MATTERS. "looks around" contains "look", so a table tested in
        // the wrong order turns scanning the room into a glance at the user - a plausible-looking motion
        // that means something different from what the character wrote.
        Maps("looks around the room", AvatarAction.LookAround);
        Maps("looks at Uzi", AvatarAction.LookAt);

        // Nothing recognisable performs NOTHING. A random motion for unparsed text reads as meaning
        // something, which in a scene is a lie about what the character did.
        Maps("recalibrates her flux inverter", AvatarAction.None);
        Maps("", AvatarAction.None);
        Maps(null, AvatarAction.None);
        return Task.CompletedTask;
    }

    /// <summary>A whole reply yields its motions in order, ignoring the ones nothing can perform.</summary>
    [AiTest(Timeout = 30_000)]
    public Task ARepliesActionsComeOutInOrder()
    {
        var got = AvatarActions.RecogniseAll("*waves* Hey. *tilts head* You okay? *recalibrates something*");
        var expected = new[] { AvatarAction.Wave, AvatarAction.Tilt };
        if (!got.SequenceEqual(expected))
            throw new Exception($"got [{string.Join(",", got)}], expected [{string.Join(",", expected)}]");

        if (AvatarActions.RecogniseAll("Just talking, no actions at all.").Count != 0)
            throw new Exception("plain dialogue must produce no motion");
        return Task.CompletedTask;
    }

    /// <summary>Only one character can hold the single physical robot; the rest still get a body.</summary>
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

    private static void Maps(string? written, AvatarAction expected)
    {
        var got = AvatarActions.Recognise(written);
        if (got != expected)
            throw new Exception($"Recognise(\"{written}\") = {got}, expected {expected}");
    }
}
