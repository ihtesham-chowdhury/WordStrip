using System.Linq;
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
/// <para><b>Slots are compact, not stretched.</b> Every slot starts at <see cref="PreferredSlotWidth"/> — a
/// constant sized for a typical predicted word, not a share of whatever width the strip happens to have on
/// offer. Stretching each slot out to fill the strip's full reserved width is what made three short
/// suggestions look as swollen as seven; sizing to the words instead means the bar itself comes out short
/// when there is little to show, the way the count of suggestions varies without the columns ballooning to
/// compensate. Only when a word genuinely needs more than its slot does anything shift, first by borrowing
/// the spare room its neighbours are not using, and only past that by growing the whole row — bounded by
/// whatever ceiling the caller's available width represents — before <see cref="ElidedText"/> takes over
/// with its own shrink-then-ellipsis fallback. So the common case — several ordinary words — produces a row
/// that does not change shape at all between keystrokes, at a width that matches what is actually on it.</para>
/// </summary>
public sealed class SlotPanel : Panel
{
    /// <summary>
    /// Relative share of a slot's width a child asks for. Emoji get less: they are one glyph and giving them
    /// a full word's column would waste the room a word could have used.
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

    /// <summary>
    /// The width one ordinary-weight slot asks for before any borrowing or growth. Set from the same "roughly
    /// how many characters" formula the window uses to decide how many columns can exist at all, so the two
    /// numbers can never disagree with each other.
    /// </summary>
    public static readonly DependencyProperty PreferredSlotWidthProperty = DependencyProperty.Register(
        nameof(PreferredSlotWidth), typeof(double), typeof(SlotPanel),
        new FrameworkPropertyMetadata(80.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double PreferredSlotWidth
    {
        get => (double)GetValue(PreferredSlotWidthProperty);
        set => SetValue(PreferredSlotWidthProperty, value);
    }

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

        // Every slot's preferred share — a constant per unit of weight, not a fraction of whatever width is
        // on offer — clamped so that even this baseline can never itself exceed what the ceiling allows.
        // That clamp is a safety net for a narrow ceiling with many slots; in the ordinary case it never
        // engages, because EffectiveSlotCount has already kept the requested column count within what the
        // ceiling can hold at this same preferred width.
        var unit = totalWeight > 0 ? Math.Min(PreferredSlotWidth, availableSize.Width / totalWeight) : 0;

        var share = new double[count];
        var want = 0.0;
        var spare = 0.0;

        for (var i = 0; i < count; i++)
        {
            share[i] = unit * weights[i];
            want += Math.Max(0, desired[i] - share[i]);
            spare += Math.Max(0, share[i] - desired[i]);
        }

        // One round of borrowing from the slots that do not need their full share. One round is enough and is
        // what keeps this stable: a slot only moves when some other slot genuinely could not fit, never
        // merely because a word's width changed.
        var transfer = Math.Min(want, spare);

        for (var i = 0; i < count; i++)
        {
            var wants = Math.Max(0, desired[i] - share[i]);
            var spares = Math.Max(0, share[i] - desired[i]);

            _slotWidths[i] = share[i]
                + (want > 0 ? wants * transfer / want : 0)
                - (spare > 0 ? spares * transfer / spare : 0);
        }

        // Borrowing alone cannot help when every slot is short of room at once — the collective ask exceeds
        // what the row could reshuffle internally. Only then does the row grow past its preferred total,
        // and only up to the ceiling the caller's available width represents. Whatever is still unmet past
        // that is left for ElidedText to shrink or elide at render time, rather than pushing the strip wider
        // than the bar was allowed to be.
        var unmet = want - spare;
        if (unmet > 0)
        {
            var headroom = Math.Max(0, availableSize.Width - share.Sum());
            var growth = Math.Min(unmet, headroom);

            if (growth > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var stillWants = Math.Max(0, desired[i] - _slotWidths[i]);
                    if (stillWants <= 0) continue;
                    _slotWidths[i] += stillWants * growth / unmet;
                }
            }
        }

        // Second pass at the width each child is actually getting, so anything still too long can shorten
        // itself to fit rather than overflow its column.
        var height = 0.0;
        var totalWidth = 0.0;
        for (var i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(_slotWidths[i], availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            totalWidth += _slotWidths[i];
        }

        SyncDividers();

        // Content-driven, not the full available width: a handful of short words should make for a short
        // row, and this is what lets the window that hosts this panel shrink to match.
        return new Size(totalWidth, height);
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
