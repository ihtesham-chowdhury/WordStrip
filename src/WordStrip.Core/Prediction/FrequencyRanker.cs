namespace WordStrip.Core.Prediction;

/// <summary>
/// Ranks candidates on the signals available today: whether the typed text matches exactly, whether it is a
/// prefix, how far the edit distance is, and how common the word is.
///
/// <para>Scoring is banded rather than blended. Corpus frequencies span nine orders of magnitude, so a raw
/// frequency term would swamp everything else — a distant fuzzy match for a very common word would outrank
/// an exact prefix completion. Each candidate class therefore gets a base band it cannot escape, and
/// frequency only orders candidates <em>within</em> a band, via log₁₀ so that "ten times more common" is a
/// fixed step rather than a landslide.</para>
///
/// <para>Ordering is fully deterministic: ties break on the shorter word, then ordinally on the word itself,
/// so identical input always produces identical output.</para>
/// </summary>
public sealed class FrequencyRanker : ICandidateRanker
{
    // Bands are spaced far enough apart that the frequency term (at most ~10) can never cross between them.
    private const double ExactWordBand = 300;
    private const double PrefixBand = 200;
    private const double FrequentWordBand = 100;
    private const double FuzzyBand = 0;

    /// <summary>Cost per edit. Large enough that a closer match always beats a more distant one in the same band.</summary>
    private const double EditDistancePenalty = 25;

    public IReadOnlyList<Suggestion> Rank(RankingContext context, IReadOnlyList<Suggestion> candidates, int maxResults)
    {
        if (candidates.Count == 0 || maxResults <= 0) return Array.Empty<Suggestion>();

        var prefixLength = context.Prefix?.Length ?? 0;

        var scored = new List<Suggestion>(candidates.Count);
        foreach (var candidate in candidates)
            scored.Add(candidate.WithScore(Score(candidate, prefixLength)));

        scored.Sort(Compare);

        return scored.Count <= maxResults ? scored : scored.GetRange(0, maxResults);
    }

    /// <summary>Exposed so tests can assert the scoring rules directly rather than inferring them from ordering.</summary>
    public static double Score(Suggestion candidate, int prefixLength)
    {
        var band = candidate.Source switch
        {
            SuggestionSource.ExactWord => ExactWordBand,
            SuggestionSource.PrefixCompletion => PrefixBand,
            SuggestionSource.FrequentWord => FrequentWordBand,
            _ => FuzzyBand,
        };

        // log10 keeps the frequency term inside its band: even the commonest word in the corpus contributes
        // about 10, well under the 100-point gap between bands.
        var frequencyTerm = Math.Log10(Math.Max(candidate.Frequency, 0) + 1);

        // A completion that adds fewer letters is the more likely intent, but this must stay a tiebreak.
        // Frequencies of similarly common words differ by only hundredths after log₁₀, so a coefficient of
        // 0.05 was enough to make length beat frequency outright and put "work" above "world"; 0.01 keeps
        // length deciding only when frequency effectively ties.
        var lengthPenalty = candidate.Source == SuggestionSource.PrefixCompletion
            ? Math.Max(0, candidate.Word.Length - prefixLength) * 0.01
            : 0;

        return band + frequencyTerm - (candidate.EditDistance * EditDistancePenalty) - lengthPenalty;
    }

    private static int Compare(Suggestion a, Suggestion b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0) return byScore;

        var byLength = a.Word.Length.CompareTo(b.Word.Length);
        if (byLength != 0) return byLength;

        return string.CompareOrdinal(a.Word, b.Word);
    }
}
