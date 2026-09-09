namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// What a character DOES is separated from what it SAYS, without eating any of the speech.
/// </summary>
/// <remarks>
/// ⚠️ NOT heavy - pure text, no model. Both directions of failure are silent to a listener: leave the
/// markers in and the voice reads "asterisk tilts head asterisk"; strip too eagerly and words disappear
/// from the middle of a sentence with nothing to indicate one ever existed.
/// </remarks>
public sealed class StageDirectionTests
{
    /// <summary>Actions come out, dialogue stays, and the spacing is repaired.</summary>
    [AiTest(Timeout = 30_000)]
    public Task ActionsAreLiftedOutAndTheDialogueIsIntact()
    {
        Speaks("*tilts head* Fine, whatever.", "Fine, whatever.", "tilts head");
        Speaks("Fine. *shrugs*", "Fine.", "shrugs");
        // In a SCENE, asterisks mean action - that is what the model was told to do with them.
        Speaks("*grabs the rail* Hold on.", "Hold on.", "grabs the rail");
        Speaks("*waves* Hi there. *smiles*", "Hi there.", "waves", "smiles");
        Speaks("Nothing to see here.", "Nothing to see here.");

        // OBSERVED IN THE ROOM GATE (qwen2.5-0.5b): the action comes first and the dialogue continues from
        // it with a comma, so lifting the action out would open the spoken line on ", could you...".
        Speaks("*looks around*, could you clarify?", "could you clarify?", "looks around");
        Speaks("*sighs*. Fine.", "Fine.", "sighs");

        // Removing an action from mid-sentence must not leave a double space or weld two words together.
        var (mid, _) = StageDirections.Split("He looked up *slowly* and spoke.");
        if (mid != "He looked up and spoke.")
            throw new Exception($"spacing was not repaired: \"{mid}\"");

        // ...nor leave a gap in front of the punctuation that followed it.
        var (punct, _) = StageDirections.Split("Fine *sighs*.");
        if (punct != "Fine.") throw new Exception($"space left before punctuation: \"{punct}\"");

        // An action-only reply has nothing to say. The caller must be able to tell, so it can stay silent
        // deliberately instead of sending an empty string to the synthesiser.
        var (silent, acts) = StageDirections.Split("*stares in silence*");
        if (silent.Length != 0 || acts.Count != 1)
            throw new Exception($"action-only reply gave spoken=\"{silent}\", {acts.Count} action(s)");
        return Task.CompletedTask;
    }

    /// <summary>Text that merely contains an asterisk is not mistaken for an action.</summary>
    /// <remarks>
    /// 🔴 THE EXPENSIVE HALF. A parser that pairs each asterisk with the next one deletes everything
    /// between them, so "2 * 3 * 4" loses " 3 " and a single dangling asterisk swallows the whole rest of
    /// the reply. The listener hears a sentence with a hole in it and there is no error anywhere.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AsterisksThatAreNotActionsAreLeftAlone()
    {
        // Markdown bold. Models emit it constantly; treating it as action silently deletes emphasised
        // words from what gets spoken.
        Speaks("This is **very** important.", "This is **very** important.");
        Speaks("**Bold** start.", "**Bold** start.");

        // Arithmetic: the space after the asterisk means it cannot open emphasis, so nothing pairs.
        Speaks("Multiply 2 * 3 * 4 to get 24.", "Multiply 2 * 3 * 4 to get 24.");

        // A lone asterisk has no partner and must not consume the rest of the sentence.
        Speaks("A lone * asterisk stays put.", "A lone * asterisk stays put.");
        Speaks("Unclosed *action that never ends", "Unclosed *action that never ends");

        Speaks("", "");
        Speaks(null, "");
        return Task.CompletedTask;
    }

    /// <summary>Outside a scene the markers go but the WORDS stay - meaning is never altered.</summary>
    /// <remarks>
    /// 🔴 THE WORST BUG THIS FILE PREVENTS, and it is a silent one. A single asterisk is ordinary markdown
    /// emphasis. Lifting <c>*not*</c> out of "I'm *not* doing that" as though it were an action makes the
    /// voice say "I'm doing that" - **the opposite of the text on screen** - and nothing reports that a
    /// word was dropped. So outside a scene the words always survive; only the markers go, which is still
    /// enough to stop the synthesiser reading an asterisk aloud.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task OutsideASceneEmphasisKeepsItsWords()
    {
        Unmarks("I'm *not* doing that.", "I'm not doing that.");
        Unmarks("That is *really* important.", "That is really important.");
        Unmarks("No markup here.", "No markup here.");
        // Bold survives untouched either way - it is not emphasis this layer owns.
        Unmarks("This is **very** important.", "This is **very** important.");
        // And the things that are not markers at all are still left alone.
        Unmarks("Multiply 2 * 3 * 4 to get 24.", "Multiply 2 * 3 * 4 to get 24.");
        Unmarks("Unclosed *emphasis that never ends", "Unclosed *emphasis that never ends");
        Unmarks("", "");
        Unmarks(null, "");

        // The whole point: an asterisk never reaches the synthesiser as a spoken character.
        var spoken = StageDirections.Unmark("*Absolutely* not.");
        if (spoken.Contains('*'))
            throw new Exception($"an asterisk survived into spoken text: \"{spoken}\"");
        return Task.CompletedTask;
    }

    private static void Unmarks(string? input, string expected)
    {
        var got = StageDirections.Unmark(input);
        if (got != expected)
            throw new Exception($"Unmark(\"{input}\") = \"{got}\", expected \"{expected}\"");
    }

    private static void Speaks(string? input, string expectedSpoken, params string[] expectedActions)
    {
        var (spoken, actions) = StageDirections.Split(input);
        if (spoken != expectedSpoken)
            throw new Exception($"Split(\"{input}\").Spoken = \"{spoken}\", expected \"{expectedSpoken}\"");
        var got = actions.Select(a => a.Text).ToArray();
        if (!got.SequenceEqual(expectedActions))
            throw new Exception($"Split(\"{input}\") actions = [{string.Join(", ", got)}], "
                + $"expected [{string.Join(", ", expectedActions)}]");
    }
}
