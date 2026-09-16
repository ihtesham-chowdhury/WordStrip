using System.Windows;
using System.Windows.Media;

// UseWindowsForms adds implicit global usings that collide with WPF on these names. Aliased the same way
// the rest of this folder does, rather than fully qualifying every use.
using Panel = System.Windows.Controls.Panel;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace WordStrip.App.UI;

/// <summary>
/// Lays the suggestion chips out as fixed columns across the whole strip, with a hairline between each —
/// the arrangement a phone keyboard uses.
///
/// <para><b>Why columns rather than a row of chips.</b> A centred row of short words in a wide strip leaves
/// most of the strip visibly empty, and because the row is centred, every chip slides sideways whenever any
/// word changes length. Dividing the same width into slots uses exactly the same pixels but reads as
/// deliberate structure instead of emptiness, and it holds each suggestion in one place. Gboard's strip has
/// just as much unused space as WordStrip's did; the dividers are the whole difference.</para>
///
/// <para><b>Slots hold still unless they have to move.</b> Every slot starts at an equal share of the width.
/// Only when a word genuinely needs more than its share does anything shift, and then only by borrowing the
/// spare room its neighbours are not using. So the common case — several short words — produces dividers
/// that do not move at all between keystrokes, which is the entire point of the exercise. A word that still
/// does not fit after borrowing is shortened from the middle by <see cref="ElidedText"/> rather than
/// widening the strip.</para>
/// </summary>
public sealed class SlotPanel : Panel
{
    /// <summary>
    /// Relative share of the strip a child asks for. Emoji get less: they are one glyph and giving them a
    /// full word's column would waste the room a word could have used.
    /// </summary>
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(SlotPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetWeight(UIElement element, double value) => element.SetValue(WeightProperty, value);

    public static double GetWeight(UIElement element) => (double)element.GetValue(WeightProperty);

    /// <summary>
    /// Off, this behaves as an ordinary horizontal stack sized to its content — which is what the strip does
    /// when the user has not asked for a fixed width, and what the settings-window preview wants.
    /// </summary>
    public static readonly DependencyProperty UseSlotsProperty = DependencyProperty.Register(
        nameof(UseSlots), typeof(bool), typeof(SlotPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty DividerBrushProperty = DependencyProperty.Register(
        nameof(DividerBrush), typeof(Brush), typeof(SlotPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DividerThicknessProperty = DependencyProperty.Register(
        nameof(DividerThickness), typeof(double), typeof(SlotPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far the divider stops short of the strip's top and bottom, as a fraction of the height.</summary>
    public static readonly DependencyProperty DividerInsetProperty = DependencyProperty.Register(
        nameof(DividerInset), typeof(double), typeof(SlotPanel),
        new FrameworkPropertyMetadata(0.22, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool UseSlots
    {
        get => (bool)GetValue(UseSlotsProperty);
        set => SetValue(UseSlotsProperty, value);
    }

    public Brush? DividerBrush
    {
        get => (Brush?)GetValue(DividerBrushProperty);
        set => SetValue(DividerBrushProperty, value);
    }

    public double DividerThickness
    {
        get => (double)GetValue(DividerThicknessProperty);
        set => SetValue(DividerThicknessProperty, value);
    }

    public double DividerInset
    {
        get => (double)GetValue(DividerInsetProperty);
        set => SetValue(DividerInsetProperty, value);
    }

    private double[] _slotWidths = Array.Empty<double>();

    /// <summary>The widths the dividers currently on screen were drawn against.</summary>
    private double[] _paintedWidths = Array.Empty<double>();

    /// <summary>
    /// Asks for a repaint when the slot boundaries have moved.
    ///
    /// <para>WPF re-runs <see cref="OnRender"/> for a dependency-property change, not for a change of
    /// children, so new words rearrange the chips while leaving the dividers drawn where the <em>previous</em>
    /// words put them — which showed up as boundaries that were missing or in the wrong place depending on
    /// what had been on the strip before.</para>
    ///
    /// <para>Posted rather than called directly: <c>InvalidateVisual</c> also invalidates arrange, so
    /// invoking it from inside a layout pass restarts that pass and WPF eventually gives up with a layout
    /// cycle. Posting it at render priority runs it once this pass has finished. Recording the widths here
    /// rather than in <c>OnRender</c> is what makes a repeat post impossible.</para>
    /// </summary>
    private void SyncDividers()
    {
        if (_paintedWidths.Length == _slotWidths.Length)
        {
            var moved = false;
            for (var i = 0; i < _slotWidths.Length; i++)
            {
                if (Math.Abs(_paintedWidths[i] - _slotWidths[i]) <= 0.01) continue;
                moved = true;
                break;
            }

            if (!moved) return;
        }

        _paintedWidths = (double[])_slotWidths.Clone();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(InvalidateVisual));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            _slotWidths = Array.Empty<double>();
            SyncDividers();
            return new Size(0, 0);
        }

        // Without a finite width to divide there are no slots to speak of, so fall back to stacking. This is
        // also the path the settings preview and the non-fixed-width strip take.
        if (!UseSlots || double.IsInfinity(availableSize.Width))
            return MeasureAsStack(availableSize);

        _slotWidths = new double[count];

        var weights = new double[count];
        var desired = new double[count];
        var totalWeight = 0.0;

        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];

            // Measured unconstrained first: the allocation below needs to know what each chip actually wants
            // before deciding what it gets.
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            desired[i] = child.DesiredSize.Width;

            weights[i] = Math.Max(0.01, GetWeight(child));
            totalWeight += weights[i];
        }

        // Every slot's fair share, then a single round of borrowing from the slots that do not need theirs.
        // One round is enough and is what keeps this stable: a slot only moves when some other slot genuinely
        // could not fit, never merely because a word's width changed.
        var share = new double[count];
        var want = 0.0;
        var spare = 0.0;

        for (var i = 0; i < count; i++)
        {
            share[i] = availableSize.Width * weights[i] / totalWeight;
            want += Math.Max(0, desired[i] - share[i]);
            spare += Math.Max(0, share[i] - desired[i]);
        }

        var transfer = Math.Min(want, spare);

        for (var i = 0; i < count; i++)
        {
            var wants = Math.Max(0, desired[i] - share[i]);
            var spares = Math.Max(0, share[i] - desired[i]);

            _slotWidths[i] = share[i]
                + (want > 0 ? wants * transfer / want : 0)
                - (spare > 0 ? spares * transfer / spare : 0);
        }

        // Second pass at the width each child is actually getting, so anything still too long can shorten
        // itself to fit rather than overflow its column.
        var height = 0.0;
        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(_slotWidths[i], availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        SyncDividers();
        return new Size(availableSize.Width, height);
    }

    private Size MeasureAsStack(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;

        _slotWidths = new double[InternalChildren.Count];

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

            _slotWidths[i] = child.DesiredSize.Width;
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        SyncDividers();
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var width = i < _slotWidths.Length ? _slotWidths[i] : InternalChildren[i].DesiredSize.Width;
            InternalChildren[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!UseSlots || DividerBrush is null || InternalChildren.Count < 2) return;
        if (DividerThickness <= 0 || ActualHeight <= 0) return;

        var inset = ActualHeight * Math.Clamp(DividerInset, 0, 0.45);
        var top = inset;
        var height = ActualHeight - (inset * 2);
        if (height <= 0) return;

        // Snapped to whole device pixels: a hairline landing on a half pixel renders as two grey rows rather
        // than one crisp line, which on a divider repeated across the strip is very visible.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var x = 0.0;

        for (var i = 0; i < InternalChildren.Count - 1; i++)
        {
            x += i < _slotWidths.Length ? _slotWidths[i] : 0;

            var snapped = Math.Round(x * dpi) / dpi;
            drawingContext.DrawRectangle(DividerBrush, null,
                new Rect(snapped - (DividerThickness / 2), top, DividerThickness, height));
        }
    }
}
