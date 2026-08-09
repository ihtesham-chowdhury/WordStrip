using WordStrip.Core.Personal;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Prediction;

/// <summary>
/// Adds contextual probability to the signals <see cref="FrequencyRanker"/> already weighs, by asking the
/// n-gram model how well each candidate follows the words before the caret.
///
/// <para><b>A wrapper, not a replacement.</b> The base score comes from <see cref="FrequencyRanker.Score"/>
/// unchanged, so exact matches still beat prefix completions and prefix completions still beat fuzzy ones no
/// matter what the language model thinks. That ordering is about what the user is demonstrably typing;
/// context is only ever an opinion about what they might mean. This is also why Phase 1's ranker is left
/// alone rather than extended — the project rule is to add a ranker, not edit the engine.</para>
///
/// <para><b>Context reorders within a band; it cannot escape one.</b> The bonus is capped below the
/// 100-point gap Phase 1 put between bands. Without that cap a confidently predicted word could outrank
/// something the user has literally already typed the letters of, which reads as the bar fighting you.</para>
///
/// <para><b>No new candidates.</b> Ranking only orders what it is given. Producing next-word candidates in
/// the first place is <see cref="PredictionEngine"/>'s job.</para>
/// </summary>
public sealed class ContextualRanker : ICandidateRanker
{
    /// <summary>
    /// What a match at each order is worth. Spaced so that any trigram evidence outranks any bigram
    /// evidence: a word the corpus has actually seen in this exact three-word sequence is a better guess
    /// than one merely known to follow the previous word, however common.
    /// </summary>
    private const double TrigramBonus = 40;
    private const double BigramBonus = 20;

    /// <summary>
    /// Weight on the log probability within an order.
    ///
    /// <para>Sized to out-argue the frequency term rather than merely nudge it. The base score already
    /// contains log₁₀ of the word's overall frequency, which across plausible candidates spans about 4.4
    /// points — and a conditional probability has <em>already</em> accounted for how common a word is, so
    /// letting raw frequency speak again double-counts it. At a weight of 2 that is exactly what happened:
    /// after "i am" the model's own ranking put "sure" second, and "the" and "to" — real but unhelpful
    /// continuations that happen to be among the commonest words in English — pushed it off a four-word bar.
    /// At 8 the probability spread within an order is around 16 points, so the conditional estimate decides
    /// and frequency only separates near-ties.</para>
    /// </summary>
    private const double ProbabilityWeight = 8.0;

    /// <summary>
    /// Floor on the log probability used for the refinement, i.e. treat anything below one-in-a-hundred as
    /// equally weak. Also what keeps the orders from overlapping: with this floor a trigram's bonus lands in
    /// [24, 40] and a bigram's in [4, 20], so the hierarchy holds however improbable the trigram.
    /// </summary>
    private const double MinLogScore = -2.0;

    /// <summary>Ceiling on the total bonus, asserted by tests. Bands are 100 apart and the frequency term reaches ~10, so 40 leaves ample headroom.</summary>
    public const double MaxContextBonus = TrigramBonus;

    /// <summary>
    /// What being one of the user's own words is worth before usage is taken into account.
    ///
    /// <para>Sized to make a personal word competitive without making it automatic. A word the general
    /// dictionary has never heard of carries no corpus frequency at all, so without this it would sit at the
    /// bottom of its band beneath every common word sharing the prefix — type "qn" and see nothing useful.
    /// With it, a freshly added word clears the ~10-point spread the frequency term can produce and lands
    /// just above the common words, which is where something the user deliberately taught the app belongs.
    /// It does not lift anything out of its band: an exact match still wins.</para>
    /// </summary>
    private const double PersonalBase = 12;

    /// <summary>Weight on log₁₀ of personal usage, so a word typed hundreds of times outranks one added yesterday.</summary>
    private const double PersonalFrequencyWeight = 6;

    /// <summary>
    /// Ceiling on the personal bonus. Bounded so no amount of repetition can let a personal word escape its
    /// band, and so a word accidentally added once and then hammered cannot permanently own the bar.
    /// </summary>
    public const double MaxPersonalBonus = 30;

    /// <summary>
    /// Ceiling on what learned usage can add, before the model's own confidence scales it down further.
    ///
    /// <para>Smaller than the trigram bonus on purpose. The general model was built from millions of words;
    /// the personal one from however many its owner has typed since switching it on. It should shade the
    /// ordering toward how this person writes, not overrule a well-evidenced general prediction — and a
    /// single word typed by accident must never be able to take over the bar.</para>
    /// </summary>
    public const double MaxLearnedBonus = 15;

