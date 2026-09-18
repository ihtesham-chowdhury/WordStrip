using System.Globalization;
using System.Windows;
using System.Windows.Media;

// UseWindowsForms adds implicit global usings that collide with WPF on these names. Aliased the same way
// the rest of this folder does, rather than fully qualifying every use.
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace WordStrip.App.UI;

/// <summary>
/// Draws a single line of text, shortening it from the <em>middle</em> when it will not fit.
///
/// <para><b>Why not a TextBlock.</b> WPF offers <c>TextTrimming</c> only at the end of the string, which is
/// the wrong end to lose for the things people put in a personal dictionary. Trimming an address to
/// "Flat 12, 46 Elm…" throws away the part that distinguishes one saved address from another, while
/// "Flat 12,...,&#160;Halsted" keeps both ends and stays identifiable at a glance. Phone keyboards elide from the
/// middle for exactly this reason, and matching that is what makes a fixed-width slot layout viable at all.</para>
///
/// <para>Render-only, like <see cref="GlassPlate"/> and <see cref="SelectionLens"/>: measuring reports what
/// the full string <em>wants</em>, so the panel can decide how much room to give it, and only the drawing is
/// shortened. Nothing here changes the text the user would actually insert.</para>
/// </summary>
public sealed class ElidedText : FrameworkElement
{
    private const string Ellipsis = "...";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ElidedText),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(ElidedText),
        // The emoji face is part of the default for the reason given at the call site in the bar's XAML:
        // drawing through an explicit Typeface only falls back within the family list it is given.
        new FrameworkPropertyMetadata(new FontFamily("Segoe UI, Segoe UI Emoji"),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(ElidedText),
        new FrameworkPropertyMetadata(14.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight), typeof(FontWeight), typeof(ElidedText),
        new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(ElidedText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Reports the width the full string would like, capped at whatever the parent is offering.
    ///
    /// <para>The cap is what lets a slot panel hand back a narrower width and have this element accept it
    /// rather than overflow — the shortening then happens at render time against the width actually
    /// granted.</para>
    /// </summary>
    /// <summary>
    /// How far the font may shrink before giving up on showing the whole word and turning to the ellipsis.
    /// Gboard's own move for a word that's close to fitting: a smaller whole word reads better than a
    /// truncated one at full size, but shrink too far and it stops looking like the same chip as its
    /// neighbours. 80% is the point past which it starts to look like a rendering glitch rather than a choice.
    /// </summary>
    private const double MinFontScale = 0.80;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measured at full size regardless of how this ends up drawn: what a slot panel needs to know is what
        // the word actually wants, so it can decide whether to hand over enough room before any shrinking or
        // eliding becomes necessary at render time.
        var text = Build(Text, FontSize);
        var width = double.IsInfinity(availableSize.Width)
            ? text.Width
            : Math.Min(text.Width, availableSize.Width);

        return new Size(width, text.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var full = Build(Text, FontSize);

        // Half a pixel of tolerance: a string that measured as exactly fitting should not be shortened by a
        // rounding difference between measure and arrange.
        FormattedText formatted;
        if (full.Width <= ActualWidth + 0.5)
        {
            formatted = full;
        }
        else
        {
            // First choice: shrink the font just enough to show the whole word. Only when even the smallest
            // readable size still overflows does this fall back to a middle ellipsis — measured at that same
            // shrunk size, since a smaller ellipsis keeps more of both ends than a full-size one would.
            var scale = FindFittingScale(ActualWidth);
            var shrunk = Build(Text, FontSize * scale);

            formatted = shrunk.Width <= ActualWidth + 0.5
                ? shrunk
                : Build(Elide(ActualWidth, FontSize * MinFontScale), FontSize * MinFontScale);
        }

        drawingContext.DrawText(formatted, new Point(0, (ActualHeight - formatted.Height) / 2));
    }

    /// <summary>
    /// The largest scale in <c>[MinFontScale, 1.0]</c> at which the whole word fits, or <see cref="MinFontScale"/>
    /// itself if even that isn't enough. Binary search on the continuous scale rather than the string, since
    /// what's varying here is the font size, not which characters survive.
    /// </summary>
    private double FindFittingScale(double maxWidth)
    {
        if (Build(Text, FontSize * MinFontScale).Width > maxWidth) return MinFontScale;

        var low = MinFontScale;
        var high = 1.0;

        for (var i = 0; i < 8; i++)
        {
            var mid = (low + high) / 2;
            if (Build(Text, FontSize * mid).Width <= maxWidth) low = mid; else high = mid;
        }

        return low;
    }

    /// <summary>
    /// The longest head+tail combination that fits. Binary search over how many characters survive, so a
    /// fifty-character entry costs about six measurements rather than fifty.
    /// </summary>
    private string Elide(double maxWidth, double fontSize)
    {
        var text = Text;

        // Not even the ellipsis fits. Drawing a partial ".." would read as a rendering fault; nothing is
        // clearer, and the slot is too narrow to be useful either way.
        if (Build(Ellipsis, fontSize).Width > maxWidth) return string.Empty;

        var low = 0;
        var high = text.Length;
        var best = Ellipsis;

        while (low <= high)
        {
            var keep = (low + high) / 2;

            // The head carries the odd character: the opening of a phrase identifies it more often than its
            // ending does.
            var head = (keep + 1) / 2;
            var tail = keep - head;

            var candidate = string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail, tail));

            if (Build(candidate, fontSize).Width <= maxWidth)
            {
                best = candidate;
                low = keep + 1;
            }
            else
            {
                high = keep - 1;
            }
        }

        return best;
    }

    private FormattedText Build(string text, double fontSize) => new(
        text ?? string.Empty,
        CultureInfo.CurrentUICulture,
        // Qualified: FrameworkElement has its own FlowDirection property, which shadows the type name here.
        System.Windows.FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
        fontSize,
        Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
