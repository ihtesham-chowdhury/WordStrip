using WordStrip.Core.Automation;

namespace WordStrip.Core.Text;

/// <summary>
/// Which mechanism produced a <see cref="TextContext"/>, and by implication how far it can be trusted.
///
/// <para>This is not decoration. The two sources differ in kind, not just in quality: one reconstructs what
/// it believes is near the caret from keystrokes it happened to observe, the other asks the application what
/// is actually there. Code that treats a guess and a reading as interchangeable will eventually correct text
/// it never saw.</para>
/// </summary>
public enum TextContextSource
{
    /// <summary>
    /// Reconstructed from keystrokes by <see cref="Input.TypingSession"/>. A best-effort shadow of the real
    /// text with no view of the document: it can be one character behind if a hook callback was timed out, it
    /// knows nothing that was typed before WordStrip started watching, and anything that could have moved the
    /// caret invisibly makes it give up rather than guess.
    /// </summary>
    KeyboardHook,

    /// <summary>
    /// Read from the document through the Text Services Framework. The text before the caret is what the
    /// application says it is, not what we inferred.
    /// </summary>
    TextServices,
}

/// <summary>
/// Everything the suggestion layer needs to know about the text around the caret, from whichever input
/// mechanism is in play.
///
/// <para>Deliberately a plain value with no Windows types in it beyond <see cref="CaretRect"/>, which is four
/// integers. The prediction engine must never be able to tell whether it is being fed a keyboard hook's guess
/// or a TSF reading — that is the whole point of the abstraction, and the reason a third provider could be
/// added later without touching anything below this line.</para>
///
/// <para><b>Only the minimum is carried.</b> Not the document, not the paragraph, not the sentence — the word
/// in progress and at most two finished words behind it, which is exactly what a trigram model consumes. A
/// provider with access to the entire document must still populate only these fields. That is a privacy
/// requirement from the phase brief, and it is also why this type has no "surrounding text" member for a
/// future implementation to quietly fill with a screenful of someone's email.</para>
/// </summary>
public readonly record struct TextContext(
    bool IsEditable,
    bool IsPasswordField,
    string CurrentWord,
    IReadOnlyList<string> PrecedingWords,
    bool IsAtSentenceStart,
    CaretRect? Caret,
    TextContextSource Source,
    bool HasSelection = false)
{
    /// <summary>
    /// Whether WordStrip may offer suggestions here — and, identically, whether it may learn from what is
    /// typed here. One predicate for both on purpose: a field the app cannot positively identify is one it
    /// must not record from either.
    /// </summary>
    public bool IsSuggestible => IsEditable && !IsPasswordField;

    /// <summary>Nothing is focused, or nothing that accepts text. Every field takes its least-capable value.</summary>
    public static TextContext None { get; } = new(
        IsEditable: false,
        IsPasswordField: false,
        CurrentWord: string.Empty,
        PrecedingWords: Array.Empty<string>(),
        IsAtSentenceStart: true,
        Caret: null,
        Source: TextContextSource.KeyboardHook);
}
