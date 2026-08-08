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
}

/// <summary>
/// A candidate word with the signals the ranker scores it on.
///
/// <para><see cref="Source"/> and <see cref="Score"/> were added additively with defaults, so the existing
/// three-argument construction used across the app keeps compiling and behaving identically.</para>
/// </summary>
public readonly record struct Suggestion(
    string Word,
    long Frequency,
    int EditDistance,
    SuggestionSource Source = SuggestionSource.PrefixCompletion,
    double Score = 0)
{
    /// <summary>Returns a copy carrying a ranking score. Kept separate so ranking never mutates candidate generation.</summary>
    public Suggestion WithScore(double score) => this with { Score = score };
}
