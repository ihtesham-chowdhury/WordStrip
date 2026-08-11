using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.Neural;

namespace WordStrip.Core.Tests;

/// <summary>
/// The neural reranking pipeline, tested without a neural model.
///
/// <para>Everything difficult about this phase is concurrency, not machine learning. Inference takes tens of
/// milliseconds while someone types every hundred or so, so results routinely arrive describing text that
/// has already changed — and applying one of those is worse than having no model at all, because the bar
/// visibly rearranges itself a beat after the user has moved on.</para>
///
/// <para>A fake reranker makes that testable and deterministic. Driving these cases through a real model
/// would mean a 90 MB download, tens of milliseconds per assertion, and no way to command the exact timing
/// the interesting failures need.</para>
/// </summary>
public class NeuralRerankTests
{
    /// <summary>A reranker whose answers, delay and failures are all dictated by the test.</summary>
    private sealed class FakeReranker : INeuralReranker
    {
        private readonly Dictionary<string, double> _scores;

        public FakeReranker(params (string Word, double Score)[] scores) =>
            _scores = scores.ToDictionary(s => s.Word, s => s.Score, StringComparer.Ordinal);

        public bool IsReady { get; set; } = true;
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public Exception? ThrowOnScore { get; set; }
        public int Calls { get; private set; }

        /// <summary>Released by the test to control exactly when an in-flight request completes.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>
        /// Makes the fake finish even after it has been cancelled, which is how the sequence-number check
        /// gets exercised at all. Cancellation is the first line of defence and normally catches a
        /// superseded request before it can deliver — but an implementation that checks its token late, or
        /// a native inference call already past the point of interruption, will still return an answer.
        /// That is exactly the case the sequence number exists for.
        /// </summary>
        public bool IgnoreCancellation { get; set; }

        public async Task<IReadOnlyDictionary<string, double>?> ScoreAsync(
            PredictionContext context, IReadOnlyList<string> candidates, CancellationToken cancellationToken)
        {
            Calls++;

            if (Gate is not null)
            {
                if (IgnoreCancellation) await Gate.Task.ConfigureAwait(false);
                else await Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);

            if (!IgnoreCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnScore is not null) throw ThrowOnScore;

            return _scores;
        }
    }

    /// <summary>Candidates in a deliberate order, with low confidence so the cascade lets them through.</summary>
    private static List<Suggestion> Candidates(params string[] words) =>
        words.Select((w, i) => new Suggestion(w, 1000 - i, 0, SuggestionSource.FrequentWord, Score: 100 - i, Confidence: 0.2))
             .ToList();

    private static IReadOnlyList<string> WordsOf(IReadOnlyList<Suggestion> suggestions) =>
        suggestions.Select(s => s.Word).ToList();

    // --- No model ---------------------------------------------------------------------------------------

    [Fact]
    public async Task With_no_model_the_order_is_untouched()
    {
        using var coordinator = new NeuralRerankCoordinator(UnavailableNeuralReranker.Instance);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates);

