namespace WordStrip.Core.Prediction.NGram;

/// <summary>
/// The on-disk contract for the n-gram model, shared by the builder that writes the files and the model
/// that reads them. It lives here rather than in the builder so the two cannot drift apart silently — a
/// mismatch would not fail to compile, it would just quietly produce no predictions.
///
/// <para><b>Format.</b> One tab-separated record per line, comments and blank lines ignored:</para>
/// <code>
/// # comment
/// looking&#9;forward&#9;-0.6021          (order 2: context word, next word, log10 probability)
/// i&#9;am&#9;looking&#9;-1.2304    (order 3: two context words, next word, log10 probability)
/// </code>
///
/// <para><b>Why text and not a binary blob.</b> The phase brief asks for a format that is versionable and
/// easy to replace or regenerate. Text is diffable, inspectable when a prediction looks wrong, and trivially
/// hand-editable for a one-off experiment. It costs load time against a binary layout, but loading happens
/// once on the background thread that already builds the SymSpell index, where several hundred milliseconds
/// is invisible.</para>
///
/// <para><b>Why probabilities and not counts.</b> The model blends two corpora whose raw counts are not
/// comparable — SymSpell's bigram counts come from Google Books and run to the billions, while counts from a
/// few dozen Gutenberg novels run to the thousands. Summing them would let one source erase the other.
/// Conditional probabilities are directly mixable, so the blend happens at build time and the file records
/// the result.</para>
/// </summary>
public static class NGramFormat
{
    /// <summary>
    /// Pseudo-token marking the start of a sentence, so the model can answer "what word usually opens a
    /// sentence?" rather than falling back to raw word frequency after every full stop. Angle brackets keep
    /// it outside the alphabet of real tokens, which are letters and apostrophes only.
    /// </summary>
    public const string SentenceStart = "<s>";

    public const char FieldSeparator = '\t';
    public const char CommentPrefix = '#';

    /// <summary>File name for a model of the given order, e.g. <c>ngram-2.txt</c>.</summary>
    public static string FileName(int order) => $"ngram-{order}.txt";

    /// <summary>Logical name of the embedded resource for a model of the given order.</summary>
    public static string EmbeddedResourceName(int order) => $"WordStrip.ngram.{order}.txt";
}
