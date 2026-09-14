using System.Text;

namespace SpawnDev.AI.Demo;

/// <summary>
/// The NON-role-play half of asterisk handling: drop the markers, keep every word.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 WHY THIS IS NOT <c>SpokenText.Split</c>. The SDK's splitter treats any <c>*…*</c> span as a stage
/// direction and removes it, which is exactly right for Rose - every persona there is told to narrate its
/// actions that way, so a lone <c>*word*</c> always IS an action. This demo also holds ordinary,
/// non-role-play conversations, and in those a single asterisk is markdown emphasis. Removing the words in
/// "I'm *not* doing that" makes the synthesiser say "I'm doing that" - <b>the opposite of the text on
/// screen</b> - with nothing anywhere reporting that a word was dropped.
/// </para>
/// <para>
/// So the demo chooses by context: role-play (a scene is set, or a character has a body) uses
/// <c>SpokenText.Split</c> and the actions drive a body; otherwise this runs and only the markers go.
/// Either way an asterisk never reaches the synthesiser.
/// </para>
/// <para>
/// ⚠️ CANDIDATE TO PROMOTE. <c>SpawnDev.Reachy</c> has no equivalent because Rose never needed one. If
/// another host wants both modes, this belongs beside <c>SpokenText.Split</c> in the SDK rather than
/// copied a second time.
/// </para>
/// </remarks>
public static class StageDirections
{
    /// <summary>
    /// Remove the asterisk markers but KEEP the words.
    /// </summary>
    /// <remarks>
    /// The pairing rule is markdown's own - an opening marker is followed by non-space, a closing one
    /// preceded by non-space - which is what stops arithmetic ("2 * 3 * 4") and a lone dangling asterisk
    /// from swallowing the text around them.
    /// </remarks>
    public static string Unmark(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('*')) return text ?? "";

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '*' && OpensHere(text, i))
            {
                int close = FindClose(text, i + 1);
                if (close >= 0)
                {
                    sb.Append(text, i + 1, close - i - 1);   // the words, without the markers
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(text[i++]);
        }
        return Tidy(sb.ToString());
    }

    /// <summary>
    /// Split a reply into what to SAY and what to DO, for an assistant that has a body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 WHY NOT JUST <c>SpokenText.Split</c>. Captain: "every ai should have and use an avatar by
    /// default". That makes ordinary question-and-answer chat embodied, and the SDK splitter treats EVERY
    /// <c>*…*</c> and <c>_…_</c> span as a stage direction. In a scene that is right, because the persona
    /// was told to write actions that way. In plain chat a single asterisk is markdown emphasis, and
    /// lifting it out of "I'm *not* doing that" makes the voice say <b>the opposite of the text on
    /// screen</b>, silently. A body is not worth that.
    /// </para>
    /// <para>
    /// 🔴 WHY NOT JUST <see cref="Unmark"/> EITHER. Keeping every word means an assistant told to write
    /// "*tilts head*" has the synthesiser read "tilts head" aloud. Both existing halves are wrong for an
    /// embodied assistant in ordinary conversation, which is what this app now is by default.
    /// </para>
    /// <para>
    /// THE RULE, and it is deliberately conservative in one direction: a marked span becomes an ACTION
    /// when <c>GestureClassifier</c> recognises a gesture in it (the body can actually perform it), or
    /// when it is more than one word and contains no negation. Otherwise the words are kept and spoken.
    /// Keeping a word that should have been silent is a small wrongness the user hears; dropping a word
    /// that carried the meaning is a large one they never detect.
    /// </para>
    /// <para>
    /// ⚠️ THE NEGATION GUARD IS THE WHOLE POINT of the second clause. Emphasis is nearly always a single
    /// word ("*not*", "*very*"), which the first clause already keeps - but "*really not*" is two, and
    /// dropping it inverts the sentence exactly like the single-word case does. Four words of guard cost
    /// nothing and close the only severe failure this function has.
    /// </para>
    /// <para>
    /// ⚠️ NO NEW VOCABULARY. Which gesture a direction describes is <c>SpawnDev.Reachy</c>'s question and
    /// stays there - a second classifier is how a scene comes to read differently depending on whether the
    /// robot is plugged in. This only decides SPEAK or PERFORM.
    /// </para>
    /// </remarks>
    /// <returns>The words to synthesise, and the stage directions to perform, in order.</returns>
    public static (string Spoken, List<string> Actions) SplitForBody(string? text)
    {
        var actions = new List<string>();
        if (string.IsNullOrEmpty(text)) return ("", actions);
        if (!text.Contains('*')) return (text, actions);

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '*' && OpensHere(text, i))
            {
                int close = FindClose(text, i + 1);
                if (close >= 0)
                {
                    var span = text[(i + 1)..close];
                    if (IsPerformable(span)) actions.Add(span.Trim());
                    else sb.Append(span);            // ordinary emphasis - keep every word
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(text[i++]);
        }
        return (Tidy(sb.ToString()), actions);
    }

    /// <summary>
    /// Would this marked span be performed as a stage direction rather than spoken?
    /// </summary>
    /// <remarks>
    /// 🔴 EXPOSED SO THE TRANSCRIPT AND THE VOICE AGREE. Captain: "the stage direction is visible in the
    /// chat (not sure if that is intentional)". It was not - the renderer knew about <c>**bold**</c> and
    /// nothing else, so a direction the body performed and the voice skipped still appeared in the bubble
    /// as literal asterisks. Styling it needs the SAME decision <see cref="SplitForBody"/> makes, or the
    /// page would italicise something the voice reads out, or read out something the page shows as an
    /// action. One predicate, both callers.
    /// </remarks>
    public static bool IsStageDirection(string? span) => span != null && IsPerformable(span);

    /// <summary>Negations, because dropping one inverts the sentence - see <see cref="SplitForBody"/>.</summary>
    private static readonly string[] Negations = ["not", "never", "n't", " no "];

    /// <summary>Is this marked span a stage direction rather than emphasis?</summary>
    private static bool IsPerformable(string span)
    {
        var s = span.Trim();
        if (s.Length == 0) return false;

        // The body can actually do this - it is an action whatever else it looks like.
        if (SpawnDev.Reachy.GestureClassifier.Classify(s) != SpawnDev.Reachy.Gesture.None) return true;

        // One word and unrecognised: emphasis. Speak it.
        if (!s.Contains(' ')) return false;

        // Multi-word: a stage direction, unless dropping it would change what was said.
        var padded = $" {s.ToLowerInvariant()} ";
        foreach (var n in Negations)
            if (padded.Contains(n, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>A single asterisk (not part of <c>**</c>) followed by a non-space: an opening marker.</summary>
    private static bool OpensHere(string s, int i)
    {
        if (i + 1 >= s.Length) return false;
        if (s[i + 1] == '*') return false;                       // ** - markdown bold, leave it alone
        if (i > 0 && s[i - 1] == '*') return false;              // the second star of a **
        return !char.IsWhiteSpace(s[i + 1]);
    }

    /// <summary>Index of the closing marker: a single asterisk preceded by a non-space.</summary>
    private static int FindClose(string s, int from)
    {
        for (int j = from; j < s.Length; j++)
        {
            if (s[j] != '*') continue;
            if (j + 1 < s.Length && s[j + 1] == '*') continue;    // part of a **
            if (char.IsWhiteSpace(s[j - 1])) continue;            // "* " never closes emphasis
            return j;
        }
        return -1;
    }

    /// <summary>Collapse the space runs removal leaves behind, and tidy punctuation around the gap.</summary>
    private static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == ' ' && sb.Length > 0 && sb[^1] == ' ') continue;
            if ((c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':')
                && sb.Length > 0 && sb[^1] == ' ') sb.Length--;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
