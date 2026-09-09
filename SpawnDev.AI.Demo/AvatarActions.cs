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
    /// The default for a character with a body, and deliberately shaped like Reachy Mini: the same actions
    /// have to read the same way whether they end up on screen or in the robot, so several characters can
    /// share a scene when there is only ever one physical robot.
    /// </remarks>
    Screen = 1,

    /// <summary>The physical Reachy Mini.</summary>
    /// <remarks>
    /// ⚠️ There is ONE robot. Giving it to two characters at once would have them fighting over the same
    /// head, so the room hands it to at most one - see <see cref="AvatarActions.SoleRobotHolder"/>.
    /// </remarks>
    Reachy = 2,
}

/// <summary>
/// The canonical things a character can be seen to DO, small enough that every backend can honour all of
/// them.
/// </summary>
/// <remarks>
/// 🔴 A SHARED VOCABULARY IS THE WHOLE DESIGN. A model writes free text - "*tilts her head*", "*glances
/// over*" - and there are two very different things that might act it out. If each backend interpreted the
/// raw text itself they would drift apart, and a scene would read differently depending on whether the
/// robot happened to be plugged in. Everything is mapped to these few actions once, and a backend only has
/// to know how to perform them.
/// <para>
/// Deliberately small, and chosen to be things Reachy Mini can actually do with a head and two antennae.
/// An action the robot cannot perform would have to be silently dropped there, and the same scene would
/// then play differently on screen - so the vocabulary is the intersection, not the union.
/// </para>
/// </remarks>
public enum AvatarAction
{
    /// <summary>Nothing recognisable - the body stays as it is.</summary>
    None = 0,
    /// <summary>A small head tilt, the "curious" pose.</summary>
    Tilt,
    /// <summary>Yes.</summary>
    Nod,
    /// <summary>No.</summary>
    Shake,
    /// <summary>Turn to take in the surroundings.</summary>
    LookAround,
    /// <summary>Attention toward the person being spoken to.</summary>
    LookAt,
    /// <summary>Antennae up: interest, surprise, delight.</summary>
    Perk,
    /// <summary>Antennae down: sadness, defeat, sulking.</summary>
    Droop,
    /// <summary>A greeting - antennae waggle, since there are no arms.</summary>
    Wave,
    /// <summary>Recoil: fear, disgust, alarm.</summary>
    Startle,
    /// <summary>Amusement.</summary>
    Laugh,
    /// <summary>A shrug, rendered as a shoulderless body can manage it.</summary>
    Shrug,
}

/// <summary>
/// Turns what a character wrote into something a body can perform.
/// </summary>
public static class AvatarActions
{
    // Ordered most-specific first: "looks around" must be tested before "look", or a scan of the room
    // becomes a glance at the user. Matching is on substrings because a model writes "tilts her head
    // slightly", never a keyword.
    private static readonly (string[] Words, AvatarAction Action)[] Vocabulary =
    {
        (new[] { "look around", "looks around", "glances around", "scans", "surveys" }, AvatarAction.LookAround),
        (new[] { "tilt", "cocks her head", "cocks his head", "cocks its head", "quizzical" }, AvatarAction.Tilt),
        (new[] { "nod", "agrees" }, AvatarAction.Nod),
        (new[] { "shake", "shakes head", "disagrees", "refuses" }, AvatarAction.Shake),
        (new[] { "wave", "waves", "greets", "salutes" }, AvatarAction.Wave),
        (new[] { "perk", "brighten", "lights up", "excited", "grins", "smiles", "beams" }, AvatarAction.Perk),
        (new[] { "droop", "sighs", "slumps", "deflates", "sulks", "frowns", "sad" }, AvatarAction.Droop),
        (new[] { "startle", "jumps", "recoils", "flinches", "gasps", "alarmed" }, AvatarAction.Startle),
        (new[] { "laugh", "giggles", "chuckles", "snickers", "cackles" }, AvatarAction.Laugh),
        (new[] { "shrug" }, AvatarAction.Shrug),
        (new[] { "look", "glance", "stares", "watches", "turns to", "peers" }, AvatarAction.LookAt),
    };

    /// <summary>
    /// The action a written stage direction describes, or <see cref="AvatarAction.None"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Returns None rather than guessing when nothing matches. A body that performs a random motion for
    /// text it did not understand is worse than one that stays still: the motion reads as meaning something,
    /// and in a scene that is a lie about what the character did.
    /// </remarks>
    public static AvatarAction Recognise(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return AvatarAction.None;
        var text = direction.ToLowerInvariant();
        foreach (var (words, action) in Vocabulary)
            foreach (var w in words)
                if (text.Contains(w, StringComparison.Ordinal)) return action;
        return AvatarAction.None;
    }

    /// <summary>Every action described by a reply's stage directions, in order, skipping unrecognised ones.</summary>
    public static List<AvatarAction> RecogniseAll(string? reply)
        => StageDirections.Split(reply).Actions
            .Select(a => Recognise(a.Text))
            .Where(a => a != AvatarAction.None)
            .ToList();

    /// <summary>
    /// Which character - if any - holds the one physical robot.
    /// </summary>
    /// <param name="agents">The room, in speaking order.</param>
    /// <returns>The id of the sole robot holder, or null when nobody is using it.</returns>
    /// <remarks>
    /// 🔴 THERE IS EXACTLY ONE ROBOT. Two characters both set to <see cref="AvatarKind.Reachy"/> would
    /// issue conflicting moves to the same head mid-scene, and the result is not a merge - it is whichever
    /// command landed last, with both characters appearing to act wrongly. The first in speaking order
    /// keeps it and the rest fall back to their on-screen bodies, which is a room that still works rather
    /// than one that refuses to start.
    /// </remarks>
    public static string? SoleRobotHolder(IEnumerable<ChatAgent> agents)
        => agents.FirstOrDefault(a => a.Avatar == AvatarKind.Reachy)?.Id;

    /// <summary>The body a character actually gets this round, given who already holds the robot.</summary>
    public static AvatarKind EffectiveAvatar(ChatAgent agent, string? robotHolderId)
        => agent.Avatar == AvatarKind.Reachy && agent.Id != robotHolderId
            ? AvatarKind.Screen      // somebody else has the robot - draw this one instead of dropping it
            : agent.Avatar;
}
