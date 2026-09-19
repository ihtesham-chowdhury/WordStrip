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

    /// <summary>
    /// The word is known for certain to begin a sentence — the provider saw the full stop, question mark or
    /// exclamation mark before it. False whenever that is merely likely: a capital is only ever added on
    /// knowledge, because capitalising a word mid-sentence is worse than missing one.
    /// </summary>
    public bool StartsSentence { get; init; }
}
