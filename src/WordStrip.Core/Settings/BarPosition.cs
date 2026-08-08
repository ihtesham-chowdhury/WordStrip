namespace WordStrip.Core.Settings;

/// <summary>Where the suggestion strip parks itself on screen.</summary>
public enum BarPosition
{
    /// <summary>Fixed just above the taskbar, horizontally centred. Never moves while you type.</summary>
    BottomCenter = 0,

    /// <summary>Follows the text caret, sitting just below it — the placement Windows' own suggestions use.</summary>
    NearCaret = 1,

    /// <summary>Fixed at the top of the display, horizontally centred.</summary>
    TopCenter = 2,
}
