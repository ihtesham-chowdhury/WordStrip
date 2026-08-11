using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.Neural;

namespace WordStrip.Neural;

/// <summary>
/// Scores candidates with a local ONNX causal language model.
///
/// <para><b>One forward pass scores the whole list.</b> A causal model produces a distribution over its
/// entire vocabulary from a single pass over the context, so every candidate is scored by reading its own
/// entry out of that one result. Running the model per candidate would be hopeless: the statistical stack
/// answers in tens of microseconds and one pass here costs tens of milliseconds, so the difference between
/// one pass and seven is the difference between usable and not.</para>
///
/// <para><b>Candidates are scored on their first token.</b> A word may tokenise into several pieces, and
/// scoring all of them properly means one extra pass per piece. The first token carries most of the signal —
/// it is where the model commits to a word — and this only ever reorders a list the statistical engine
/// already approved, so an approximation costs at most a slightly worse ordering, never a wrong word.</para>
///
/// <para><b>Everything is optional and everything can fail.</b> A missing model, a corrupt file, an ONNX
/// runtime that will not initialise: all end with <see cref="IsReady"/> false and every call returning
/// null, which the coordinator reads as "no opinion" and keeps the statistical order.</para>
/// </summary>
public sealed class OnnxNeuralReranker : INeuralReranker, IDisposable
{
    /// <summary>
    /// How many tokens of history to feed the model. Attention cost grows with the square of this, and a
    /// keyboard has no use for long-range context — the words immediately before the caret are what decide
    /// the next one.
    /// </summary>
    private const int MaxContextTokens = 48;

    private readonly object _gate = new();
    private InferenceSession? _session;
    private BpeTokenizer? _tokenizer;
    private string? _inputIdsName;
    private string? _attentionMaskName;
    private string? _positionIdsName;
    private string? _logitsName;
    private int _numLayers;
    private int _numHeads;
    private int _headDimension;

    public bool IsReady { get; private set; }

    /// <summary>Why the model is not available, for the settings window. Null when it loaded.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Milliseconds the model took to load, for the benchmarks the phase requires.</summary>
    public double LoadMilliseconds { get; private set; }

    /// <summary>
    /// Why the last scoring attempt produced nothing, or null if it succeeded.
    ///
    /// <para>Recorded rather than merely swallowed. Returning null on failure is the right behaviour — the
    /// caller keeps the statistical order and the user notices nothing — but a failure that leaves no trace
    /// anywhere is indistinguishable from a model that simply has no opinion, and the two need very
    /// different responses from whoever is debugging it.</para>
    /// </summary>
    public string? LastScoreError { get; private set; }

