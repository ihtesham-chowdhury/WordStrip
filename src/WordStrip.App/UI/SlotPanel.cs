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
/// <para><b>Fixed geometry.</b> In slot mode the panel is exactly <see cref="SlotCount"/> slots of
/// <see cref="PreferredSlotWidth"/> each, whatever the words in them. Its size therefore never changes while
/// someone types, and neither does anything above it: a word changing length re-measures its own chip and
/// stops here, because this panel's answer to "how big are you" is the same as it was a keystroke ago. Every
/// earlier arrangement let the words influence the geometry in some way — stretching to fill a width,
/// borrowing from a neighbour, growing a row — and every one of them showed up as the bar moving under the
/// user's eyes. A word too long for its slot is shrunk and then shortened from the middle by
/// <see cref="ElidedText"/>; it never widens anything.</para>
/// </summary>
public sealed class SlotPanel : Panel
{
    /// <summary>
    /// Off, this behaves as an ordinary horizontal stack sized to its content — which is what the strip does
    /// when the user has not asked for a fixed width, and what the settings-window preview wants.
    /// </summary>
    public static readonly DependencyProperty UseSlotsProperty = DependencyProperty.Register(
        nameof(UseSlots), typeof(bool), typeof(SlotPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// The width of every slot. Set from the same "roughly how many characters" formula the window uses to
    /// decide how many slots can exist at all, so the two numbers can never disagree with each other.
    /// </summary>
    public static readonly DependencyProperty PreferredSlotWidthProperty = DependencyProperty.Register(
        nameof(PreferredSlotWidth), typeof(double), typeof(SlotPanel),
        new FrameworkPropertyMetadata(80.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double PreferredSlotWidth
    {
        get => (double)GetValue(PreferredSlotWidthProperty);
        set => SetValue(PreferredSlotWidthProperty, value);
    }

    /// <summary>How many slots the panel lays out in slot mode — its width is this many slots, filled or not.</summary>
    public static readonly DependencyProperty SlotCountProperty = DependencyProperty.Register(
        nameof(SlotCount), typeof(int), typeof(SlotPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    /// <summary>
    /// How many leading slots hold a candidate. Dividers are drawn only between those: a hairline beside an
    /// empty slot reads as a missing word rather than as structure.
    /// </summary>
    public static readonly DependencyProperty FilledCountProperty = DependencyProperty.Register(
        nameof(FilledCount), typeof(int), typeof(SlotPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int FilledCount
    {
        get => (int)GetValue(FilledCountProperty);
        set => SetValue(FilledCountProperty, value);
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
        new FrameworkPropertyMetadata(0.30, FrameworkPropertyMetadataOptions.AffectsRender));

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

        if (!UseSlots || count == 0) return MeasureAsStack(availableSize);

        var slots = Math.Max(1, SlotCount);

        // One width for every slot, from the font and the configured count alone. The ceiling can only ever
        // make the slots narrower — which is decided by the settings, not by what is being typed.
        var slotWidth = PreferredSlotWidth;
        if (!double.IsInfinity(availableSize.Width) && slotWidth * slots > availableSize.Width)
            slotWidth = Math.Max(1, availableSize.Width / slots);

        if (_slotWidths.Length != count) _slotWidths = new double[count];

        var height = 0.0;
        for (var i = 0; i < count; i++)
        {
            _slotWidths[i] = i < slots ? slotWidth : 0;

            var child = InternalChildren[i];
            child.Measure(new Size(_slotWidths[i], availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        SyncDividers();
        return new Size(slotWidth * slots, height);
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
        var filled = Math.Min(FilledCount, InternalChildren.Count);
        if (!UseSlots || DividerBrush is null || filled < 2) return;
        if (DividerThickness <= 0 || ActualHeight <= 0) return;

        var inset = ActualHeight * Math.Clamp(DividerInset, 0, 0.45);
        var top = inset;
        var height = ActualHeight - (inset * 2);
        if (height <= 0) return;

        // Snapped to whole device pixels: a hairline landing on a half pixel renders as two grey rows rather
        // than one crisp line, which on a divider repeated across the strip is very visible.
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var x = 0.0;

        for (var i = 0; i < filled - 1; i++)
        {
            x += i < _slotWidths.Length ? _slotWidths[i] : 0;

            var snapped = Math.Round(x * dpi) / dpi;
            drawingContext.DrawRectangle(DividerBrush, null,
                new Rect(snapped - (DividerThickness / 2), top, DividerThickness, height));
        }
    }
}
