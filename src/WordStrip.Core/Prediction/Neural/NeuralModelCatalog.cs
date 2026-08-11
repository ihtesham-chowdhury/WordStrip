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

    /// <summary>Files to fetch, as (url, local file name). Sizes are verified, not estimated.</summary>
    public required IReadOnlyList<(string Url, string FileName, long Bytes)> Files { get; init; }

    public long TotalBytes
    {
        get
        {
            long total = 0;
            foreach (var file in Files) total += file.Bytes;
            return total;
        }
    }
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
    /// <para><b>Licence:</b> the upstream model is Apache 2.0, which permits redistribution. The ONNX
    /// conversion is republished by onnx-community and carries no licence statement of its own, so the
    /// upstream terms are what apply — worth stating plainly rather than quietly assuming. WordStrip does
    /// not bundle it either way: the download is the user's explicit choice.</para>
    ///
    /// <para><b>Sizes verified against the publisher on 2026-08-11, not estimated.</b> An earlier figure of
    /// ~90 MB quoted in planning was wrong by a factor of nearly three. int8 is chosen over the smaller
    /// fp16 build deliberately: this runs on the CPU, where fp16 is usually widened back to fp32 and ends
    /// up slower, and latency is the whole constraint here.</para>
    /// </summary>
    public static NeuralModelDescriptor DistilGpt2 { get; } = new()
    {
        Name = "DistilGPT2",
        Publisher = "Hugging Face (ONNX conversion by onnx-community)",
        License = "Apache 2.0 (from the upstream distilbert/distilgpt2 model)",
        SourceUrl = "https://huggingface.co/onnx-community/distilgpt2-ONNX",
        DownloadMegabytes = 227,
        ExpectedRamMegabytes = 400,
        Quantization = "int8 (ONNX)",
        Requirements = "CPU only; no GPU required. Runs entirely on this machine.",
        Summary =
            "A small language model that reads the words before your cursor and reorders the suggestions " +
            "WordStrip has already chosen. It never adds words of its own, never sends anything anywhere, " +
            "and WordStrip works exactly as it does now without it.",

        Files = new[]
        {
            (Base + "onnx/model_int8.onnx", "model.onnx", 236_714_483L),
            (Base + "vocab.json", "vocab.json", 798_156L),
            (Base + "merges.txt", "merges.txt", 456_318L),
        },
    };

    private const string Base = "https://huggingface.co/onnx-community/distilgpt2-ONNX/resolve/main/";
}
