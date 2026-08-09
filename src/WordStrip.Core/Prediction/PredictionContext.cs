using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Prediction;

/// <summary>
/// Everything the prediction layer knows about where the caret is and what led up to it.
///
/// <para>Deliberately a plain value passed in from above rather than something the prediction layer reads
/// out of Windows itself. The app already reconstructs the typed word from the keyboard hook, and that
/// same layer is the only one that can say honestly whether its picture of the text is still trustworthy —
/// a click or an arrow key invalidates it. Phase 7's text-services work will replace how this is
/// populated, and nothing below this type should have to change when it does.</para>
/// </summary>
public readonly record struct PredictionContext(
    string PartialWord,
    IReadOnlyList<string> PrecedingWords,
    bool IsSentenceStart = false,
    char? PrecedingPunctuation = null,
    bool ShouldCapitalize = false)
{
    private static readonly string[] NoWords = Array.Empty<string>();

    /// <summary>No context at all: nothing typed, nothing known before the caret.</summary>
    public static PredictionContext Empty { get; } = new(string.Empty, NoWords);

    /// <summary>Context for someone who has typed nothing yet but is known to be starting a sentence.</summary>
    public static PredictionContext AtSentenceStart() =>
        new(string.Empty, NoWords, IsSentenceStart: true, ShouldCapitalize: true);

    /// <summary>Convenience for the common "just these words came before" case, used heavily in tests.</summary>
    public static PredictionContext After(params string[] precedingWords) =>
        new(string.Empty, precedingWords ?? NoWords);

    /// <summary>True while the user is mid-word, which is what separates completion mode from next-word mode.</summary>
    public bool HasPartialWord => !string.IsNullOrEmpty(PartialWord);

    /// <summary>
    /// The last one or two preceding words, lower-cased, most recent last, with
    /// <see cref="NGramFormat.SentenceStart"/> standing in at the beginning of a sentence.
    ///
    /// <para>The marker matters: after a full stop the previous sentence's last word says nothing useful
    /// about the next word, but "a sentence is starting" says a great deal. Substituting it here is what
    /// lets the model answer with sentence openers instead of falling straight through to raw word
    /// frequency.</para>
    /// </summary>
    public IReadOnlyList<string> ModelContext()
    {
        if (IsSentenceStart || PrecedingWords.Count == 0)
            return new[] { NGramFormat.SentenceStart };

        var last = NGramTokenizer.Normalize(PrecedingWords[^1]);
        if (last.Length == 0)
            return new[] { NGramFormat.SentenceStart };

        if (PrecedingWords.Count == 1)
            return new[] { NGramFormat.SentenceStart, last };

        var secondLast = NGramTokenizer.Normalize(PrecedingWords[^2]);
        return secondLast.Length == 0
            ? new[] { NGramFormat.SentenceStart, last }
            : new[] { secondLast, last };
    }
}
