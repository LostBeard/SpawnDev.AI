namespace SpawnDev.AI.Demo.Tests;

/// <summary>
/// Outside role-play the asterisk markers go but every WORD stays.
/// </summary>
/// <remarks>
/// 🔴 THE WORST BUG THIS PREVENTS IS SILENT. A single asterisk is ordinary markdown emphasis. Lifting
/// <c>*not*</c> out of "I'm *not* doing that" as though it were a stage direction makes the voice say "I'm
/// doing that" - <b>the opposite of the text on screen</b> - and nothing reports that a word was dropped.
/// <para>
/// ⚠️ The role-play direction is <c>SpawnDev.Reachy.SpokenText.Split</c> and is NOT retested here; it is
/// the SDK's, exercised against real model output there. This covers only the half the SDK has no
/// equivalent for, because Rose is always in character and never needed it.
/// </para>
/// </remarks>
public sealed class StageDirectionTests
{
    /// <summary>Emphasis keeps its words; the markers never reach the synthesiser.</summary>
    [AiTest(Timeout = 30_000)]
    public Task EmphasisKeepsItsWordsOutsideRolePlay()
    {
        Unmarks("I'm *not* doing that.", "I'm not doing that.");
        Unmarks("That is *really* important.", "That is really important.");
        Unmarks("No markup here.", "No markup here.");
        // Bold is not this layer's business and must survive untouched.
        Unmarks("This is **very** important.", "This is **very** important.");
        Unmarks("", "");
        Unmarks(null, "");

        var spoken = StageDirections.Unmark("*Absolutely* not.");
        if (spoken.Contains('*'))
            throw new Exception($"an asterisk survived into spoken text: \"{spoken}\"");
        return Task.CompletedTask;
    }

    /// <summary>Text that merely contains an asterisk is not treated as a marker.</summary>
    /// <remarks>
    /// 🔴 A parser that pairs each asterisk with the next one deletes everything between them, so
    /// "2 * 3 * 4" loses " 3 " and a lone dangling asterisk swallows the rest of the reply. The listener
    /// hears a sentence with a hole in it and there is no error anywhere.
    /// </remarks>
    [AiTest(Timeout = 30_000)]
    public Task AsterisksThatAreNotMarkersAreLeftAlone()
    {
        Unmarks("Multiply 2 * 3 * 4 to get 24.", "Multiply 2 * 3 * 4 to get 24.");
        Unmarks("A lone * asterisk stays put.", "A lone * asterisk stays put.");
        Unmarks("Unclosed *emphasis that never ends", "Unclosed *emphasis that never ends");
        return Task.CompletedTask;
    }

    private static void Unmarks(string? input, string expected)
    {
        var got = StageDirections.Unmark(input);
        if (got != expected)
            throw new Exception($"Unmark(\"{input}\") = \"{got}\", expected \"{expected}\"");
    }
}
