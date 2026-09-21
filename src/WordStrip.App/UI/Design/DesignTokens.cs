namespace WordStrip.App.UI.Design;

/// <summary>
/// The numbers the interface is built from, in one place.
///
/// <para>This is deliberately not a framework. It exists because the same values — a control's height, the
/// gap between a label and its control, how heavy a selected word is — were being re-decided in each file
/// that needed them, and two places that mean "one step of spacing" have to agree or the interface stops
/// looking like one system. Material colour is not here: a theme's surface, selection and text live in
/// <see cref="Theming.ThemeVariant"/>, and the bar's own sizes are derived from one scale factor in
/// <see cref="GlassMetrics"/>. This layer holds what is true across every theme.</para>
///
/// <para>Everything is in device-independent units, so it scales with the display.</para>
/// </summary>
public static class DesignTokens
{
    /// <summary>
    /// Type. One family with an explicit emoji fallback, because <see cref="ElidedText"/> draws with an
    /// explicit Typeface and only falls back within this list.
    /// </summary>
    public static class Type
    {
        public const string Family = "Segoe UI Variable Text, Segoe UI, Segoe UI Emoji";

        /// <summary>The Settings window's display family. Variable Display is drawn for larger sizes.</summary>
        public const string DisplayFamily = "Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI";

        public const double Title = 20;
        public const double Section = 14;
        public const double Body = 13.5;
        public const double Caption = 12;

        /// <summary>Secondary text: a supporting line under a setting, a theme's one-line description.</summary>
        public const double MutedOpacity = 0.62;

        /// <summary>A suggestion that is not the first one. Quieter, but never so quiet it reads as disabled.</summary>
        public const double AlternateOpacity = 0.78;
    }

    /// <summary>Spacing, on a four-unit rhythm. Named by use rather than by size, so a change of mind stays local.</summary>
    public static class Space
    {
        public const double Hairline = 1;
        public const double Tight = 4;
        public const double Inline = 8;
        public const double Row = 12;
        public const double Block = 16;
        public const double Section = 24;
        public const double Page = 32;
    }

    /// <summary>Corner radii. The bar's own radius is the theme's; these are for the Settings surfaces.</summary>
    public static class Radius
    {
        public const double Control = 6;
        public const double Card = 10;
        public const double Preview = 12;
        public const double Pill = 999;
    }

    /// <summary>Control metrics. One height for anything a pointer aims at, so rows line up down a page.</summary>
    public static class Control
    {
        public const double Height = 32;
        public const double SmallHeight = 26;
        public const double ToggleWidth = 40;
        public const double ToggleHeight = 22;
        public const double ToggleKnob = 14;
        public const double MinTouchTarget = 32;
        public const double BorderThickness = 1;
    }

    /// <summary>The keyboard focus ring: one shape and width everywhere, drawn outside the control it belongs to.</summary>
    public static class Focus
    {
        public const double Thickness = 2;
        public const double Offset = 2;
        public const double Radius = 8;
    }

    /// <summary>
    /// Durations in seconds. The bar's spring lives in <see cref="MotionProfile"/>; these are the plain
    /// transitions the Settings window uses, and the one number that matters most — how long a hover takes
    /// to register — is deliberately short enough to feel like a response rather than an animation.
    /// </summary>
    public static class Motion
    {
        public const double HoverIn = 0.09;
        public const double HoverOut = 0.14;
        public const double Press = 0.06;
        public const double Selection = 0.18;
        public const double PageChange = 0.16;
    }

    /// <summary>
    /// How the bar's two states differ. The selection surface is the same shape in both; only its strength
    /// changes, so moving from "Space will take this" to "I am cycling candidates" reads as the same object
    /// becoming more certain rather than as a different treatment.
    /// </summary>
    public static class Selection
    {
        /// <summary>The user is driving: Tab has put a candidate into the text.</summary>
        public const double ActiveOpacity = 1.0;

        /// <summary>Space would commit the first candidate. Present, quieter, and without the position indicator.</summary>
        public const double ArmedOpacity = 0.62;
    }
}
