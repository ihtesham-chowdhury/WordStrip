namespace WordStrip.Core.Prediction;

/// <summary>
/// What the ranker knows about the current typing situation: the in-progress word, and since Phase 2 what
/// came before it.
///
/// <para>Existing as a type rather than a bare string is what let the contextual signal arrive without
/// touching the ranker interface or any call site — <see cref="Context"/> was added with a default, so
/// <c>new RankingContext(prefix)</c> still compiles and still means exactly what it did. Phase 3's personal
/// history should arrive the same way.</para>
/// </summary>
public readonly record struct RankingContext(string Prefix, PredictionContext Context = default);

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
