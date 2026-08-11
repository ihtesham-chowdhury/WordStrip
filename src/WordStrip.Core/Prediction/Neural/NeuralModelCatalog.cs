namespace WordStrip.Core.Prediction.Neural;

/// <summary>
/// Everything that must be known about a model before it is fetched: what it is, who published it, what it
/// costs to run, and under what licence.
///
/// <para>This exists as code rather than as a note in a document because the phase brief is explicit that a
/// model must not be downloaded without its source, licence and size being documented — and a fact recorded
/// next to the download button is far harder to lose than one recorded in prose. The settings window shows
/// these values to the user <em>before</em> anything is fetched, so the decision is theirs and informed.</para>
/// </summary>
public sealed record NeuralModelDescriptor
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required string License { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>Approximate download size in megabytes, shown before the user commits to fetching it.</summary>
    public required int DownloadMegabytes { get; init; }

    /// <summary>Rough resident memory once loaded, so the cost is stated rather than discovered.</summary>
    public required int ExpectedRamMegabytes { get; init; }

    public required string Quantization { get; init; }
    public required string Requirements { get; init; }

    /// <summary>Plain-language summary for the settings window, not for developers.</summary>
    public required string Summary { get; init; }
}

/// <summary>The models WordStrip knows how to use.</summary>
public static class NeuralModelCatalog
{
    /// <summary>
    /// DistilGPT2 — a distilled, half-size GPT-2 released by Hugging Face.
    ///
    /// <para>Chosen over a larger model for the reason the phase brief gives: a keyboard needs low latency,
    /// a small footprint and predictable output, and a general-purpose LLM is not automatically the right
    /// tool. At 82 million parameters it is small enough to score a candidate list inside a keystroke's
    /// budget on a CPU, and it only ever reranks — it cannot introduce a word the statistical stack did not
    /// already offer.</para>
    ///
    /// <para><b>Licence verified 2026-08-11:</b> Apache 2.0, which permits redistribution. WordStrip still
    /// does not bundle it — the download is the user's explicit choice, so nobody pays for a feature they
    /// did not ask for.</para>
    /// </summary>
    public static NeuralModelDescriptor DistilGpt2 { get; } = new()
    {
        Name = "DistilGPT2",
        Publisher = "Hugging Face",
        License = "Apache 2.0",
        SourceUrl = "https://huggingface.co/distilbert/distilgpt2",
        DownloadMegabytes = 90,
        ExpectedRamMegabytes = 250,
        Quantization = "int8 (ONNX)",
        Requirements = "CPU only; no GPU required. Runs entirely on this machine.",
        Summary =
            "A small language model that reads the words before your cursor and reorders the suggestions " +
            "WordStrip has already chosen. It never adds words of its own, never sends anything anywhere, " +
            "and WordStrip works exactly as it does now without it.",
    };
}
