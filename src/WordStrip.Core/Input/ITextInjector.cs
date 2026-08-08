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
}