    /// <summary>
    /// Loads the model. Returns false rather than throwing — the app must carry on regardless, and a
    /// failure here is an ordinary state to report, not an exception to propagate through prediction.
    /// </summary>
    public bool TryLoad(NeuralModelStore store)
    {
        lock (_gate)
        {
            if (IsReady) return true;

            if (!store.IsDownloaded)
            {
                LoadError = "The model has not been downloaded.";
                return false;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var options = new SessionOptions
                {
                    // One thread each. The bar is one small inference at a time on a machine whose owner is
                    // typing; spinning up a thread pool to race through it would take CPU away from the
                    // application they are actually using.
                    IntraOpNumThreads = 1,
                    InterOpNumThreads = 1,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };

                _session = new InferenceSession(store.PathOf("model.onnx"), options);

                using var vocab = File.OpenRead(store.PathOf("vocab.json"));
                using var merges = File.OpenRead(store.PathOf("merges.txt"));
                _tokenizer = BpeTokenizer.Create(vocab, merges);

                ResolveGraph();

                stopwatch.Stop();
                LoadMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                IsReady = true;
                LoadError = null;
                return true;
            }
            catch (Exception ex)
            {
                // Corrupt download, unsupported CPU, missing native library — all the same to the caller.
                _session?.Dispose();
                _session = null;
                _tokenizer = null;
                IsReady = false;
                LoadError = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Reads the input and output names, and the shape of the past-key-value inputs, off the graph.
    ///
    /// <para>Read rather than hard-coded because exported models disagree about naming — some call it
    /// <c>logits</c>, some <c>last_hidden_state</c>; some want a full set of empty past-key-value tensors and
    /// some do not. Assuming one convention makes the code work with exactly the file it was written against
    /// and fail confusingly with any other.</para>
    /// </summary>
    private void ResolveGraph()
    {
        var session = _session!;

        foreach (var input in session.InputMetadata)
        {
            if (input.Key.Contains("input_ids", StringComparison.OrdinalIgnoreCase)) _inputIdsName = input.Key;
            else if (input.Key.Contains("attention_mask", StringComparison.OrdinalIgnoreCase)) _attentionMaskName = input.Key;
            else if (input.Key.Contains("position_ids", StringComparison.OrdinalIgnoreCase)) _positionIdsName = input.Key;
        }

        _logitsName = session.OutputMetadata.Keys.FirstOrDefault(k => k.Contains("logits", StringComparison.OrdinalIgnoreCase))
                      ?? session.OutputMetadata.Keys.First();

        // past_key_values.N.key / .value — count the layers and read the head shape off one of them.
        var pastInputs = session.InputMetadata.Where(i => i.Key.Contains("past", StringComparison.OrdinalIgnoreCase)).ToList();
        _numLayers = pastInputs.Count / 2;

        if (pastInputs.Count > 0)
        {
            var shape = pastInputs[0].Value.Dimensions;   // [batch, heads, sequence, headDim]
            if (shape.Length == 4)
            {
                _numHeads = shape[1] > 0 ? shape[1] : 12;
                _headDimension = shape[3] > 0 ? shape[3] : 64;
            }
        }

        if (_inputIdsName is null)
            throw new InvalidOperationException("The model has no input_ids input; it is not a causal language model.");
    }

    public Task<IReadOnlyDictionary<string, double>?> ScoreAsync(
        PredictionContext context,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        // An already-cancelled request is answered, not thrown at. The contract is that no opinion comes
        // back as null; handing the token to Task.Run instead would return a cancelled task and throw out of
        // the caller's await, which is a different thing entirely and one the coordinator would have to
        // catch on a path where nothing has gone wrong.
        if (!IsReady || candidates.Count == 0 || cancellationToken.IsCancellationRequested)
            return Task.FromResult<IReadOnlyDictionary<string, double>?>(null);

        // Off the caller's thread: this is tens of milliseconds and the caller is on the typing path.
        return Task.Run<IReadOnlyDictionary<string, double>?>(() =>
        {
            try
            {
                var scores = Score(context, candidates, cancellationToken);
                LastScoreError = scores is null ? "no scores produced" : null;
                return scores;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                // An inference failure must never reach the bar. The statistical order is already an answer.
                LastScoreError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        });
    }

    private IReadOnlyDictionary<string, double>? Score(
        PredictionContext context, IReadOnlyList<string> candidates, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(context);
        if (prompt.Length == 0) return null;

        lock (_gate)
        {
            if (!IsReady) return null;

            var tokens = _tokenizer!.EncodeToIds(prompt);
            if (tokens.Count == 0) return null;

            if (tokens.Count > MaxContextTokens)
                tokens = tokens.Skip(tokens.Count - MaxContextTokens).ToList();

            cancellationToken.ThrowIfCancellationRequested();

            var length = tokens.Count;
            var ids = new DenseTensor<long>(new[] { 1, length });
            var mask = new DenseTensor<long>(new[] { 1, length });
            var positions = new DenseTensor<long>(new[] { 1, length });

            for (var i = 0; i < length; i++)
            {
                ids[0, i] = tokens[i];
                mask[0, i] = 1;

                // Simply 0..n-1. There is no cached history to offset against, because this is one pass over
                // the whole prompt rather than an incremental decode.
                positions[0, i] = i;
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputIdsName!, ids),
            };

            if (_attentionMaskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_attentionMaskName, mask));

            // Required by some exports and absent from others — which is why the graph is inspected rather
            // than assumed. Omitting it against a model that wants it fails deep inside the embedding
            // lookup with a message about a Gather node, a long way from anything recognisable.
            if (_positionIdsName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_positionIdsName, positions));

            // Empty history: this is a single pass over the whole prompt, not an incremental decode, so
            // there is nothing cached from a previous step to hand back in.
            for (var layer = 0; layer < _numLayers; layer++)
            {
                var empty = new DenseTensor<float>(new[] { 1, _numHeads, 0, _headDimension });
                inputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{layer}.key", empty));
                inputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{layer}.value", empty));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var results = _session!.Run(inputs, new[] { _logitsName! });
            var logits = results.First().AsTensor<float>();

            // Only the final position matters: that is the model's prediction for what comes next.
            var vocabSize = logits.Dimensions[^1];
            var lastPosition = logits.Dimensions[1] - 1;

            var row = new float[vocabSize];
            for (var v = 0; v < vocabSize; v++) row[v] = logits[0, lastPosition, v];

            var logSoftmaxDenominator = LogSumExp(row);

            var scores = new Dictionary<string, double>(candidates.Count, StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The leading space matters: GPT-2's vocabulary distinguishes "forward" at the start of a
                // line from " forward" mid-sentence, and mid-sentence is nearly always the case here.
                var candidateTokens = _tokenizer.EncodeToIds(" " + candidate);
                if (candidateTokens.Count == 0) continue;

                var first = candidateTokens[0];
                if (first < 0 || first >= vocabSize) continue;

                scores[candidate] = row[first] - logSoftmaxDenominator;
            }

            return scores.Count == 0 ? null : scores;
        }
    }

    /// <summary>
    /// Normalises the logits into log probabilities, subtracting the maximum first so exponentiating cannot
    /// overflow — logits routinely reach values whose exponential does not fit in a float.
    /// </summary>
    private static double LogSumExp(float[] values)
    {
        var max = float.NegativeInfinity;
        foreach (var value in values) if (value > max) max = value;

        double sum = 0;
        foreach (var value in values) sum += Math.Exp(value - max);

        return max + Math.Log(sum);
    }

    /// <summary>The text handed to the model: the words before the caret, as ordinary prose.</summary>
    private static string BuildPrompt(PredictionContext context)
    {
        var words = context.PrecedingWords;
        if (words.Count == 0) return string.Empty;

        return string.Join(' ', words).Trim();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
            IsReady = false;
        }
    }
}
