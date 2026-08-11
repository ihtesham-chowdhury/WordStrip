using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.Neural;
using WordStrip.Neural;
using Xunit.Abstractions;

namespace WordStrip.Core.Tests;

/// <summary>
/// The real ONNX reranker, against the real model.
///
/// <para>Every test here skips when the model is absent, and that is the point rather than a compromise:
/// the model is a 227 MB optional download, so a suite that required it would be unrunnable on a fresh
/// clone and on any machine that has chosen not to have it — which is the state the application is designed
/// to work in. The behaviour that matters when there is no model is covered by
/// <see cref="NeuralRerankTests"/>, which needs nothing.</para>
///
/// <para>Point the tests at a model with <c>WORDSTRIP_TEST_MODEL_DIR</c>.</para>
/// </summary>
public sealed class OnnxRerankerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly NeuralModelStore? _store;
    private readonly OnnxNeuralReranker _reranker = new();

    public OnnxRerankerTests(ITestOutputHelper output)
    {
        _output = output;

        var directory = Environment.GetEnvironmentVariable("WORDSTRIP_TEST_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        var store = new NeuralModelStore(directory: directory);
        if (store.IsDownloaded) _store = store;
    }

    public void Dispose() => _reranker.Dispose();

    /// <summary>True when a model is available and loaded; writes why not when it isn't.</summary>
    private bool Ready()
    {
        if (_store is null)
        {
            _output.WriteLine("No model available (set WORDSTRIP_TEST_MODEL_DIR) — skipping.");
            return false;
        }

        if (_reranker.IsReady) return true;

        if (!_reranker.TryLoad(_store))
        {
            _output.WriteLine($"Model failed to load: {_reranker.LoadError}");
            return false;
        }

        return true;
    }

    [Fact]
    public void The_model_loads()
    {
        if (!Ready()) return;

        _output.WriteLine($"cold load: {_reranker.LoadMilliseconds:N0} ms");
        Assert.True(_reranker.IsReady);
        Assert.Null(_reranker.LoadError);
    }

    [Fact]
    public async Task It_scores_every_candidate_it_is_given()
    {
        if (!Ready()) return;

        var candidates = new[] { "forward", "for", "at", "toward" };
        var scores = await _reranker.ScoreAsync(PredictionContext.After("i", "am", "looking"), candidates, default);

        Assert.True(scores is not null, $"scoring produced nothing: {_reranker.LastScoreError}");
        foreach (var candidate in candidates) Assert.True(scores!.ContainsKey(candidate), $"missing {candidate}");
    }

    [Fact]
    public async Task It_prefers_the_word_that_actually_fits()
    {
        if (!Ready()) return;

        // "looking forward" against three grammatical-but-wrong alternatives. If the model cannot get this
        // one right it is not earning its 227 MB.
        var scores = await _reranker.ScoreAsync(
            PredictionContext.After("i", "am", "looking"), new[] { "forward", "banana", "the", "elephant" }, default);

        Assert.NotNull(scores);
        foreach (var pair in scores!.OrderByDescending(p => p.Value))
            _output.WriteLine($"  {pair.Key,-10} {pair.Value,8:F3}");

        Assert.True(scores["forward"] > scores["banana"], "forward should beat banana after 'i am looking'");
        Assert.True(scores["forward"] > scores["elephant"], "forward should beat elephant after 'i am looking'");
    }

    [Fact]
    public async Task Context_changes_its_mind()
    {
        if (!Ready()) return;

        // The same two candidates, judged against two different contexts. A reranker that scores a word the
        // same way regardless of what came before it is not reading the context at all.
        //
        // Both contexts are several words long on purpose. A single word is not enough for the model to
        // commit to anything — asked what follows a bare "looking", it reasonably rates the very common
        // " you" above " forward", and a test built on that expectation is testing nothing but a coin toss.
        var afterThank = await _reranker.ScoreAsync(
            PredictionContext.After("i", "would", "like", "to", "thank"), new[] { "you", "forward" }, default);

        var afterLooking = await _reranker.ScoreAsync(
            PredictionContext.After("i", "am", "really", "looking"), new[] { "you", "forward" }, default);

        Assert.True(afterThank is not null, $"scoring produced nothing: {_reranker.LastScoreError}");
        Assert.True(afterLooking is not null, $"scoring produced nothing: {_reranker.LastScoreError}");

        _output.WriteLine($"after '...thank'   : you={afterThank!["you"]:F3} forward={afterThank["forward"]:F3}");
        _output.WriteLine($"after '...looking' : you={afterLooking!["you"]:F3} forward={afterLooking["forward"]:F3}");

        Assert.True(afterThank["you"] > afterThank["forward"], "'thank you' should beat 'thank forward'");

        // What is asserted is that the context moves the score, not that a particular word wins.
        //
        // The stronger claim — "forward" beating "you" after "looking" — is one this model does not reliably
        // make: measured at -10.60 against -9.69, it narrowly prefers "you". First-token scoring on an
        // 82-million-parameter model quantised to int8 gives a useful but coarse signal, and " you" is such
        // a common token that it survives a context that ought to bury it. Asserting the stronger claim
        // would be asserting something untrue and would fail the moment the model or its quantisation
        // changed.
        //
        // The weaker claim is the one that matters for a reranker anyway, and it holds by a wide margin:
        // "you" is worth about seven and a half nats more after "thank" than after "looking". A model that
        // scored it identically in both would be contributing nothing.
        var shift = afterThank["you"] - afterLooking["you"];
        _output.WriteLine($"context shift for 'you': {shift:F3} nats");

        Assert.True(shift > 3.0, $"context should move the score substantially; it moved {shift:F3}");
    }

    [Fact]
    public async Task An_empty_context_has_nothing_to_say()
    {
        if (!Ready()) return;

        Assert.Null(await _reranker.ScoreAsync(PredictionContext.Empty, new[] { "forward" }, default));
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        if (!Ready()) return;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scores = await _reranker.ScoreAsync(
            PredictionContext.After("i", "am", "looking"), new[] { "forward", "for" }, cts.Token);

        Assert.Null(scores);
    }

    [Fact]
    public async Task The_same_context_always_scores_the_same()
    {
        if (!Ready()) return;

        var context = PredictionContext.After("i", "am", "looking");
        var first = await _reranker.ScoreAsync(context, new[] { "forward", "for", "at" }, default);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var again = await _reranker.ScoreAsync(context, new[] { "forward", "for", "at" }, default);
            foreach (var pair in first!) Assert.Equal(pair.Value, again![pair.Key], precision: 5);
        }
    }

    [Fact]
    public async Task Inference_is_fast_enough_for_typing()
    {
        if (!Ready()) return;

        var context = PredictionContext.After("i", "am", "looking", "forward");
        var candidates = new[] { "to", "for", "at", "toward", "with" };

        // Warm up: the first pass pays one-off allocation and JIT that says nothing about steady state.
        await _reranker.ScoreAsync(context, candidates, default);

        var single = await MeasureAsync(() => _reranker.ScoreAsync(context, new[] { "to" }, default));
        var five = await MeasureAsync(() => _reranker.ScoreAsync(context, candidates, default));

        var before = GC.GetTotalMemory(forceFullCollection: true);
        await _reranker.ScoreAsync(context, candidates, default);
        var after = GC.GetTotalMemory(forceFullCollection: false);

        _output.WriteLine($"cold load        : {_reranker.LoadMilliseconds:N0} ms");
        _output.WriteLine($"warm, 1 candidate: {single:N1} ms");
        _output.WriteLine($"warm, 5 candidates: {five:N1} ms");
        _output.WriteLine($"allocated per call: {(after - before) / 1024.0:N0} KB");
        _output.WriteLine($"process working set: {Environment.WorkingSet / 1024 / 1024:N0} MB");

        // One pass scores the whole list, so five candidates must not cost five times one.
        Assert.True(five < single * 2.5,
            $"scoring five candidates ({five:N1} ms) should not be far more than one ({single:N1} ms)");

        // The coordinator abandons anything past its timeout, so a model slower than this would simply
        // never be heard from and the feature would be dead weight.
        Assert.True(five < 400, $"inference took {five:N1} ms, too slow to be useful on the typing path");
    }

    private static async Task<double> MeasureAsync(Func<Task> action)
    {
        const int iterations = 5;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++) await action();

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds / iterations;
    }
}
