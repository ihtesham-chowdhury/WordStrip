namespace WordStrip.Core.Input;

/// <summary>
/// Abstraction over "make the replacement text appear in the focused control." Kept separate from the
/// hook/prediction logic specifically so the current SendInput-based implementation can be swapped for a
/// Text Services Framework backend later without touching anything else in the app.
/// </summary>
public interface ITextInjector
{
    /// <summary>Replaces a word the user is still in the middle of typing (no boundary character sent yet) with <paramref name="replacement"/>.</summary>
    void ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace);

    /// <summary>Replaces a word that was already committed with a boundary character (space/punctuation already typed after it) with <paramref name="replacement"/>, preserving that boundary character.</summary>
    void ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement);

    /// <summary>
    /// Replaces exactly <paramref name="existing"/>, which must be the text immediately before the caret, with
    /// exactly <paramref name="replacement"/>. No case matching and no implied space: the caller has already
    /// decided the final text, which is what lets it replace that same span again later without guessing at
    /// how much to delete. Only the part after the shared prefix is deleted and retyped.
    /// </summary>
    void ReplaceText(string existing, string replacement);
}
