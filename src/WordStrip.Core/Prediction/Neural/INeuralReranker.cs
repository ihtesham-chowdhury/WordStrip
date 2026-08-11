namespace WordStrip.Core.Prediction.Neural;

/// <summary>
/// Scores an existing candidate list by how well each word fits the context, using a local neural model.
///
/// <para><b>A reranker, not a generator.</b> It never proposes words — the statistical stack decides what
/// may be offered, and this only reorders. That keeps every existing guarantee intact: no word can appear
/// that the dictionary, the personal vocabulary or the n-gram model did not already vouch for, and a model
/// that is unavailable, slow or wrong can never do worse than leave the order alone.</para>
///
/// <para><b>Scoring a list costs one inference, not one per candidate.</b> A causal language model produces
/// a distribution over the whole vocabulary from a single forward pass over the context, so every candidate
/// is scored by reading its own entry out of that one result. Anything per-candidate would be hopeless here
/// — the statistical path answers in tens of microseconds and a transformer takes tens of milliseconds.</para>
/// </summary>
public interface INeuralReranker
{
    /// <summary>Whether a model is loaded and inference can be attempted. False means every call returns null.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Log probabilities for the candidates, keyed by candidate, or null when there is nothing to say —
    /// no model, a failed load, or a cancelled request.
    ///
    /// <para>Returning null rather than throwing is the contract: the caller's correct response to "the
    /// neural model had no opinion" is to keep the statistical ordering, which is not an error case and
    /// should not be written as one.</para>
    /// </summary>
    Task<IReadOnlyDictionary<string, double>?> ScoreAsync(
        PredictionContext context,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken);
}
