using WordStrip.Core.Automation;
using WordStrip.Core.Prediction;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// What the bar needs in order to render one update: the candidates to show (empty means "hide"), and
/// where the text caret is, for caret-following placement. Caret is null when the focused control doesn't
/// report one.
/// </summary>
/// <param name="IsIdle">
/// True when these are next-word predictions shown between words, false when they complete something the
/// user is part-way through typing. Purely descriptive of the payload — both kinds behave identically at the
/// keyboard, since the bar claims Tab whenever it is showing anything.
///
/// <para>It did briefly gate key handling, back when the between-words bar deliberately claimed nothing.
/// That was reversed after use: it left the predictions reachable only by mouse, on exactly the path where
/// they matter most. See <c>BarInputRouter</c>.</para>
/// </param>
public readonly record struct SuggestionUpdate(
    IReadOnlyList<Suggestion> Suggestions,
    CaretRect? Caret,
    bool IsIdle = false)
{
    public static SuggestionUpdate Empty { get; } = new(Array.Empty<Suggestion>(), null);
}
