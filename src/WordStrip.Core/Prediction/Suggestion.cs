namespace WordStrip.Core.Prediction;

/// <summary>Where a candidate came from. Lets the ranker weigh candidates without re-deriving how they were found.</summary>
public enum SuggestionSource
{
    /// <summary>The typed text is a prefix of this word.</summary>
    PrefixCompletion = 0,

    /// <summary>The typed text is exactly this dictionary word.</summary>
    ExactWord = 1,

    /// <summary>Reached by edit-distance from the typed text; the typed text is not a prefix of it.</summary>
    FuzzyMatch = 2,

    /// <summary>A common word offered when there is no prefix to work from yet.</summary>
    FrequentWord = 3,

    /// <summary>Several words offered as one candidate, e.g. "forward to". Phase 5.</summary>
    Phrase = 4,

    /// <summary>An emoji matching the word being typed or just finished. Placed deliberately by the engine rather than ranked against words.</summary>
    Emoji = 5,
}

/// <summary>
/// One thing the bar can offer, and the signals the ranker scores it on.
///
/// <para><see cref="Word"/> is the text to insert, whether that is one word, several, or an emoji. Callers
/// deliberately cannot tell how a candidate was produced from how they insert it — that separation is what
/// let phrases and emoji arrive without the UI or the injector learning anything new.</para>
///
/// <para>Every field beyond the first three was added with a default, so the three-argument construction
/// used throughout the app keeps compiling and behaving exactly as it did.</para>
/// </summary>
public readonly record struct Suggestion(
    string Word,
    long Frequency,
    int EditDistance,
    SuggestionSource Source = SuggestionSource.PrefixCompletion,
    double Score = 0,
    double Confidence = 1.0)
{
    /// <summary>Returns a copy carrying a ranking score. Kept separate so ranking never mutates candidate generation.</summary>
    public Suggestion WithScore(double score) => this with { Score = score };

    /// <summary>How many words this candidate inserts. One for an ordinary completion, more for a phrase.</summary>
    public int WordCount
    {
        get
        {
            if (string.IsNullOrEmpty(Word)) return 0;

            var words = 1;
            foreach (var c in Word)
            {
                if (c == ' ') words++;
            }

            return words;
        }
    }

    public bool IsPhrase => Source == SuggestionSource.Phrase;

    public bool IsEmoji => Source == SuggestionSource.Emoji;
}
