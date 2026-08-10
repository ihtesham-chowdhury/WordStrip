using WordStrip.Core.Personal;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Prediction;

/// <summary>
/// Turns next-word prediction into phrase prediction: "looking" becomes "forward to", "thank you" becomes
/// "for your support".
///
/// <para><b>Bounded beam search.</b> Take the few most likely next words, extend each by asking the model
/// again with that word appended, keep the best handful, repeat up to a strict word limit. Enumerating
/// continuations properly is exponential; a beam keeps it to a fixed number of dictionary lookups per
/// keystroke regardless of how much context there is.</para>
///
/// <para><b>Longer is not better.</b> A phrase must earn each extra word. Every extension multiplies in
/// another conditional probability, and the mean of those probabilities — not their sum — decides the
/// score, so tacking on a plausible-but-vague word actively hurts. Without that, "for the" beats "for" for
/// no reason other than being longer, and the bar fills with padding.</para>
///
/// <para><b>Diversity beats completeness.</b> Offering "forward", "forward to" and "forward to the" as three
/// slots wastes a bar that only has room for a handful. Only the longest form of any given start survives,
/// unless a shorter form is meaningfully more confident.</para>
/// </summary>
public sealed class PhraseGenerator
{
    /// <summary>
    /// Hard ceiling on words in a phrase. Three is where the corpus stops being able to justify a fourth:
    /// beyond it the model is extending on a two-word context it has largely already spent, and the result
    /// reads as invention rather than prediction.
    /// </summary>
    public const int MaxPhraseWords = 3;

    /// <summary>How many partial phrases survive each round. Small on purpose — this runs per keystroke.</summary>
    public const int BeamWidth = 6;

    /// <summary>How many continuations to consider for each surviving phrase.</summary>
    public const int BranchingFactor = 4;

    /// <summary>
    /// A phrase must average at least this much log₁₀ probability per word to be offered at all
    /// (10⁻¹·⁴ ≈ 4%). Below it the model is guessing, and a wrong three-word suggestion costs the user far
    /// more than a missing one — they have to notice it, reject it, and find their place again.
    /// </summary>
    public const double MinMeanLogProbability = -1.4;

    /// <summary>
    /// A longer phrase has to beat the shorter one it extends by this much mean log probability to replace
    /// it. Stops "for" quietly becoming "for the" on a rounding error.
    /// </summary>
    public const double ExtensionMargin = 0.15;

    private readonly NGramLanguageModel _model;
    private readonly PersonalLanguageModel? _personalLearning;

    public PhraseGenerator(NGramLanguageModel model, PersonalLanguageModel? personalLearning = null)
    {
        _model = model;
        _personalLearning = personalLearning;
    }

    /// <summary>One candidate mid-search: the words chosen so far and the log probabilities that produced them.</summary>
    private readonly record struct Beam(List<string> Words, double TotalLogProbability)
    {
        public double MeanLogProbability => Words.Count == 0 ? double.NegativeInfinity : TotalLogProbability / Words.Count;
    }

    /// <summary>
    /// Phrases likely to follow the context, best first. Single words are included — a confident one-word
    /// prediction is often the right answer, and this returns the whole spread rather than only the long ones.
    /// </summary>
    public IReadOnlyList<PhraseCandidate> Generate(PredictionContext context, int maxResults)
    {
        if (maxResults <= 0) return Array.Empty<PhraseCandidate>();

        // At least as many seeds as the bar has slots. The beam width governs how many partial phrases are
        // carried forward for extension, not how many candidates may be returned — conflating the two
        // silently capped a seven-slot bar at six.
        var seeds = _model.GetNextWordCandidates(context, Math.Max(maxResults, BeamWidth));
        if (seeds.Count == 0) return Array.Empty<PhraseCandidate>();

        var completed = new List<Beam>();
        var frontier = new List<Beam>();

        foreach (var seed in seeds)
        {
            var beam = new Beam(new List<string> { seed.Word }, ScoreOf(context, Array.Empty<string>(), seed.Word, seed.LogScore));
            completed.Add(beam);

            // Only a word the model actually predicted from context is worth extending. A word that arrived
            // from the unigram fallback means the model has never seen this context and is offering common
            // English; building a phrase on top of that produces a fluent sentence fragment with no
            // connection to what the user is writing, which is exactly the confident-and-wrong failure the
            // brief asks to avoid. Uncertainty should shorten suggestions, not lengthen them.
            if (seed.Order is NGramOrder.Bigram or NGramOrder.Trigram && frontier.Count < BeamWidth)
                frontier.Add(beam);
        }

        for (var length = 1; length < MaxPhraseWords && frontier.Count > 0; length++)
        {
            var next = new List<Beam>();

            foreach (var beam in frontier)
            {
                // The context for the extension is the original context plus what this phrase has committed
                // to so far — which is what makes "looking forward" able to propose "to".
                var extended = Extend(context, beam.Words);

                foreach (var continuation in _model.GetNextWordCandidates(extended, BranchingFactor))
                {
                    // An extension has to be evidenced by the full three-word context, not by a backoff.
                    // A bigram answer here means only "this word often follows that one", which is how a
                    // phrase generator produces fluent nonsense: after "how are we", "to" is a common
                    // successor of "we" and "do" of "to", and the bar ends up offering "we to do". Requiring
                    // the corpus to have actually seen the sequence is what keeps phrases grammatical.
                    if (continuation.Order != NGramOrder.Trigram) continue;

                    // A phrase that repeats itself is a sign the model has run out of things to say.
                    if (beam.Words.Contains(continuation.Word, StringComparer.Ordinal)) continue;

                    var words = new List<string>(beam.Words) { continuation.Word };
                    var score = beam.TotalLogProbability
                              + ScoreOf(extended, beam.Words, continuation.Word, continuation.LogScore);

                    var candidate = new Beam(words, score);
                    if (candidate.MeanLogProbability < MinMeanLogProbability) continue;

                    next.Add(candidate);
                    completed.Add(candidate);
                }
            }

            next.Sort(static (a, b) => b.MeanLogProbability.CompareTo(a.MeanLogProbability));
            frontier = next.Count <= BeamWidth ? next : next.GetRange(0, BeamWidth);
        }

        return Select(completed, maxResults);
    }

