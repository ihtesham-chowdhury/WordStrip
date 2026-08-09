using WordStrip.Core.Automation;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// What the bar needs in order to render one update: the candidates to show (empty means "hide"), and
/// where the text caret is, for caret-following placement. Caret is null when the focused control doesn't
/// report one.
/// </summary>
/// <param name="IsIdle">
/// True when these are the common words shown between words rather than completions for something the user
/// is part-way through typing. The distinction decides whether the bar is allowed to claim keystrokes: a bar
/// full of completions owns Tab/Space/Enter/Esc, but an idle one must not, or Tab would stop indenting and
/// moving between fields and Esc would stop closing dialogs for as long as the bar is on screen — which,
/// once it persists, is nearly always.
/// </param>
public readonly record struct SuggestionUpdate(
    IReadOnlyList<Suggestion> Suggestions,
    CaretRect? Caret,
    bool IsIdle = false)
{
    public static SuggestionUpdate Empty { get; } = new(Array.Empty<Suggestion>(), null);
}
