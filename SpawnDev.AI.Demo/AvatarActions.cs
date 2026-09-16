using SpawnDev.Reachy;

namespace SpawnDev.AI.Demo;

/// <summary>
/// Where a character's body is: nowhere, on the screen, or the actual robot.
/// </summary>
/// <remarks>
/// Part of the persona because it is part of who the character IS - one of Aubs's cast can be the one that
/// drives the robot while the rest are drawn on screen, and that is a property of the character rather
/// than a mode the whole room is in.
/// </remarks>
public enum AvatarKind
{
    /// <summary>No body. The character is text and voice only.</summary>
    None = 0,

    /// <summary>An animated Reachy-shaped avatar drawn on the page.</summary>
    /// <remarks>
    /// Deliberately shaped like Reachy Mini, and driven by the SAME <see cref="Gesture"/> vocabulary: the
    /// robot exists once, so several characters in one scene need bodies that read the same way.
    /// </remarks>
    Screen = 1,

    /// <summary>The physical Reachy Mini.</summary>
    /// <remarks>
    /// ⚠️ There is ONE robot. Giving it to two characters at once would have them issuing conflicting
    /// moves to the same head, so the room hands it to at most one - see
    /// <see cref="AvatarActions.SoleRobotHolder"/>.
    /// </remarks>
    Reachy = 2,
}

/// <summary>
/// Turns what a character wrote into something a body can perform.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE VOCABULARY AND THE CLASSIFIER BOTH COME FROM <c>SpawnDev.Reachy</c>, ON PURPOSE. That SDK
/// already carries <see cref="Gesture"/>, <c>GestureClassifier</c> and <c>ReachyBody</c> - promoted out of
/// the Rose app precisely "so every host animates the robot identically" - and its gesture amplitudes are
/// measured on a real unit rather than chosen to look plausible.
/// </para>
/// <para>
/// ⚠️ A second vocabulary and a second classifier were written here first, and deleted on finding those.
/// Two classifiers is how a scene comes to read differently depending on whether the robot is plugged in,
/// which is the exact failure a shared vocabulary exists to prevent. If a phrasing classifies wrongly, fix
/// <c>GestureClassifier</c> in the SDK - never add a special case here.
/// </para>
/// </remarks>
public static class AvatarActions
{
    /// <summary>
    /// Every gesture a reply's stage directions describe, in order, skipping the ones nothing can perform.
    /// </summary>
    /// <remarks>
    /// Uses <c>SpokenText.Split</c> for the same reason the robot does: besides asterisked directions it
    /// also pulls out third-person prose that is really a stage direction ("His head bobs up and down"),
    /// which a model writes often and which would otherwise be spoken aloud.
    /// </remarks>
    public static List<Gesture> RecogniseAll(string? reply)
    {
        var (_, actions) = SpokenText.Split(reply);
        var found = new List<Gesture>();
        foreach (var action in actions)
        {
            var g = GestureClassifier.Classify(action);
            if (g != Gesture.None) found.Add(g);
        }
        return found;
    }

    /// <summary>
    /// Which character - if any - holds the one physical robot.
    /// </summary>
    /// <param name="agents">The room, in speaking order.</param>
    /// <returns>The id of the sole robot holder, or null when nobody is using it.</returns>
    /// <remarks>
    /// 🔴 THERE IS EXACTLY ONE REACHY MINI. Two characters both set to <see cref="AvatarKind.Reachy"/>
    /// would issue conflicting moves to the same head mid-scene, and the result is not a merge - it is
    /// whichever command landed last, with both characters appearing to act wrongly. The first in speaking
    /// order keeps it and the rest fall back to their on-screen bodies, which is a room that still works
    /// rather than one that refuses to start.
    /// </remarks>
    public static string? SoleRobotHolder(IEnumerable<ChatAgent> agents)
        => agents.FirstOrDefault(a => a.Avatar == AvatarKind.Reachy)?.Id;

    /// <summary>The body a character actually gets this round, given who already holds the robot.</summary>
    public static AvatarKind EffectiveAvatar(ChatAgent agent, string? robotHolderId)
        => agent.Avatar == AvatarKind.Reachy && agent.Id != robotHolderId
            ? AvatarKind.Screen      // somebody else has the robot - draw this one instead of dropping it
            : agent.Avatar;

    /// <summary>
    /// Whether the voice that is speaking right now should come out of the physical robot.
    /// </summary>
    /// <param name="agents">The room. Empty when the solo assistant is answering.</param>
    /// <param name="speakingAgentId">Who is talking, or null/empty for the solo assistant.</param>
    /// <returns>True when this speaker owns the robot for the current utterance.</returns>
    /// <remarks>
    /// 🔴 A CONNECTED ROBOT THAT NOBODY HOLDS IS STILL THE THING THE USER JUST CONNECTED. The rule used to
    /// require a room character explicitly assigned <see cref="AvatarKind.Reachy"/>, so pressing "Connect
    /// my Reachy" on a first visit did nothing audible: the robot linked, the reply came out of the page's
    /// own speakers, and nothing said why. Building a character and handing it the body is a configuration
    /// step standing in front of the demo's whole point.
    ///
    /// ⚠️ It is a PURE FUNCTION on purpose. This decision used to live inside the page, reading four
    /// instance fields, which meant the one rule that decides where a character's voice comes out could
    /// not be tested without a browser and a robot. The caller supplies connectedness; everything else
    /// about who wins is here.
    /// </remarks>
    public static bool SpeakerDrivesRobot(IEnumerable<ChatAgent> agents, string? speakingAgentId)
    {
        var list = agents as IReadOnlyList<ChatAgent> ?? agents.ToList();
        var holder = SoleRobotHolder(list);

        // Nobody in the room claimed it - whoever is talking gets it, including the solo assistant, which
        // has no agent id at all.
        if (holder == null) return true;

        // Somebody did. Only they may drive it, and only while they are the one speaking - otherwise two
        // characters would issue conflicting moves to one head.
        if (string.IsNullOrEmpty(speakingAgentId)) return false;
        var agent = list.FirstOrDefault(a => a.Id == speakingAgentId);
        return agent != null && EffectiveAvatar(agent, holder) == AvatarKind.Reachy;
    }
}
