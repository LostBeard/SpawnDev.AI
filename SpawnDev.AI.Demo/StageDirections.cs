using System.Text;

namespace SpawnDev.AI.Demo;

/// <summary>
/// A physical action a character performed, written beside what it said - "tilts head", "waves".
/// </summary>
/// <param name="Text">The action itself, without the surrounding markers.</param>
/// <param name="Index">Where it appeared in the spoken text, so an avatar can act at the right moment.</param>
public sealed record StageDirection(string Text, int Index);

/// <summary>
/// Separates what a character SAYS from what it DOES.
/// </summary>
/// <remarks>
/// <para>
/// In role-play a model writes physical action between asterisks - <c>*tilts head*</c> - and that
/// convention is the natural place to drive an avatar or a robot from: the model is already producing the
/// actions, so nothing has to be bolted on to ask for them.
/// </para>
/// <para>
/// 🔴 IT IS ALSO A BUG IF NOBODY SEPARATES THEM. The TTS reads whatever it is given, so a character in a
/// scene says, out loud, "asterisk tilts head asterisk" - or worse, the words with no pause and no
/// indication they were never spoken. Splitting is not a nicety for the avatar; it is what makes spoken
/// role-play listenable at all.
/// </para>
/// <para>
/// ⚠️ <c>**bold**</c> IS NOT AN ACTION. Models emit markdown emphasis constantly, and treating a double
/// asterisk as a stage direction would silently delete emphasised words from what gets spoken - the
/// listener hears a sentence with holes in it and nothing indicates why.
/// </para>
/// </remarks>
public static class StageDirections
{
    /// <summary>
    /// Split a reply into the words to speak and the actions to perform.
    /// </summary>
    /// <param name="text">The character's full reply, as written.</param>
    /// <returns>
    /// <c>Spoken</c> is the reply with actions removed and spacing repaired; <c>Actions</c> is what it did,
    /// in order. Text with no actions comes back with <c>Spoken</c> equal to the input.
    /// </returns>
    /// <remarks>
    /// The pairing rule is markdown's own: an opening marker is followed by non-space and a closing marker
    /// is preceded by non-space. That is what keeps arithmetic ("2 * 3 * 4") and a lone dangling asterisk
    /// from swallowing the sentence around them - a parser that just matched the next asterisk would delete
    /// real speech, and the listener would never know a word was missing.
    /// </remarks>
    public static (string Spoken, IReadOnlyList<StageDirection> Actions) Split(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('*'))
            return (text ?? "", Array.Empty<StageDirection>());

        var spoken = new StringBuilder(text.Length);
        var actions = new List<StageDirection>();
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] != '*' || !OpensHere(text, i)) { spoken.Append(text[i++]); continue; }

            int close = FindClose(text, i + 1);
            if (close < 0) { spoken.Append(text[i++]); continue; }   // unmatched - it is just an asterisk

            var action = text[(i + 1)..close].Trim();
            if (action.Length > 0) actions.Add(new StageDirection(action, spoken.Length));
            i = close + 1;

            // The action sat between words; leaving both spaces gives the synthesiser a double gap, and
            // eating both runs the words together. Collapse to exactly one, and to none at a boundary.
            while (i < text.Length && text[i] == ' ' && spoken.Length > 0 && spoken[^1] == ' ') i++;
        }

        return (Tidy(spoken.ToString()), actions);
    }

    /// <summary>Just the words to speak - the common case at a TTS call site.</summary>
    public static string Spoken(string? text) => Split(text).Spoken;

    /// <summary>
    /// Remove the asterisk markers but KEEP the words, for text that is not role-play.
    /// </summary>
    /// <remarks>
    /// 🔴 THE REASON THIS EXISTS INSTEAD OF ALWAYS CALLING <see cref="Split"/>. A single asterisk is
    /// ordinary markdown emphasis, and outside a scene that is what it almost always is. Treating
    /// <c>I'm *not* doing that</c> as an action does not merely drop a flourish - the synthesiser then says
    /// "I'm doing that", **the opposite of what is on screen**, with nothing anywhere to indicate a word
    /// was removed. Dropping the markers and speaking the words is wrong in no case; dropping the words is
    /// catastrophic in this one.
    /// <para>
    /// So the two are chosen between by CONTEXT: a scene means the model was asked for the action
    /// convention and <see cref="Split"/> applies; with no scene there is nothing to act out, and this
    /// does. Either way the synthesiser never reads an asterisk aloud.
    /// </para>
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

    /// <summary>A single asterisk (not part of <c>**</c>) followed by a non-space: an opening marker.</summary>
    private static bool OpensHere(string s, int i)
    {
        if (i + 1 >= s.Length) return false;
        if (s[i + 1] == '*') return false;                       // ** - markdown bold, not an action
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

    /// <summary>
    /// Collapse the runs of spaces removal leaves behind, and tidy punctuation around the gap.
    /// </summary>
    /// <remarks>
    /// ⚠️ Includes punctuation left DANGLING AT THE START, which is not hypothetical: a model writes
    /// "*looks around*, could you clarify?" (observed from qwen2.5-0.5b in the room gate), and lifting the
    /// action out leaves ", could you clarify?" - so the synthesiser opens the line on a comma.
    /// </remarks>
    private static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == ' ' && sb.Length > 0 && sb[^1] == ' ') continue;
            // "He said . " - removing an action before punctuation leaves a space in front of it.
            if ((c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':')
                && sb.Length > 0 && sb[^1] == ' ') sb.Length--;
            sb.Append(c);
        }
        // Drop punctuation the removal orphaned at the very start - it belonged to the action, not to
        // the words that are about to be spoken.
        var text = sb.ToString().Trim();
        int start = 0;
        while (start < text.Length && (text[start] is ',' or ';' or ':' or '.' or '!' or '?' or ' '))
            start++;
        return start > 0 ? text[start..].TrimStart() : text;
    }
}
