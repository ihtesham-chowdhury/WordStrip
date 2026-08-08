namespace WordStrip.Core.Input;

public sealed class WordCommittedEventArgs : EventArgs
{
    public required string Word { get; init; }
    public required char BoundaryChar { get; init; }
}
