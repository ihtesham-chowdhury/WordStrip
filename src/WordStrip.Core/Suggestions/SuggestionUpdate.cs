using WordStrip.Core.Automation;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// What the bar needs in order to render one update: the candidates to show (empty means "hide"), where the
/// text caret is, and which of the bar's two states it is in.
///
/// <para><b>Passive</b> (<see cref="SelectedIndex"/> is -1) is the resting state: the bar is available, not
/// asking for anything, and nothing on it looks selected. <b>Active</b> is only ever entered by the user
/// pressing Tab — the candidate they have just put into their text is highlighted, and a further Tab moves
/// on to the next. Model updates never make the bar active; only a deliberate keystroke does.</para>
/// </summary>
/// <param name="IsIdle">True for next-word predictions shown between words, false for completions of a word
/// in progress. Descriptive of the payload; the controller, not the bar, decides what keys do.</param>
/// <param name="SelectedIndex">The candidate the user has just inserted with Tab, or -1 when passive.</param>
/// <param name="FirstIsArmed">True when Space or closing punctuation would commit the first candidate, so the
/// bar can give it quiet emphasis. Never true when the bar is active.</param>
public readonly record struct SuggestionUpdate(
    IReadOnlyList<Suggestion> Suggestions,
    CaretRect? Caret,
    bool IsIdle = false,
    int SelectedIndex = -1,
    bool FirstIsArmed = false)
{
    public static SuggestionUpdate Empty { get; } = new(Array.Empty<Suggestion>(), null);

    /// <summary>Whether the user is currently driving the bar with Tab.</summary>
    public bool IsActive => SelectedIndex >= 0;
}