    /// <summary>
    /// Score for one word in one position: the general model's backoff score, nudged by how much the user's
    /// own writing agrees. Personal evidence shapes phrases rather than inventing them — it can promote
    /// "Northfield Data Systems" above the corpus ordering, but only among sequences the corpus recognises.
    /// </summary>
    private double ScoreOf(PredictionContext context, IReadOnlyList<string> committed, string word, double modelScore)
    {
        if (_personalLearning is null) return modelScore;

        var personalContext = new List<string>(context.PrecedingWords);
        personalContext.AddRange(committed);

        var personal = _personalLearning.GetPersonalScore(word, personalContext);
        if (personal <= 0) return modelScore;

        // Added in log space as a bounded bonus: at most half an order of magnitude, so it reorders phrases
        // of similar strength without letting a personal habit manufacture a phrase from nothing.
        return modelScore + Math.Min(personal, 1.0) * 0.5;
    }

    private static PredictionContext Extend(PredictionContext context, IReadOnlyList<string> committed)
    {
        var words = new List<string>(context.PrecedingWords);
        words.AddRange(committed);

        return context with { PrecedingWords = words, IsSentenceStart = false };
    }

    /// <summary>
    /// Picks the final spread: best first, and never two phrases where one is merely a longer version of the
    /// other unless the longer one is clearly better.
    /// </summary>
    private static List<PhraseCandidate> Select(List<Beam> completed, int maxResults)
    {
        completed.Sort(static (a, b) =>
        {
            var byScore = b.MeanLogProbability.CompareTo(a.MeanLogProbability);
            if (byScore != 0) return byScore;

            // Deterministic: shorter first, then ordinally on the text.
            var byLength = a.Words.Count.CompareTo(b.Words.Count);
            return byLength != 0 ? byLength : string.CompareOrdinal(string.Join(' ', a.Words), string.Join(' ', b.Words));
        });

        var chosen = new List<Beam>();

        foreach (var beam in completed)
        {
            var supersedes = -1;
            var redundant = false;

            for (var i = 0; i < chosen.Count; i++)
            {
                if (!SharesStart(chosen[i].Words, beam.Words)) continue;

                if (beam.Words.Count > chosen[i].Words.Count &&
                    beam.MeanLogProbability >= chosen[i].MeanLogProbability - ExtensionMargin)
                {
                    supersedes = i;   // the longer form is worth having instead
                }
                else
                {
                    redundant = true; // same opening, and not a good enough reason to show both
                }

                break;
            }

            if (redundant) continue;

            if (supersedes >= 0) chosen[supersedes] = beam;
            else if (chosen.Count < maxResults) chosen.Add(beam);
        }

        return chosen
            .Select(b => new PhraseCandidate(
                string.Join(' ', b.Words),
                b.Words.Count,
                b.MeanLogProbability,
                Confidence: ConfidenceFor(b.MeanLogProbability)))
            .ToList();
    }

    private static bool SharesStart(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count > 0 && b.Count > 0 && string.Equals(a[0], b[0], StringComparison.Ordinal);

    /// <summary>
    /// Maps mean log probability onto 0–1. Certainty (log 0) is 1; the threshold at which a phrase stops
    /// being offered at all is 0. Callers use it to decide how much to commit to a prediction.
    /// </summary>
    private static double ConfidenceFor(double meanLogProbability) =>
        Math.Clamp(1 - (meanLogProbability / MinMeanLogProbability), 0, 1);
}

/// <summary>A generated phrase, before it becomes a <see cref="Suggestion"/>.</summary>
public readonly record struct PhraseCandidate(string Text, int WordCount, double MeanLogProbability, double Confidence);
