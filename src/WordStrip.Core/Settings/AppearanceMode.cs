namespace WordStrip.Core.Settings;

/// <summary>
/// Whether the strip picks its light or dark palette from what is behind it, or is simply told which to use.
///
/// <para><see cref="Auto"/> was the only behaviour for a long time and it is genuinely nicer over a stable
/// backdrop: the glass adapts, the way Apple's materials do. It is much less nice over a page whose
/// brightness changes as the user scrolls or switches tabs, where the strip flips palette underneath them.
/// A browser is exactly that case, and a control that changes appearance while you are reading it reads as
/// a fault rather than as adaptation.</para>
///
/// <para>So the sampling stays, and stops being compulsory. Choosing <see cref="Light"/> or
/// <see cref="Dark"/> skips the screen probe entirely — no sampling, no hysteresis, no cost.</para>
/// </summary>
public enum AppearanceMode
{
    /// <summary>Sample the screen behind the strip and adapt. Best over a backdrop that holds still.</summary>
    Auto = 0,

    /// <summary>Always the palette designed for light backdrops. Dark text on a pale scrim.</summary>
    Light = 1,

    /// <summary>Always the palette designed for dark backdrops. Pale text on a dark scrim.</summary>
    Dark = 2,
}
