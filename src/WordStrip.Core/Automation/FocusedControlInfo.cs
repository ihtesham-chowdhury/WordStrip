namespace WordStrip.Core.Automation;

/// <summary>A caret rectangle in screen pixels.</summary>
public readonly record struct CaretRect(int Left, int Top, int Right, int Bottom)
{
    public int Height => Bottom - Top;
}

public readonly record struct FocusedControlInfo(
    bool IsStandardEditControl,
    bool IsPasswordField,
    CaretRect? Caret = null);
