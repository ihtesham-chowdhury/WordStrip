namespace WordStrip.Core.Prediction.Neural;

/// <summary>How much a neural score is allowed to move a candidate, and when the model is consulted at all.</summary>
public sealed record NeuralRerankOptions
{
    /// <summary>
    /// Skip the model when the statistical stack is already this confident. A trigram hit on a context the
    /// corpus knows well needs no second opinion, and asking for one costs a thousand times more than the
    /// answer it would confirm.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.62;

    /// <summary>
    /// Ceiling on what reranking can add to a candidate's score. Bounded on exactly the same reasoning as
    /// every other signal here: it must be able to reorder candidates within a band and never lift one out
    /// of it, so a confident model still cannot outrank a word the user has finished typing.
    /// </summary>
    public double MaxNeuralBonus { get; init; } = 25;

    /// <summary>
    /// How long a single inference may take before it is abandoned. A late answer is worthless — by then the
    /// user has typed on and the bar has moved — so waiting longer buys nothing but a stale result to throw
    /// away.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Largest candidate list worth scoring. The bar shows at most seven.</summary>
    public int MaxCandidates { get; init; } = 8;
}

/// <summary>
/// Runs the neural reranker off the typing path and decides whether its answer is still wanted by the time
/// it arrives.
///
/// <para><b>The concurrency is the hard part, not the model.</b> Inference takes tens of milliseconds while
/// someone types every hundred or so, which guarantees that results arrive after the state they were
/// computed for has gone. A result applied to the wrong state is worse than no result at all: the bar would
/// visibly rearrange itself a beat after the user moved on, which reads as the app fighting them.</para>
///
/// <para>Two mechanisms, both necessary. Every request carries a sequence number and only the newest may
/// deliver, so an in-flight answer for an older keystroke is discarded even if it completes. And starting a
/// request cancels the previous one, so abandoned work stops rather than competing for the CPU with the
/// request that replaced it.</para>
///
/// <para>Everything here degrades to doing nothing: no model, a failed load, a timeout, a cancellation and
/// an exception all end in the statistical ordering being kept.</para>
/// </summary>
public sealed class NeuralRerankCoordinator : IDisposable
{
    private readonly INeuralReranker _reranker;
    private readonly NeuralRerankOptions _options;
    private readonly object _gate = new();

    private CancellationTokenSource? _inFlight;
    private long _issuedSequence;
    private long _deliveredSequence;

    public NeuralRerankCoordinator(INeuralReranker reranker, NeuralRerankOptions? options = null)
    {
        _reranker = reranker;
        _options = options ?? new NeuralRerankOptions();
    }

    /// <summary>Counts requests that finished but were already superseded. Surfaced for the tests that prove staleness is actually caught.</summary>
    public int StaleResultsDiscarded { get; private set; }

    /// <summary>Counts requests never made because the statistical answer was already good enough.</summary>
    public int SkippedAsConfident { get; private set; }

    /// <summary>
    /// Whether the model is worth consulting for this result.
    ///
    /// <para>The cascade: if the best candidate already carries high confidence the statistical stack has
    /// answered, and the model is not asked. This is what keeps ordinary typing on the microsecond path —
    /// most keystrokes in familiar text never reach the model at all.</para>
    /// </summary>
    public bool ShouldRerank(IReadOnlyList<Suggestion> ranked)
    {
        if (!_reranker.IsReady || ranked.Count < 2) return false;

        if (ranked[0].Confidence >= _options.ConfidenceThreshold)
        {
            SkippedAsConfident++;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reranks if it is worth doing and the answer is still current, otherwise returns the input untouched.
    ///
    /// <para>The returned list is safe to display: either it is the original, or it is the original reordered
    /// by a result that was still current when it arrived.</para>
    /// </summary>
    public async Task<IReadOnlyList<Suggestion>> RerankAsync(
        PredictionContext context,
        IReadOnlyList<Suggestion> ranked,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldRerank(ranked)) return ranked;

        long sequence;
        CancellationTokenSource cts;

        lock (_gate)
        {
            sequence = ++_issuedSequence;

            // Whatever was running is for a keystroke that no longer matters.
            _inFlight?.Cancel();
            _inFlight?.Dispose();

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.Timeout);
            _inFlight = cts;
        }

        IReadOnlyDictionary<string, double>? scores;

        try
        {
            var candidates = ranked.Count <= _options.MaxCandidates
                ? ranked
                : ranked.Take(_options.MaxCandidates).ToList();

            scores = await _reranker
                .ScoreAsync(context, candidates.Select(s => s.Word).ToList(), cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ranked;
        }
        catch (Exception)
        {
            // A model that misbehaves must not take suggestions down with it. The statistical ordering is
            // already a complete answer; this was only ever an improvement on top of it.
            return ranked;
        }

        if (scores is null || scores.Count == 0) return ranked;

        lock (_gate)
        {
            // Someone has typed since this was asked for. Its answer describes text that is no longer on
            // screen, and applying it would rearrange the bar under the user.
            if (sequence <= _deliveredSequence)
            {
                StaleResultsDiscarded++;
                return ranked;
            }

            _deliveredSequence = sequence;
        }

        return Apply(ranked, scores);
    }

    /// <summary>
    /// Folds the neural scores into the existing ones.
    ///
    /// <para>Scores are normalised against the best candidate before being weighted, so what matters is the
    /// model's <em>relative</em> preference rather than its absolute log probabilities — those vary with
    /// context length and vocabulary and are not comparable to the ranker's scale at all.</para>
    /// </summary>
    private IReadOnlyList<Suggestion> Apply(IReadOnlyList<Suggestion> ranked, IReadOnlyDictionary<string, double> scores)
    {
        var best = double.NegativeInfinity;
        foreach (var candidate in ranked)
        {
            if (scores.TryGetValue(candidate.Word, out var score) && score > best) best = score;
        }

        if (double.IsNegativeInfinity(best)) return ranked;

        var rescored = new List<Suggestion>(ranked.Count);
        foreach (var candidate in ranked)
        {
            if (!scores.TryGetValue(candidate.Word, out var score))
            {
                rescored.Add(candidate);
                continue;
            }

            // 0 for the model's favourite, falling away for the rest. One order of magnitude less likely
            // costs the whole bonus, which keeps a strong opinion meaningful and a weak one negligible.
            var relative = Math.Clamp(1 + (score - best), 0, 1);
            rescored.Add(candidate.WithScore(candidate.Score + (relative * _options.MaxNeuralBonus)));
        }

        rescored.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;

            var byLength = a.Word.Length.CompareTo(b.Word.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(a.Word, b.Word);
        });

        return rescored;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _inFlight?.Cancel();
            _inFlight?.Dispose();
            _inFlight = null;
        }
    }
}
