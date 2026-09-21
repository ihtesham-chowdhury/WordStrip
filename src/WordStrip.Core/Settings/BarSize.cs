namespace WordStrip.Core.Settings;

/// <summary>
/// How dense the bar is, as a choice about appearance rather than a number of pixels.
///
/// <para>This replaced a thickness slider. A slider asks the user to pick a size for a bar they are not
/// looking at, in units they have no feel for, and then holds that size whether they are typing eight-point
/// notes or a heading — which is exactly how a helpful strip ends up looking enormous over small text.</para>
/// </summary>
public enum BarSize
{
    /// <summary>Follows the text being written, from the caret's own height. The default.</summary>
    Automatic = 0,

    /// <summary>Fixed and tight. Suits dark, dense environments — code, terminals, notes.</summary>
    Compact = 1,

    /// <summary>Fixed at the size most documents want.</summary>
    Standard = 2,

    /// <summary>Fixed and generous. Suits large text, presentations and anyone who wants more to aim at.</summary>
    Comfortable = 3,
}
