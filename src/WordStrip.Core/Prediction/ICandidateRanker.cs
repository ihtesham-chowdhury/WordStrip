namespace WordStrip.Core.Prediction;

/// <summary>
/// What the ranker knows about the current typing situation.
///
/// <para>Only the in-progress word today. This exists as a type rather than a bare string precisely so later
/// phases can add preceding words, caret context or personal history without changing the ranker interface
/// or every call site.</para>
/// </summary>
public readonly record struct RankingContext(string Prefix);

/// <summary>
/// Orders candidates for display. Kept separate from candidate <em>generation</em> so that adding a new
/// signal — contextual probability, a personal vocabulary — means writing another ranker rather than
/// reworking <see cref="PredictionEngine"/>.
/// </summary>
public interface ICandidateRanker
{
    /// <summary>Scores and orders candidates, returning at most <paramref name="maxResults"/>. Must be deterministic.</summary>
    IReadOnlyList<Suggestion> Rank(RankingContext context, IReadOnlyList<Suggestion> candidates, int maxResults);
}
