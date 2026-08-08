namespace WordStrip.Core.Settings;

/// <summary>
/// How much the desktop behind the bar is blurred. This is the true backdrop blur, provided by the window
/// manager — distinct from the glass tint, which controls how opaque the material itself is.
/// </summary>
public enum BackdropBlur
{
    /// <summary>No blur. The bar is a plain tinted panel — cheapest to render and the clearest to read.</summary>
    None = 0,

    /// <summary>Use whatever the selected theme was designed around. The default.</summary>
    Auto = 3,

    /// <summary>A subtle, largely static blur of the desktop wallpaper.</summary>
    Subtle = 1,

    /// <summary>Full translucent blur of whatever is directly behind the bar. Closest to Liquid Glass.</summary>
    Full = 2,
}
