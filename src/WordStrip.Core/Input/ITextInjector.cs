namespace WordStrip.Core.Input;

/// <summary>
/// Abstraction over "make the replacement text appear in the focused control." Kept separate from the
/// hook/prediction logic specifically so the current SendInput-based implementation can be swapped for a
/// Text Services Framework backend later without touching anything else in the app.
/// </summary>
public interface ITextInjector
{
    /// <summary>
    /// Replaces a word the user is still in the middle of typing (no boundary character sent yet) with
    /// <paramref name="replacement"/>. Returns false, having changed nothing, when the field can be read and
    /// does not end with <paramref name="typedWord"/>.
    /// </summary>
    bool ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace);

    /// <summary>
    /// Replaces a word that was already committed with a boundary character (space/punctuation already typed
    /// after it) with <paramref name="replacement"/>, preserving that boundary character. Returns false,
    /// having changed nothing, when the field can be read and ends with neither the word nor the word and its
    /// boundary — the boundary may legitimately still be on its way to the control.
    /// </summary>
    bool ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement);

    /// <summary>
    /// Replaces exactly <paramref name="existing"/>, which must be the text immediately before the caret, with
    /// exactly <paramref name="replacement"/>. No case matching and no implied space: the caller has already
    /// decided the final text, which is what lets it replace that same span again later without guessing at
    /// how much to delete. Only the part after the shared prefix is deleted and retyped.
    ///
    /// <para>Returns false, having changed nothing, when the injector can see the field and it does not in
    /// fact end with <paramref name="existing"/> — the caret moved, the text changed without a keystroke, a
    /// selection is active. Where the field cannot be read, the caller's record is trusted and this returns
    /// true.</para>
    /// </summary>
    bool ReplaceText(string existing, string replacement);
}