        Assert.Equal(WordsOf(candidates), WordsOf(result));
    }

    [Fact]
    public async Task A_model_that_is_not_ready_is_never_called()
    {
        var fake = new FakeReranker(("at", 0.0)) { IsReady = false };
        using var coordinator = new NeuralRerankCoordinator(fake);

        await coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward", "for", "at"));

        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task A_model_that_fails_leaves_the_order_alone()
    {
        var fake = new FakeReranker(("at", 0.0)) { ThrowOnScore = new InvalidOperationException("model exploded") };
        using var coordinator = new NeuralRerankCoordinator(fake);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates);

        // A broken model must not be able to take suggestions down with it.
        Assert.Equal(WordsOf(candidates), WordsOf(result));
    }

    [Fact]
    public async Task An_empty_answer_leaves_the_order_alone()
    {
        var fake = new FakeReranker();
        using var coordinator = new NeuralRerankCoordinator(fake);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates);

        Assert.Equal(WordsOf(candidates), WordsOf(result));
    }

    // --- The cascade ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_confident_statistical_answer_never_reaches_the_model()
    {
        var fake = new FakeReranker(("at", 0.0));
        using var coordinator = new NeuralRerankCoordinator(fake);

        var confident = new List<Suggestion>
        {
            new("forward", 1000, 0, SuggestionSource.FrequentWord, Score: 100, Confidence: 0.95),
            new("for", 900, 0, SuggestionSource.FrequentWord, Score: 99, Confidence: 0.95),
        };

        await coordinator.RerankAsync(PredictionContext.After("looking"), confident);

        // This is what keeps ordinary typing on the microsecond path.
        Assert.Equal(0, fake.Calls);
        Assert.Equal(1, coordinator.SkippedAsConfident);
    }

    [Fact]
    public async Task A_single_candidate_is_not_worth_reranking()
    {
        var fake = new FakeReranker(("forward", 0.0));
        using var coordinator = new NeuralRerankCoordinator(fake);

        await coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward"));

        Assert.Equal(0, fake.Calls);
    }

    // --- Reranking --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_model_can_promote_a_candidate()
    {
        // Statistically "forward" leads; the model strongly prefers "at".
        var fake = new FakeReranker(("forward", -4.0), ("for", -3.0), ("at", -0.1));
        using var coordinator = new NeuralRerankCoordinator(fake);

        var result = await coordinator.RerankAsync(
            PredictionContext.After("looking"), Candidates("forward", "for", "at"));

        Assert.Equal("at", result[0].Word);
    }

    [Fact]
    public async Task A_candidate_the_model_says_nothing_about_keeps_its_score()
    {
        var fake = new FakeReranker(("forward", -0.1));
        using var coordinator = new NeuralRerankCoordinator(fake);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates);

        var untouched = result.Single(s => s.Word == "at");
        Assert.Equal(candidates.Single(s => s.Word == "at").Score, untouched.Score);
    }

    [Fact]
    public async Task Reranking_cannot_move_a_candidate_further_than_its_bound()
    {
        var options = new NeuralRerankOptions { MaxNeuralBonus = 25 };
        var fake = new FakeReranker(("forward", -50.0), ("for", 0.0), ("at", -50.0));
        using var coordinator = new NeuralRerankCoordinator(fake, options);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates);

        foreach (var candidate in candidates)
        {
            var after = result.Single(s => s.Word == candidate.Word);
            Assert.InRange(after.Score - candidate.Score, 0, options.MaxNeuralBonus);
        }
    }

    [Fact]
    public async Task Reranking_is_deterministic()
    {
        var fake = new FakeReranker(("forward", -1.0), ("for", -1.0), ("at", -1.0));
        using var coordinator = new NeuralRerankCoordinator(fake);

        var first = WordsOf(await coordinator.RerankAsync(
            PredictionContext.After("looking"), Candidates("forward", "for", "at")));

        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.Equal(first, WordsOf(await coordinator.RerankAsync(
                PredictionContext.After("looking"), Candidates("forward", "for", "at"))));
        }
    }

    // --- Staleness and cancellation ---------------------------------------------------------------------

    [Fact]
    public async Task A_result_that_arrives_after_a_newer_request_is_discarded()
    {
        // The failure this guards: the user types on, a slower earlier inference finishes, and the bar
        // rearranges itself to describe text that is no longer on screen.
        var fake = new FakeReranker(("at", -0.1), ("forward", -4.0), ("for", -4.0)) { IgnoreCancellation = true };
        using var coordinator = new NeuralRerankCoordinator(fake);

        var firstGate = new TaskCompletionSource();
        fake.Gate = firstGate;

        var older = coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward", "for", "at"));

        // A newer keystroke, allowed to complete immediately.
        fake.Gate = null;
        var newer = await coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward", "for", "at"));
        Assert.Equal("at", newer[0].Word);

        // Now let the older one finish. It must not deliver.
        firstGate.SetResult();
        var olderResult = await older;

        Assert.Equal(new[] { "forward", "for", "at" }, WordsOf(olderResult));
        Assert.Equal(1, coordinator.StaleResultsDiscarded);
    }

    [Fact]
    public async Task Starting_a_request_cancels_the_one_it_replaces()
    {
        var fake = new FakeReranker(("at", -0.1)) { Delay = TimeSpan.FromSeconds(5) };
        using var coordinator = new NeuralRerankCoordinator(fake);

        var older = coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward", "for", "at"));
        await Task.Delay(30);

        fake.Delay = TimeSpan.Zero;
        await coordinator.RerankAsync(PredictionContext.After("looking"), Candidates("forward", "for", "at"));

        // Abandoned work must stop rather than compete for the CPU with the request that replaced it.
        var abandoned = await older.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "forward", "for", "at" }, WordsOf(abandoned));
    }

    [Fact]
    public async Task An_inference_that_runs_too_long_is_abandoned()
    {
        var options = new NeuralRerankOptions { Timeout = TimeSpan.FromMilliseconds(40) };
        var fake = new FakeReranker(("at", -0.1)) { Delay = TimeSpan.FromSeconds(5) };
        using var coordinator = new NeuralRerankCoordinator(fake, options);
        var candidates = Candidates("forward", "for", "at");

        var result = await coordinator.RerankAsync(PredictionContext.After("looking"), candidates)
                                      .WaitAsync(TimeSpan.FromSeconds(3));

        // A late answer is worthless, so waiting longer buys only a stale result to throw away.
        Assert.Equal(WordsOf(candidates), WordsOf(result));
    }

    [Fact]
    public async Task An_externally_cancelled_request_leaves_the_order_alone()
    {
        var fake = new FakeReranker(("at", -0.1)) { Delay = TimeSpan.FromSeconds(5) };
        using var coordinator = new NeuralRerankCoordinator(fake);
        using var cts = new CancellationTokenSource();
        var candidates = Candidates("forward", "for", "at");

        var pending = coordinator.RerankAsync(PredictionContext.After("looking"), candidates, cts.Token);
        cts.Cancel();

        Assert.Equal(WordsOf(candidates), WordsOf(await pending.WaitAsync(TimeSpan.FromSeconds(2))));
    }

    [Fact]
    public async Task Only_the_configured_number_of_candidates_is_ever_scored()
    {
        var seen = 0;
        var fake = new CountingReranker(count => seen = count);
        using var coordinator = new NeuralRerankCoordinator(fake, new NeuralRerankOptions { MaxCandidates = 3 });

        await coordinator.RerankAsync(
            PredictionContext.After("looking"), Candidates("a", "b", "c", "d", "e", "f", "g"));

        Assert.Equal(3, seen);
    }

    private sealed class CountingReranker : INeuralReranker
    {
        private readonly Action<int> _report;

        public CountingReranker(Action<int> report) => _report = report;

        public bool IsReady => true;

        public Task<IReadOnlyDictionary<string, double>?> ScoreAsync(
            PredictionContext context, IReadOnlyList<string> candidates, CancellationToken cancellationToken)
        {
            _report(candidates.Count);
            return Task.FromResult<IReadOnlyDictionary<string, double>?>(null);
        }
    }
}
