using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WordStrip.App.Interop;
using WordStrip.App.UI.Theming;
using WordStrip.Core.Automation;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WordStrip.App.UI;

/// <summary>
/// The floating Liquid Glass suggestion strip. Owns only presentation — it never talks to the keyboard hook
/// or prediction engine directly. The app-level orchestrator feeds it suggestion lists and Tab/Space/click
/// intent, since keyboard interaction has to be routed through the global hook rather than WPF's own
/// focus-based input (this window deliberately never takes keyboard focus — see <see cref="GlassWindowBehavior"/>).
/// </summary>
public partial class SuggestionBarWindow : Window
{
    private readonly AppSettings _settings;

    private ThemeDefinition _theme = ThemeCatalog.All[0];
    private ThemeBrushes _brushes = ThemeBrushes.Build(ThemeCatalog.All[0], GlassAppearance.OverLight, 0.62, true, false);
    private GlassMetrics _metrics = GlassMetrics.ForScale(1.0, ThemeCatalog.All[0].CornerRadius, true);
    private MotionProfile _motion = MotionProfile.ForSpeed(1.0);
    private SolidColorBrush _restingTextBrush = new(Colors.White);
    private SolidColorBrush _selectedTextBrush = new(Colors.Black);

    public ObservableCollection<SuggestionChipViewModel> Chips { get; } = new();

    private IReadOnlyList<Suggestion> _currentSuggestions = Array.Empty<Suggestion>();
    private int _selectedIndex = -1;
    private CaretRect? _caret;
    private bool _isRevealed;
    private DateTime _lastCycleAt = DateTime.MinValue;
    private GlassAppearance _appearance = GlassAppearance.OverLight;
    private DateTime _lastProbeAt = DateTime.MinValue;

    /// <summary>Raised when a chip is clicked directly with the mouse (bypassing Tab-cycling).</summary>
    public event EventHandler<Suggestion>? SuggestionClicked;

    public SuggestionBarWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = this;

        SizeChanged += (_, _) => Reposition();