    private readonly NGramLanguageModel _model;
    private readonly PersonalVocabularyStore? _personalVocabulary;
    private readonly PersonalLanguageModel? _personalLearning;

    public ContextualRanker(
        NGramLanguageModel model,
        PersonalVocabularyStore? personalVocabulary = null,
        PersonalLanguageModel? personalLearning = null)
    {
        _model = model;
        _personalVocabulary = personalVocabulary;
        _personalLearning = personalLearning;
    }

    public IReadOnlyList<Suggestion> Rank(RankingContext context, IReadOnlyList<Suggestion> candidates, int maxResults)
    {
        if (candidates.Count == 0 || maxResults <= 0) return Array.Empty<Suggestion>();

        var prefixLength = context.Prefix?.Length ?? 0;

        // Resolved once for the whole list. Every candidate is scored against the same preceding words, so
        // asking the model to re-derive them per candidate is pure waste — and on a sixty-candidate list it
        // tripled the cost of a keystroke before this was hoisted out.
        var lookup = _model.Resolve(context.Context);

        var scored = new List<Suggestion>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var score = FrequencyRanker.Score(candidate, prefixLength)
                      + ContextBonus(lookup, candidate.Word)
                      + PersonalBonus(candidate.Word)
                      + LearnedBonus(candidate.Word, context.Context);

            scored.Add(candidate.WithScore(score));
        }

        scored.Sort(Compare);

        return scored.Count <= maxResults ? scored : scored.GetRange(0, maxResults);
    }

    /// <summary>
    /// How much the preceding words argue for this candidate. Zero when the model has nothing to say, which
    /// leaves the candidate scored exactly as Phase 1 would have scored it.
    /// </summary>
    /// <remarks>Public so tests can assert the hierarchy and the cap directly rather than inferring them from output ordering.</remarks>
    public double ContextBonus(string word, PredictionContext context) =>
        ContextBonus(_model.Resolve(context), word);

    private static double ContextBonus(NGramLanguageModel.ContextLookup lookup, string word)
    {
        var order = lookup.Score(word, out var logScore);

        // A unigram "match" means only that the word exists. That is not contextual evidence, and the base
        // score already accounts for how common a word is.
        if (order is NGramOrder.None or NGramOrder.Unigram) return 0;

        var refinement = ProbabilityWeight * Math.Max(logScore, MinLogScore);

        return order switch
        {
            NGramOrder.Trigram => TrigramBonus + refinement,
            NGramOrder.Bigram => BigramBonus + refinement,
            _ => 0,
        };
    }

    /// <summary>
    /// How much this being one of the user's own words is worth. Zero for anything not in the personal
    /// vocabulary, so an engine without one ranks exactly as it did before Phase 3.
    /// </summary>
    /// <remarks>Public so tests can assert the bound directly rather than inferring it from output ordering.</remarks>
    public double PersonalBonus(string word)
    {
        if (_personalVocabulary is null) return 0;

        var frequency = _personalVocabulary.GetFrequency(word);
        if (frequency <= 0) return 0;

        return Math.Min(PersonalBase + (PersonalFrequencyWeight * Math.Log10(frequency + 1)), MaxPersonalBonus);
    }

    /// <summary>
    /// How much the user's own writing argues for this word here. Zero when learning is off, when nothing
    /// has been learned yet, or when this word has never been seen — so the ranking is unchanged until
    /// there is genuine evidence to change it.
    ///
    /// <para>The model's <see cref="PersonalLanguageModel.Confidence"/> is already folded into the score it
    /// returns, which is what makes learning arrive as a gradual drift rather than a sudden change in
    /// behaviour a few sentences after switching it on.</para>
    /// </summary>
    /// <remarks>Public so tests can assert the bound and the cold-start ramp directly.</remarks>
    public double LearnedBonus(string word, PredictionContext context)
    {
        if (_personalLearning is null) return 0;

        var score = _personalLearning.GetPersonalScore(word, context.PrecedingWords);
        return score <= 0 ? 0 : Math.Min(score * MaxLearnedBonus, MaxLearnedBonus);
    }

    /// <summary>Same tiebreak as <see cref="FrequencyRanker"/>: score, then the shorter word, then ordinal. Identical input always produces identical output.</summary>
    private static int Compare(Suggestion a, Suggestion b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0) return byScore;

        var byLength = a.Word.Length.CompareTo(b.Word.Length);
        if (byLength != 0) return byLength;

        return string.CompareOrdinal(a.Word, b.Word);
    }
}
