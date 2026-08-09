namespace WordStrip.Core.Input;

public sealed class WordCommittedEventArgs : EventArgs
{
    public required string Word { get; init; }
    public required char BoundaryChar { get; init; }

    /// <summary>
    /// The words already behind the caret when this one was finished, oldest first.
    ///
    /// <para>Snapshotted before the history is updated, so it is the context the word was typed <em>in</em>
    /// rather than the context that now includes it. Reconstructing this from
    /// <see cref="TypingSession.RecentWords"/> afterwards would be wrong in two different ways depending on
    /// the boundary character — an ordinary space appends the word, while a full stop clears the history
    /// entirely — and personal learning needs the pair to be exact.</para>
    /// </summary>
    public IReadOnlyList<string> PrecedingWords { get; init; } = Array.Empty<string>();
}