        ApplyAppearance();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GlassWindowBehavior.Apply(this);
    }

    public bool HasSelection => _selectedIndex >= 0;

    /// <summary>
    /// Whether to animate at all. Two independent ways to say no: Windows' own "animation effects" switch,
    /// which the app must respect as an accessibility preference, and the user dragging the speed slider to
    /// its far end, which now means off rather than merely fast.
    /// </summary>
    private bool UseMotion => SystemAppearance.UseMotion && !_motion.IsInstant;

    public void ShowSuggestions(IReadOnlyList<Suggestion> suggestions, CaretRect? caret)
    {
        _caret = caret;

        if (suggestions.Count == 0)
        {
            HideBar();
            return;
        }

        var sameWords = suggestions.Count == _currentSuggestions.Count;
        if (sameWords)
        {
            for (var i = 0; i < suggestions.Count; i++)
            {
                if (!string.Equals(suggestions[i].Word, _currentSuggestions[i].Word, StringComparison.Ordinal))
                {
                    sameWords = false;
                    break;
                }
            }
        }

        _currentSuggestions = suggestions;
        var reappearing = !_isRevealed;

        // Rebuilding the chips on every keystroke would restart the reveal animation and make the strip
        // strobe while typing, so only touch them when the words actually changed.
        if (!sameWords)
        {
            Chips.Clear();
            foreach (var s in suggestions)
                Chips.Add(CreateChip(s.Word));
        }

        // A bar that has just reappeared must start with nothing highlighted, even when it happens to be
        // showing the same words as last time. Carrying a stale selection over would mean the next Space
        // silently replaces the word with a candidate the user never chose.
        if (!sameWords || reappearing)
            ClearSelection();

        Show();

        // Only force a synchronous layout pass when the content actually changed. Doing it on every
        // keystroke made the window re-measure and move constantly, which is felt as stutter while typing.
        if (!sameWords) UpdateLayout();

        Reposition();
        AdaptToBackground(reappearing);
        Reveal();
    }

    /// <summary>
    /// Retints the glass for whatever is behind it. Only sampled when the bar (re)appears or after a pause —
    /// reading pixels off the screen DC is far too costly to do on every keystroke, and the backdrop rarely
    /// changes brightness mid-word anyway.
    /// </summary>
    private void AdaptToBackground(bool reappearing)
    {
        // Pinned to a palette: no screen probe, no hysteresis, no cost. The whole point of choosing light or
        // dark is that the strip stops changing underneath you, so sampling and then ignoring the answer
        // would be both wasteful and a lie about what the setting does.
        if (_settings.AppearanceMode != AppearanceMode.Auto)
        {
            var pinned = _settings.AppearanceMode == AppearanceMode.Dark
                ? GlassAppearance.OverDark
                : GlassAppearance.OverLight;

            if (pinned == _appearance) return;

            _appearance = pinned;
            ApplyPalette();
            return;
        }

        if (!SystemAppearance.UseGlass) return;

        var now = DateTime.UtcNow;
        if (!reappearing && now - _lastProbeAt < TimeSpan.FromMilliseconds(700)) return;
        _lastProbeAt = now;

        var scale = GetDpiScale();
        var luminance = BackgroundProbe.SampleAround(
            (int)Math.Round(Left * scale),
            (int)Math.Round(Top * scale),
            (int)Math.Round(ActualWidth * scale),
            (int)Math.Round(ActualHeight * scale));

        if (luminance is null) return;

        // Hysteresis around the midpoint: without a dead band, a backdrop hovering near the threshold would
        // flip the whole material back and forth while the user types.
        var next = _appearance switch
        {
            GlassAppearance.OverDark when luminance > 0.46 => GlassAppearance.OverLight,
            GlassAppearance.OverLight when luminance < 0.34 => GlassAppearance.OverDark,
            _ => _appearance,
        };

        if (next == _appearance) return;

        _appearance = next;
        ApplyPalette();
    }

    public void HideBar()
    {
        _currentSuggestions = Array.Empty<Suggestion>();
        ClearSelection();

        if (!IsVisible)
        {
            _isRevealed = false;
            return;
        }

        Dismiss();
    }

    private SuggestionChipViewModel CreateChip(string word) => new()
    {
        Word = word,
        Metrics = _metrics,
        HoverBrush = new SolidColorBrush(_brushes.HoverOverlay),
        Foreground = _restingTextBrush,
    };

    /// <summary>Drops the highlight and returns every chip to its resting appearance.</summary>
    private void ClearSelection()
    {
        _selectedIndex = -1;
        _lastCycleAt = DateTime.MinValue;

        foreach (var chip in Chips)
        {
            chip.IsSelected = false;
            chip.Foreground = _restingTextBrush;
        }

        HidePill();
    }

    /// <summary>Moves the highlighted chip forward (Tab) or backward (Shift+Tab), wrapping around.</summary>
    public void CycleSelection(bool forward)
    {
        if (Chips.Count == 0) return;

        if (_selectedIndex >= 0 && _selectedIndex < Chips.Count)
        {
            Chips[_selectedIndex].IsSelected = false;
            Chips[_selectedIndex].Foreground = _restingTextBrush;
        }

        _selectedIndex = _selectedIndex < 0
            ? (forward ? 0 : Chips.Count - 1)
            : ((_selectedIndex + (forward ? 1 : -1)) % Chips.Count + Chips.Count) % Chips.Count;

        Chips[_selectedIndex].IsSelected = true;
        Chips[_selectedIndex].Foreground = _selectedTextBrush;

        // Holding Tab auto-repeats at roughly 30/second. A spring tuned for one deliberate press never
        // reaches its target between repeats, so the lens lags further behind on every tick and ends up
        // looking stuck. Detect the scrub and switch to a spring short enough to keep up.
        var now = DateTime.UtcNow;
        var isRepeat = now - _lastCycleAt < MotionProfile.RepeatThreshold;
        _lastCycleAt = now;

        var motion = isRepeat ? _motion.ForRepeat() : _motion;
        MovePillTo(_selectedIndex, motion);

        // Sample only while the lens is actually moving. Frames sampled after it settles are idle frames,
        // whose intervals are irregular by design and would otherwise be miscounted as dropped frames.
        FrameProbe.Record(isRepeat ? "tab-repeat" : "tab-cycle", TimeSpan.FromSeconds(motion.LensSeconds));
    }

    public Suggestion? GetSelectedSuggestion() =>
        _selectedIndex >= 0 && _selectedIndex < _currentSuggestions.Count
            ? _currentSuggestions[_selectedIndex]
            : null;

    /// <summary>
    /// Rebuilds the material from the current settings and system accessibility state. Called at startup and
    /// whenever the user changes something in the settings window, so changes are visible immediately.
    /// </summary>
    public void ApplyAppearance()
    {
        _theme = ThemeCatalog.Get(_settings.Theme);
        _metrics = GlassMetrics.ForScale(_settings.BarScale, _theme.CornerRadius, _theme.ShowIndicator);
        _motion = MotionProfile.ForSpeed(_settings.MotionSpeed);

        var edge = _metrics.Inset + _metrics.RimThickness;
        // Extra room at the bottom for the position indicator, which sits below the chips.
        ContentLayer.Margin = new Thickness(edge, edge, edge, edge + _metrics.IndicatorReserve);

        Plate.RimThickness = _metrics.RimThickness;
        Plate.CornerRadius = _metrics.PlateRadius;
        Lens.CornerRadius = _metrics.ChipRadius;
        Lens.IndicatorThickness = _metrics.IndicatorThickness;
        Lens.IndicatorWidthFactor = _metrics.IndicatorWidthFactor;
        Lens.IndicatorGap = Math.Max(2, _metrics.IndicatorReserve * 0.45);

        ApplyFixedWidth();
        ApplyPalette();

        // Chip sizing lives on the view models, so re-create them to pick up new metrics.
        if (Chips.Count > 0)
        {
            var words = Chips.Select(c => c.Word).ToList();
            var selected = _selectedIndex;
            Chips.Clear();
            foreach (var word in words)
                Chips.Add(CreateChip(word));

            _selectedIndex = -1;
            HidePill();
            if (selected >= 0 && selected < Chips.Count)
            {
                _selectedIndex = selected;
                Chips[selected].IsSelected = true;
                Chips[selected].Foreground = _selectedTextBrush;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => MovePillTo(selected)));
            }
        }

        UpdateLayout();
        Reposition();
    }

    /// <summary>
    /// Pins the strip to one width, or releases it to size itself to its content.
    ///
    /// <para>The width is set on the root element rather than the window, because the window is
    /// <c>SizeToContent="WidthAndHeight"</c> and takes its size from what it contains. Setting
    /// <see cref="double.NaN"/> is how WPF is told to go back to measuring.</para>
    ///
    /// <para>Content wider than the fixed width is clipped rather than allowed to push the strip out,
    /// because "fixed" has to mean fixed — a width that mostly holds is worse than no promise at all. Long
    /// phrases lose their tail to an ellipsis instead, which is visible and understandable, and the user can
    /// widen the strip or ask for fewer suggestions.</para>
    /// </summary>
    private void ApplyFixedWidth()
    {
        if (!_settings.FixedBarWidth)
        {
            RootHost.Width = double.NaN;
            // Qualified: UseWindowsForms adds an implicit global using that makes the bare name ambiguous.
            ChipList.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            ContentLayer.ClipToBounds = false;
            return;
        }

        // Device-independent units, which is what WPF lays out in — the work area is already in those, so no
        // DPI conversion belongs here.
        RootHost.Width = Math.Round(SystemParameters.WorkArea.Width * _settings.BarWidthFraction);

        // Centred, the way a phone keyboard's row is, rather than left-packed with dead space on the right.
        ChipList.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        ContentLayer.ClipToBounds = true;
    }

    /// <summary>
    /// Swaps just the brushes. Kept separate from <see cref="ApplyAppearance"/> so retinting the glass when
    /// the backdrop's brightness changes doesn't rebuild every chip or force a relayout mid-typing.
    /// </summary>
    private void ApplyPalette()
    {
        _brushes = ThemeBrushes.Build(
            _theme, _appearance, _settings.GlassTint,
            allowTransparency: SystemAppearance.TransparencyEnabled,
            highContrast: SystemAppearance.HighContrast);

        _restingTextBrush = new SolidColorBrush(_brushes.TextColor);
        _selectedTextBrush = new SolidColorBrush(_brushes.SelectedTextColor);
        _restingTextBrush.Freeze();
        _selectedTextBrush.Freeze();

        Plate.Fill = _brushes.Scrim;
        Plate.Rim = _brushes.Hairline;
        Plate.Sheen = _brushes.Sheen;
        Plate.Bezel = _brushes.Bezel;

        Lens.Fill = _brushes.Pill;
        Lens.Rim = _brushes.PillRim;
        Lens.Indicator = _brushes.ShowIndicator ? _brushes.Indicator : null;

        Plate.Effect = _brushes.ShadowOpacity > 0
            ? new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = _brushes.ShadowOpacity,
                BlurRadius = _brushes.ShadowBlur,
                ShadowDepth = _brushes.ShadowDepth,
                Direction = 270,
                RenderingBias = RenderingBias.Performance,
            }
            : null;

        // Deliberately NOT bitmap-cached. GlassPlate reports zero desired size so it can't force the window
        // to stay wide, and a BitmapCache sized from a zero-size element caches nothing — the plate simply
        // stops being drawn and the window backdrop shows through instead. The reason the cache was added
        // (keeping the shadow off the animating lens) is already handled by the plate being a separate,
        // static sibling, so nothing is lost by dropping it.
        Plate.CacheMode = null;

        foreach (var chip in Chips)
            chip.Foreground = chip.IsSelected ? _selectedTextBrush : _restingTextBrush;
    }

    /// <summary>
    /// Monitor DPI scale, or 1.0 when the window has no presentation source yet.
    /// <see cref="VisualTreeHelper.GetDpi"/> throws for a visual that isn't connected to one, and appearance
    /// is applied from the constructor — before the HWND exists — so it has to be asked defensively.
    /// </summary>
    private double GetDpiScale() =>
        PresentationSource.FromVisual(this) is null ? 1.0 : VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SuggestionChipViewModel vm }) return;

        var index = Chips.IndexOf(vm);
        if (index >= 0 && index < _currentSuggestions.Count)
            SuggestionClicked?.Invoke(this, _currentSuggestions[index]);
    }

    // --- Motion -------------------------------------------------------------------------------------
    // Springs rather than bezier curves: the strip should settle like a physical object. Animations
    // deliberately omit a From value so that re-triggering mid-flight continues from wherever the property
    // currently is instead of snapping back to a start point — the interruptibility that makes rapid Tab
    // presses feel continuous rather than jerky.

    private static SpringEase Spring(double response, double damping, double durationSeconds) =>
        new() { Response = response, DampingFraction = damping, DurationSeconds = durationSeconds };

    private void Reveal()
    {
        if (_isRevealed) return;
        _isRevealed = true;

        if (!UseMotion)
        {
            RootHost.BeginAnimation(OpacityProperty, null);
            RootTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RootHost.Opacity = 1;
            RootTranslate.Y = 0;
            RootScale.ScaleX = 1;
            RootScale.ScaleY = 1;
            return;
        }

        // Grow out of whichever screen edge the bar is anchored to, so it reads as emerging from there
        // rather than materialising in mid-air. Following the caret has no edge to grow from, so it pops
        // from its own centre.
        RootHost.RenderTransformOrigin = _settings.BarPosition switch
        {
            BarPosition.TopCenter => new System.Windows.Point(0.5, 0),
            BarPosition.NearCaret => new System.Windows.Point(0.5, 0.5),
            _ => new System.Windows.Point(0.5, 1),
        };

        var seconds = _motion.RevealSeconds;
        var duration = new Duration(TimeSpan.FromSeconds(seconds));

        RootHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, new Duration(TimeSpan.FromSeconds(_motion.FadeInSeconds)))
            { EasingFunction = Spring(_motion.FadeInSeconds, 1.0, _motion.FadeInSeconds) });

        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, duration) { EasingFunction = Spring(_motion.RevealResponse, _motion.RevealDamping, seconds) });

        // The pop: both axes scale up together past 1 and settle back, the gesture a macOS window makes when
        // an application opens. Scaling only one axis — as this used to — is a stretch, which reads as the
        // bar being pulled rather than arriving.
        //
        // The bounce lives on scale alone. Letting position overshoot too turns the arrival into a wobble,
        // and on a bar this wide any vertical overshoot is very visible along its top and bottom edges.
        var bounce = Spring(_motion.RevealResponse, _motion.BounceDamping, seconds);

        // These two state a From, unlike everything else here. The rule elsewhere is to omit it so a
        // re-triggered animation continues from wherever the property currently is — right for the selection
        // lens, which is retargeted mid-flight. An entrance is not that: it is discrete, and it has to start
        // small every time or there is no pop. Assigning the property instead would do nothing, because a
        // still-running dismiss animation holds its value and wins over a local set.
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(_motion.RevealScaleFrom, 1, duration) { EasingFunction = bounce });

        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(_motion.RevealScaleFrom, 1, duration) { EasingFunction = bounce });
    }

    private void Dismiss()
    {
        if (!_isRevealed)
        {
            Hide();
            return;
        }

        _isRevealed = false;

        if (!UseMotion)
        {
            RootHost.BeginAnimation(OpacityProperty, null);
            RootHost.Opacity = 0;
            Hide();
            return;
        }

        var duration = new Duration(TimeSpan.FromSeconds(_motion.DismissSeconds));
        var fade = new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

        // Hide the window only once it has finished fading, otherwise it blinks out instantly.
        fade.Completed += (_, _) =>
        {
            if (!_isRevealed) Hide();
        };

        RootHost.BeginAnimation(OpacityProperty, fade);
        RootTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

        // Shrink away as it goes, mirroring the way it grew. Closing that simply fades leaves the bar
        // hanging at full size while it disappears, which reads as it being switched off rather than
        // leaving. No bounce here — an overshoot on the way out just delays getting out of the way.
        var shrink = new DoubleAnimation(_motion.DismissScaleTo, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };

        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private void MovePillTo(int index) => MovePillTo(index, _motion);

    private void MovePillTo(int index, MotionProfile motion)
    {
        if (ChipList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
        {
            // Containers are generated lazily; retry once layout has produced them.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => MovePillTo(index, motion)));
            return;
        }

        var origin = container.TranslatePoint(new System.Windows.Point(0, 0), Lens);
        var targetWidth = container.ActualWidth;
        var targetHeight = container.ActualHeight;

        Lens.LensY = origin.Y;
        Lens.LensHeight = targetHeight;

        var firstShow = Lens.Opacity < 0.01;
        if (firstShow || !UseMotion)
        {
            // Nothing to glide from on the first highlight — appear in place rather than sweeping in from 0,0.
            Lens.BeginAnimation(SelectionLens.LensXProperty, null);
            Lens.BeginAnimation(SelectionLens.LensWidthProperty, null);
            Lens.LensX = origin.X;
            Lens.LensWidth = targetWidth;

            if (UseMotion)
            {
                Lens.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, new Duration(TimeSpan.FromSeconds(motion.FadeInSeconds)))
                    { EasingFunction = Spring(motion.FadeInSeconds, 1.0, motion.FadeInSeconds) });
            }
            else
            {
                Lens.BeginAnimation(OpacityProperty, null);
                Lens.Opacity = 1;
            }
            return;
        }

        // Position and width are sprung with slightly different damping so the lens stretches a touch as it
        // travels and settles a moment after it arrives — the "fluid" half of Liquid Glass. Apple's guidance
        // is explicit that co-animated properties need not share identical timing.
        var duration = new Duration(TimeSpan.FromSeconds(motion.LensSeconds));

        Lens.BeginAnimation(SelectionLens.LensXProperty,
            new DoubleAnimation(origin.X, duration)
            { EasingFunction = Spring(motion.LensResponse, motion.LensDamping - 0.06, motion.LensSeconds) });

        Lens.BeginAnimation(SelectionLens.LensWidthProperty,
            new DoubleAnimation(targetWidth, duration)
            { EasingFunction = Spring(motion.LensResponse * 1.1, motion.LensDamping, motion.LensSeconds) });
    }

    private void HidePill()
    {
        Lens.BeginAnimation(SelectionLens.LensXProperty, null);
        Lens.BeginAnimation(SelectionLens.LensWidthProperty, null);
        Lens.BeginAnimation(OpacityProperty, null);
        Lens.Opacity = 0;
    }

    // --- Placement ----------------------------------------------------------------------------------

    private void Reposition()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var workArea = SystemParameters.WorkArea;
        var gap = _metrics.EdgeGap;
        double left, top;

        switch (_settings.BarPosition)
        {
            case BarPosition.TopCenter:
                left = workArea.Left + (workArea.Width - ActualWidth) / 2;
                top = workArea.Top + gap;
                break;

            case BarPosition.NearCaret when _caret is { } caret:
                (left, top) = ComputeNearCaret(caret, workArea);
                break;

            default:
                left = workArea.Left + (workArea.Width - ActualWidth) / 2;
                top = workArea.Bottom - ActualHeight - gap;
                break;
        }

        // Moving a top-level window is a compositor operation, not a cheap property set. Re-applying the
        // same position on every keystroke produced visible jitter, so only move when it actually changed.
        if (Math.Abs(Left - left) > 0.5) Left = left;
        if (Math.Abs(Top - top) > 0.5) Top = top;
    }

    private (double Left, double Top) ComputeNearCaret(CaretRect caret, Rect workArea)
    {
        // Caret coordinates are physical pixels; WPF positions windows in device-independent units.
        var scale = GetDpiScale();
        var caretLeft = caret.Left / scale;
        var caretTop = caret.Top / scale;
        var caretBottom = caret.Bottom / scale;
        var caretGap = Math.Max(6, _metrics.EdgeGap * 0.6);

        var left = caretLeft - ActualWidth / 2;
        var top = caretBottom + caretGap;

        // Flip above the caret rather than hang off the bottom of the screen.
        if (top + ActualHeight > workArea.Bottom)
            top = caretTop - ActualHeight - caretGap;

        return (
            Math.Clamp(left, workArea.Left + 4, Math.Max(workArea.Left + 4, workArea.Right - ActualWidth - 4)),
            Math.Clamp(top, workArea.Top + 4, Math.Max(workArea.Top + 4, workArea.Bottom - ActualHeight - 4)));
    }
}
