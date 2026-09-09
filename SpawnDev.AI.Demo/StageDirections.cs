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
