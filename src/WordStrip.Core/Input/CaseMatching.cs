namespace WordStrip.Core.Input;

/// <summary>
/// Re-applies the capitalisation the user actually typed to a dictionary word, so accepting a suggestion
/// after typing "Hel" yields "Help" rather than silently downcasing to "help".
///
/// <para>Lives outside the injector so the controller can compute the <em>exact</em> text that reaches the
/// screen before it is sent. Replacing a prediction a moment later means deleting precisely what was
/// inserted, and that is only possible if the caller, not the injector, knows what that was.</para>
/// </summary>
public static class CaseMatching
{
    public static string Apply(string typedWord, string replacement)
    {
        if (string.IsNullOrEmpty(typedWord) || string.IsNullOrEmpty(replacement)) return replacement;

        var letters = typedWord.Count(char.IsLetter);
        if (letters > 1 && typedWord.Where(char.IsLetter).All(char.IsUpper))
            return replacement.ToUpperInvariant();

        if (char.IsUpper(typedWord[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    /// <summary>
    /// How many leading characters two strings share. The injectors keep that much in place and rewrite only
    /// what follows — for completing "wor" to "world" that means no deletions at all.
    /// </summary>
    public static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[i] == b[i]) i++;
        return i;
    }
}
