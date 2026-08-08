using WordStrip.Core.Automation;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// What the bar needs in order to render one update: the candidates to show (empty means "hide"), and
/// where the text caret is, for caret-following placement. Caret is null when the focused control doesn't
/// report one.
/// </summary>
public readonly record struct SuggestionUpdate(IReadOnlyList<Suggestion> Suggestions, CaretRect? Caret)
{
    public static SuggestionUpdate Empty { get; } = new(Array.Empty<Suggestion>(), null);
}
