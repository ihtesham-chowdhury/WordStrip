namespace WordStrip.Core.Prediction.Neural;

/// <summary>
/// The reranker used when there is no model: it is never ready and never has an opinion.
///
/// <para>Exists so that "no neural model" is an ordinary object rather than a null to be checked for at
/// every call site. The absence of the feature is the default state of the application — most users will
/// never download a model — so it deserves to be the well-behaved case rather than the exceptional one.</para>
/// </summary>
public sealed class UnavailableNeuralReranker : INeuralReranker
{
    public static UnavailableNeuralReranker Instance { get; } = new();

    public bool IsReady => false;

    public Task<IReadOnlyDictionary<string, double>?> ScoreAsync(
        PredictionContext context,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, double>?>(null);
}
