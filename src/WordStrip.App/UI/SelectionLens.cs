using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace WordStrip.App.UI;

/// <summary>
/// The travelling highlight behind the selected chip, drawn directly rather than composed from a Border.
///
/// <para>The reason this exists is frame timing. Animating a Border's <c>Width</c> and <c>Canvas.Left</c>
/// makes WPF run a measure and arrange pass on every single frame of the movement, and that layout work is
/// what produced the occasional dropped frame when cycling quickly with Tab. Here the position and width are
/// dependency properties flagged <see cref="FrameworkPropertyMetadataOptions.AffectsRender"/> only, so
/// animating them re-runs <see cref="OnRender"/> and nothing else — no layout, no measure, no arrange.</para>
///
/// <para>The element itself never changes size; it fills the strip and paints the lens wherever the
/// animation currently puts it.</para>
/// </summary>
public sealed class SelectionLens : FrameworkElement
{
    private static FrameworkPropertyMetadata RenderOnly(double defaultValue) =>
        new(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty LensXProperty =
        DependencyProperty.Register(nameof(LensX), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty LensYProperty =
        DependencyProperty.Register(nameof(LensY), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty LensWidthProperty =
        DependencyProperty.Register(nameof(LensWidth), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty LensHeightProperty =
        DependencyProperty.Register(nameof(LensHeight), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SelectionLens),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RimProperty =
        DependencyProperty.Register(nameof(Rim), typeof(Brush), typeof(SelectionLens),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IndicatorProperty =
        DependencyProperty.Register(nameof(Indicator), typeof(Brush), typeof(SelectionLens),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IndicatorThicknessProperty =
        DependencyProperty.Register(nameof(IndicatorThickness), typeof(double), typeof(SelectionLens), RenderOnly(0));

    public static readonly DependencyProperty IndicatorWidthFactorProperty =
        DependencyProperty.Register(nameof(IndicatorWidthFactor), typeof(double), typeof(SelectionLens), RenderOnly(0.42));

    public static readonly DependencyProperty IndicatorGapProperty =
        DependencyProperty.Register(nameof(IndicatorGap), typeof(double), typeof(SelectionLens), RenderOnly(3));

    /// <summary>
    /// The position marker beneath the selected word. Drawn here, from the same animated position and width
    /// as the selection surface, so it travels with it automatically rather than needing its own animation
    /// that could drift out of step.
    /// </summary>
    public Brush? Indicator { get => (Brush?)GetValue(IndicatorProperty); set => SetValue(IndicatorProperty, value); }

    public double IndicatorThickness { get => (double)GetValue(IndicatorThicknessProperty); set => SetValue(IndicatorThicknessProperty, value); }
    public double IndicatorWidthFactor { get => (double)GetValue(IndicatorWidthFactorProperty); set => SetValue(IndicatorWidthFactorProperty, value); }
    public double IndicatorGap { get => (double)GetValue(IndicatorGapProperty); set => SetValue(IndicatorGapProperty, value); }

    public double LensX { get => (double)GetValue(LensXProperty); set => SetValue(LensXProperty, value); }
    public double LensY { get => (double)GetValue(LensYProperty); set => SetValue(LensYProperty, value); }
    public double LensWidth { get => (double)GetValue(LensWidthProperty); set => SetValue(LensWidthProperty, value); }
    public double LensHeight { get => (double)GetValue(LensHeightProperty); set => SetValue(LensHeightProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Rim { get => (Brush?)GetValue(RimProperty); set => SetValue(RimProperty, value); }

    public SelectionLens()
    {
        IsHitTestVisible = false;
    }

    // Purely decorative: it paints inside whatever space the strip gives it and asks for none of its own.
    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (LensWidth <= 0 || LensHeight <= 0 || Opacity <= 0) return;

        var pen = Rim is null ? null : new Pen(Rim, 1);
        pen?.Freeze();

        // Inset by half the pen width so the rim is drawn inside the lens bounds rather than straddling them.
        var inset = pen is null ? 0 : 0.5;
        var rect = new Rect(
            LensX + inset,
            LensY + inset,
            Math.Max(0, LensWidth - (inset * 2)),
            Math.Max(0, LensHeight - (inset * 2)));

        var radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
        drawingContext.DrawRoundedRectangle(Fill, pen, rect, radius, radius);

        if (Indicator is null || IndicatorThickness <= 0) return;

        // Centred under the selected word, a fraction of its width — long enough to read as a position
        // marker, short enough not to compete with the word above it.
        var indicatorWidth = Math.Max(8, LensWidth * IndicatorWidthFactor);
        var indicatorRect = new Rect(
            LensX + (LensWidth - indicatorWidth) / 2,
            LensY + LensHeight + IndicatorGap,
            indicatorWidth,
            IndicatorThickness);

        var indicatorRadius = IndicatorThickness / 2;
        drawingContext.DrawRoundedRectangle(Indicator, null, indicatorRect, indicatorRadius, indicatorRadius);
    }
}
