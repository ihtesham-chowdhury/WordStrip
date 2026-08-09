using System.Text;

namespace WordStrip.Core.Prediction.NGram;

/// <summary>
/// Turns text into the tokens the n-gram model is keyed on.
///
/// <para>Shared between the offline builder and the running app on purpose. The corpus is tokenised once at
/// build time and the typed context is tokenised on every keystroke, and if the two ever disagree about
/// what a token is — a curly apostrophe, a trailing comma, a capital letter — every lookup misses and the
/// model silently predicts nothing. That failure produces no error and no crash, just a permanently
/// unhelpful bar, which is exactly the kind of bug worth designing out rather than hunting later.</para>
/// </summary>
public static class NGramTokenizer
{
    /// <summary>Characters that end a sentence, after which the next word is a sentence opener.</summary>
    public static bool IsSentenceTerminator(char c) => c is '.' or '!' or '?';

    /// <summary>
    /// Reduces a word to its model form: lower-cased, letters and inner apostrophes only.
    /// Returns an empty string for anything with no letters in it, which the caller should skip.
    /// </summary>
    public static string Normalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;

        // Overwhelmingly the common case at runtime: candidates come from the dictionary and are already
        // lower-case letters. Returning them untouched avoids a StringBuilder and a string allocation for
        // every one of the sixty-odd candidates scored on each keystroke.
        if (IsAlreadyNormalized(word)) return word;

        var builder = new StringBuilder(word.Length);

        foreach (var raw in word)
        {
            // Gutenberg texts are full of typographic apostrophes; folding them to ASCII here is what keeps
            // "don’t" and "don't" from becoming two unrelated tokens.
            var c = raw is '’' or 'ʼ' ? '\'' : raw;

            if (char.IsLetter(c)) builder.Append(char.ToLowerInvariant(c));
            else if (c == '\'' && builder.Length > 0) builder.Append(c);
        }

        // Trailing apostrophes are possessives with the s dropped ("the boys' room") or an unbalanced quote;
        // either way the bare word is the token we want.
        while (builder.Length > 0 && builder[^1] == '\'')
            builder.Length -= 1;

        return builder.Length == 0 ? string.Empty : builder.ToString();
    }

    /// <summary>
    /// Streams a document as tokens, inserting <see cref="NGramFormat.SentenceStart"/> at the beginning and
    /// after every sentence terminator. Used by the offline builder; the app tokenises single words instead.
    ///
    /// <para>The sentence marker is what stops n-grams spanning a full stop. Without it the corpus would
    /// teach the model that the last word of one sentence predicts the first word of the next, which is
    /// noise, and there would be no way to answer "what word tends to start a sentence?".</para>
    /// </summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        yield return NGramFormat.SentenceStart;

        var word = new StringBuilder(32);
        var emittedSinceBoundary = false;

        foreach (var raw in text)
        {
            var c = raw is '’' or 'ʼ' ? '\'' : raw;

            if (char.IsLetter(c) || (c == '\'' && word.Length > 0))
            {
                word.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : c);
                continue;
            }

            if (word.Length > 0)
            {
                var token = Trim(word);
                if (token.Length > 0) { yield return token; emittedSinceBoundary = true; }
                word.Clear();
            }

            // Only open a new sentence if the last one had something in it. Runs like "..." or ".  --  ."
            // would otherwise emit a string of empty sentences and inflate the sentence-start counts.
            if (IsSentenceTerminator(c) && emittedSinceBoundary)
            {
                yield return NGramFormat.SentenceStart;
                emittedSinceBoundary = false;
            }
        }

        if (word.Length > 0)
        {
            var token = Trim(word);
            if (token.Length > 0) yield return token;
        }
    }

    /// <summary>True when <see cref="Normalize"/> would return the input unchanged: lower-case letters, with apostrophes only on the inside.</summary>
    private static bool IsAlreadyNormalized(string word)
    {
        if (word[^1] == '\'' || word[0] == '\'') return false;

        foreach (var c in word)
        {
            if (c == '\'') continue;
            if (!char.IsLetter(c) || char.IsUpper(c)) return false;
        }

        return true;
    }

    private static string Trim(StringBuilder word)
    {
        while (word.Length > 0 && word[^1] == '\'') word.Length -= 1;
        return word.Length == 0 ? string.Empty : word.ToString();
    }
}
