namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// An assistant with a body acts out what it writes - without the voice mangling what it says.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 THE REPORT. Captain: "the avatar seem to be 100% static, never changes. does the ai know to use it?
/// every ai should have and use an avatar by default (unless specifically turned off for that persona)."
/// Giving the default assistant a working body means ordinary question-and-answer chat is now embodied,
/// and that is exactly where the two existing asterisk rules are each wrong:
/// </para>
/// <list type="bullet">
/// <item><c>SpokenText.Split</c> (the scene rule) lifts EVERY marked span out of speech, so "I'm *not*
/// doing that" is spoken as "I'm doing that" - the opposite of the text on screen, silently.</item>
/// <item><c>StageDirections.Unmark</c> (the plain-chat rule) keeps every word, so "*tilts head*" is read
/// aloud as "tilts head".</item>
/// </list>
/// <para>
/// ⚠️ THE INVERSION IS THE ONE THAT CANNOT BE HEARD AS A BUG. A voice saying "tilts head" is obviously
/// wrong to anybody listening. A voice confidently saying the opposite of the screen is not obviously
/// anything - which is why the negation cases below are the centre of this class rather than an edge.
/// </para>
/// <para>
/// ⚠️ NOT a test of <c>GestureClassifier</c>: which words map to which gesture is the SDK's business.
/// What is pinned here is SPEAK vs PERFORM, which is this layer's decision.
/// </para>
/// </remarks>
public sealed class EmbodiedSpeechTests
{
    /// <summary>A written action is performed, not spoken.</summary>
    [AiTest(Timeout = 30_000)]
    public Task AnActionIsPerformedAndNotSpoken()
    {
        var (spoken, actions) = StageDirections.SplitForBody("*tilts head* That is a good question.");
        if (actions.Count != 1 || actions[0] != "tilts head")
            throw new Exception($"the action was lost: [{string.Join("|", actions)}]");
        if (spoken.Contains("tilts", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"the stage direction reached the synthesiser: \"{spoken}\"");
        if (!spoken.Contains("good question"))
            throw new Exception($"the dialogue was lost with the action: \"{spoken}\"");

        // Several, in order - a body that nods then shakes means something specific.
        var (_, two) = StageDirections.SplitForBody("*nods* Yes. *antennae droop* Or maybe not.");
        if (two.Count != 2 || two[0] != "nods" || two[1] != "antennae droop")
            throw new Exception($"order or content wrong: [{string.Join("|", two)}]");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Emphasis keeps its words, so the voice never says the opposite of the screen.
    /// </summary>
    /// <remarks>
    /// 🔴 RED-CHECK NOTE: run these strings through <c>SpawnDev.Reachy.SpokenText.Split</c> and every one
    /// of them loses the emphasised words - that is the behaviour this function exists to not have, and
    /// the reason the demo could not simply adopt the scene rule everywhere.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task EmphasisIsSpokenInFull()
    {
        foreach (var (input, mustContain) in new[]
        {
            ("I'm *not* doing that.", "not"),
            ("That is *really not* what I said.", "really not"),
            ("You should *never* run that command.", "never"),
            ("It is *very* fast.", "very"),
        })
        {
            var (spoken, actions) = StageDirections.SplitForBody(input);
            if (!spoken.Contains(mustContain, StringComparison.Ordinal))
                throw new Exception($"\"{input}\" lost \"{mustContain}\" from the spoken text: \"{spoken}\" "
                    + "- the voice would say the opposite of what is on screen");
            if (actions.Count != 0)
                throw new Exception($"\"{input}\" produced a phantom action: [{string.Join("|", actions)}]");
        }
        return Task.CompletedTask;
    }

    /// <summary>Markdown bold and arithmetic are left completely alone.</summary>
    /// <remarks>
    /// ⚠️ The pairing rule is markdown's own, and it is what stops "2 * 3 * 4" from swallowing the text
    /// between the asterisks. A model writing a multiplication into an answer is not rare.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task BoldAndArithmeticSurvive()
    {
        var (bold, boldActions) = StageDirections.SplitForBody("This is **important** to know.");
        if (!bold.Contains("important")) throw new Exception($"bold text was eaten: \"{bold}\"");
        if (boldActions.Count != 0) throw new Exception("** produced an action");

        var (math, mathActions) = StageDirections.SplitForBody("The answer is 2 * 3 * 4 = 24.");
        if (!math.Contains("24")) throw new Exception($"arithmetic was mangled: \"{math}\"");
        if (mathActions.Count != 0)
            throw new Exception($"arithmetic produced an action: [{string.Join("|", mathActions)}]");

        var (plain, none) = StageDirections.SplitForBody("No markers here at all.");
        if (plain != "No markers here at all." || none.Count != 0)
            throw new Exception($"plain text was altered: \"{plain}\"");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every action the split produces can actually drive the body.
    /// </summary>
    /// <remarks>
    /// 🔴 THE HALF THAT WOULD PASS EVERYTHING ABOVE AND STILL LEAVE THE AVATAR STILL. Lifting a direction
    /// out of the speech and having no gesture to perform for it is the reported symptom exactly: the
    /// words vanish from the voice AND nothing moves. This asserts the two halves agree - that what the
    /// default prompt asks the model to write is what the classifier recognises.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task TheActionsAskedForInThePromptArePerformable()
    {
        // The vocabulary the default system prompt (Home.BodyInstructions) tells the model to use.
        string[] asked =
        [
            "*nods*", "*shakes head*", "*tilts head*", "*looks up*", "*looks down*",
            "*leans in*", "*turns to face you*", "*antennae perk up*", "*antennae wiggle*",
            "*antennae droop*",
        ];
        var unperformable = new List<string>();
        foreach (var line in asked)
        {
            var (_, actions) = StageDirections.SplitForBody(line);
            if (actions.Count == 0) { unperformable.Add($"{line} (not lifted from speech)"); continue; }
            if (SpawnDev.Reachy.GestureClassifier.Classify(actions[0]) == SpawnDev.Reachy.Gesture.None)
                unperformable.Add($"{line} (lifted, but no gesture)");
        }
        if (unperformable.Count > 0)
            throw new Exception("the prompt asks for actions the body cannot perform, so they would be "
                + "removed from the speech and animate nothing: " + string.Join("; ", unperformable));
        return Task.CompletedTask;
    }

    /// <summary>A body is the default for a character, and turning it off is explicit.</summary>
    /// <remarks>
    /// Captain: "every ai should have and use an avatar by default (unless specifically turned off for
    /// that persona)." This is one field's default, which is precisely the kind of thing that gets
    /// reverted by an unrelated edit and noticed by nobody.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task ACharacterHasABodyUnlessItIsTurnedOff()
    {
        var agent = new ChatAgent("id", "Nova", "m", "persona");
        if (agent.Avatar != AvatarKind.Screen)
            throw new Exception($"a new character defaulted to {agent.Avatar}, not Screen");

        var saved = new SavedCharacter("id", "Nova", "p", "m", null, DateTime.UtcNow);
        if (saved.Avatar != AvatarKind.Screen)
            throw new Exception($"a saved character defaulted to {saved.Avatar}, not Screen - a character "
                + "stored before this field existed would read back with no body");

        // And the opt-out still works, or "unless specifically turned off" is not true.
        var text = new ChatAgent("id", "Quiet", "m", "p", Avatar: AvatarKind.None);
        if (text.Avatar != AvatarKind.None) throw new Exception("a character cannot be made text-only");
        return Task.CompletedTask;
    }
}
