namespace WordStrip.Core.Settings;

/// <summary>
/// The bar's visual personality. Every theme is a set of material tokens over the same component, geometry,
/// interaction and motion system — the strip behaves identically in all of them, it just looks different.
/// </summary>
public enum BarTheme
{
    /// <summary>Translucent Windows 11 utility surface. The native-feeling default.</summary>
    FluentAcrylic = 0,

    /// <summary>Calmer and more opaque than Acrylic, with the blur pulled right back.</summary>
    MicaInspired = 1,

    /// <summary>Acrylic with a stronger depth hierarchy and clearer selected-state elevation.</summary>
    FluentDepth = 2,

    /// <summary>Bright, typography-led and restrained. Apple's discipline, not Apple's rendering.</summary>
    AppleFrosted = 3,

    /// <summary>Dark, dense, high contrast — a fast command surface for power users.</summary>
    RaycastFloating = 4,

    /// <summary>Soft, pale and spatial, with generous shadow so it reads as floating above the app.</summary>
    VisionInspired = 5,

    /// <summary>Tonal Material 3 surface with a tinted selection container.</summary>
    Material3 = 6,
}
