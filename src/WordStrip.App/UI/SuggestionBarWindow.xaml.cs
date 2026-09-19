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
using WordStrip.Core.Suggestions;
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
    private System.Windows.Threading.DispatcherTimer? _probePauseTimer;
    private bool _probeInFlight;
    private SlotPanel? _slots;
    private double _heldWidth;
    private System.Windows.Threading.DispatcherTimer? _relaxTimer;

    /// <summary>Raised when a chip is clicked directly with the mouse (bypassing Tab-cycling).</summary>
    public event EventHandler<Suggestion>? SuggestionClicked;

    public SuggestionBarWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = this;

        SizeChanged += (_, _) =>
        {
            FrameProbe.CountResize();
            Reposition();
        };

        // ApplyAppearance runs below, before the items control has generated its panel, so the slot settings
        // it tries to push have nowhere to land. Re-applying once the list is loaded is what actually
        // configures the panel; without this the strip silently runs in its stacked fallback and no dividers
        // are ever drawn.
        ChipList.Loaded += (_, _) => ApplySlotLayout();

        ApplyAppearance();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        GlassWindowBehavior.Apply(this);
    }

    /// <summary>How many slots the bar shows, from its width and font. The controller trims to this.</summary>
    public int VisibleSlotCount => EffectiveSlotCount();

    /// <summary>Raised when appearance changes may have changed <see cref="VisibleSlotCount"/>.</summary>
    public event EventHandler? SlotCountChanged;

    private SuggestionUpdate _lastUpdate = SuggestionUpdate.Empty;

    /// <summary>
    /// Whether to animate at all. Two independent ways to say no: Windows' own "animation effects" switch,
    /// which the app must respect as an accessibility preference, and the user dragging the speed slider to
    /// its far end, which now means off rather than merely fast.
    /// </summary>
    private bool UseMotion => SystemAppearance.UseMotion && !_motion.IsInstant;

    /// <summary>
    /// Renders one update from the controller.
    ///
    /// <para><b>A model update changes words, not the bar.</b> The chips are a fixed pool, one per slot, and
    /// an update rewrites their text in place: no chips are created or destroyed, no layout is forced, the
    /// window does not move unless its target position actually changed, and nothing animates. The lens —
    /// the only prominent motion the bar has — moves solely when the update carries a selection, which only
    /// the user pressing Tab can produce.</para>
    /// </summary>
    public void ShowSuggestions(SuggestionUpdate update)
    {
        _lastUpdate = update;
        _caret = update.Caret;

        if (update.Suggestions.Count == 0)
        {
            HideBar();
            return;
        }

        FrameProbe.CountRender();
        if (update.SelectedIndex < 0) FrameProbe.Record("typing", TimeSpan.FromSeconds(2));
        var reappearing = !_isRevealed;
        var poolRebuilt = EnsureChipPool();

        var suggestions = update.Suggestions.Count > Chips.Count
            ? update.Suggestions.Take(Chips.Count).ToList()
            : update.Suggestions;
        _currentSuggestions = suggestions;

        for (var i = 0; i < Chips.Count; i++)
        {
            var chip = Chips[i];
            Suggestion? candidate = i < suggestions.Count ? suggestions[i] : null;

            chip.Word = candidate?.Word ?? string.Empty;
            chip.IsEmoji = candidate?.IsEmoji ?? false;
            chip.IsPrimary = i == 0;
            chip.IsArmed = i == 0 && update.FirstIsArmed;
        }

        if (FindSlotPanel() is { } slots) slots.FilledCount = suggestions.Count;

        Show();

        // Geometry only needs settling when it can actually have changed: the bar appearing, the slot pool
        // being rebuilt, or the bar sizing itself to its words. In the fixed layout a word change cannot
        // alter the window's size, so forcing a layout pass here would be work spent proving nothing moved.
        if (reappearing || poolRebuilt || !_settings.FixedBarWidth)
        {
            UpdateLayout();
            if (!_settings.FixedBarWidth && UpdateDynamicWidth()) UpdateLayout();
        }

        // The highlight means "this is what your key will take". While cycling that is the candidate Tab just
        // inserted; otherwise it marks the first candidate when Space will commit it - the same selection
        // surface in every theme, because a weight change alone proved too easy to miss.
        ApplySelection(update.SelectedIndex >= 0 ? update.SelectedIndex : update.FirstIsArmed ? 0 : -1);
        Reposition();
        AdaptToBackground(reappearing);
        Reveal();
    }

    /// <summary>
    /// Retints the glass for whatever is behind it — but never while the user is typing.
    ///
    /// <para><b>Measured, not assumed.</b> Reading pixels off the screen DC takes about 145 ms on a typical
    /// machine, and it used to run on the UI thread every 700 ms of typing. With WORDSTRIP_FRAMELOG on, that
    /// was the whole of the bar's jank: every other render was well under a millisecond, and these produced
    /// a frame gap of 150-200 ms three times a second. So the probe now runs only when the bar appears and
    /// once typing has paused, and the pixel read itself happens on a worker thread; the UI thread only
    /// applies the answer. The backdrop rarely changes brightness mid-word, and when it does the palette
    /// catches up a moment after the user stops.</para>
    /// </summary>
    private void AdaptToBackground(bool reappearing)
    {
        // Pinned to a palette: no screen probe, no hysteresis, no cost. The whole point of choosing light or
        // dark is that the strip stops changing underneath you, so sampling and then ignoring the answer
        // would be both wasteful and a lie about what the setting does.
        if (_settings.AppearanceMode != AppearanceMode.Auto)
        {
            _probePauseTimer?.Stop();
            var pinned = _settings.AppearanceMode == AppearanceMode.Dark
                ? GlassAppearance.OverDark
                : GlassAppearance.OverLight;

            if (pinned == _appearance) return;

            _appearance = pinned;
            ApplyPalette();
            return;
        }

        if (!SystemAppearance.UseGlass) return;

        if (reappearing)
        {
            StartProbe();
            return;
        }

        // Still typing: push the probe back until the updates stop.
        _probePauseTimer ??= new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(1200),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) =>
            {
                _probePauseTimer!.Stop();
                StartProbe();
            },
            Dispatcher);

        _probePauseTimer.Stop();
        _probePauseTimer.Start();
    }

    /// <summary>Samples the backdrop on a worker thread and applies the result back here. One at a time.</summary>
    private void StartProbe()
    {
        if (_probeInFlight || !IsVisible) return;

        var scale = GetDpiScale();
        var left = (int)Math.Round(Left * scale);
        var top = (int)Math.Round(Top * scale);
        var width = (int)Math.Round(ActualWidth * scale);
        var height = (int)Math.Round(ActualHeight * scale);

        _probeInFlight = true;
        System.Threading.Tasks.Task.Run(() => BackgroundProbe.SampleAround(left, top, width, height))
            .ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
            {
                _probeInFlight = false;
                if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) ApplyLuminance(task.Result);
            })));
    }

    private void ApplyLuminance(double? luminance)
    {
        if (luminance is null || !_isRevealed || _settings.AppearanceMode != AppearanceMode.Auto) return;

        // Hysteresis around the midpoint: without a dead band, a backdrop hovering near the threshold would
        // flip the whole material back and forth.
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

    /// <summary>
    /// Renders the bar once, invisibly, so the first real appearance is not also the first render.
    ///
    /// <para>Measured: the first time the bar showed, it blocked the UI thread for about 750 ms (JIT and
    /// first-use setup of the glass, text and layout paths). The keyboard hook runs on that same thread, and
    /// Windows skips a low-level hook that does not answer in time — so keys typed during that stall never
    /// reached WordStrip. The first Space of a session went missing, "i am" was tracked as "iam", and
    /// everything that followed was judged against the wrong word. Paying the cost at startup, before the hook
    /// is installed, moves it to where no keystroke can be lost.</para>
    /// </summary>
    public void WarmUp()
    {
        Opacity = 0;
        ShowSuggestions(new SuggestionUpdate(
            new[] { new Suggestion("warm", 1, 0), new Suggestion("up", 1, 0) }, null, SelectedIndex: 0));
        UpdateLayout();
        HideBar();

        Hide();
        _isRevealed = false;
        Opacity = 1;
    }

    public void HideBar()
    {
        _currentSuggestions = Array.Empty<Suggestion>();
        ClearSelection(fade: false);

        // A bar that has gone away has no width worth defending; the next one should open at the size its own
        // words ask for rather than inheriting the last sentence's.
        ReleaseDynamicWidth();

        if (!IsVisible)
        {
            _isRevealed = false;
            return;
        }

        Dismiss();
    }

    private SuggestionChipViewModel CreateChip() => new()
    {
        Metrics = _metrics,
        HoverBrush = new SolidColorBrush(_brushes.HoverOverlay),
        Foreground = _restingTextBrush,
        CollapseWhenEmpty = !_settings.FixedBarWidth,
    };

    /// <summary>
    /// Keeps exactly one chip per slot. Returns true when the pool had to be rebuilt, which only happens when
    /// the slot count or the metrics change — never because of what is being typed.
    /// </summary>
    private bool EnsureChipPool()
    {
        var wanted = EffectiveSlotCount();
        if (Chips.Count == wanted) return false;

        ClearSelection(fade: false);
        Chips.Clear();
        for (var i = 0; i < wanted; i++) Chips.Add(CreateChip());

        if (FindSlotPanel() is { } slots) slots.SlotCount = wanted;
        return true;
    }

    /// <summary>Drops the highlight and returns every chip to its resting appearance.</summary>
    private void ClearSelection(bool fade = true)
    {
        if (_selectedIndex >= 0 && _selectedIndex < Chips.Count)
        {
            Chips[_selectedIndex].IsSelected = false;
            Chips[_selectedIndex].Foreground = _restingTextBrush;
        }

        _selectedIndex = -1;
        _lastCycleAt = DateTime.MinValue;
        HidePill(fade);
    }

    /// <summary>
    /// Puts the selection where the controller says it is. Passive updates (index -1) clear it; an active one
    /// moves the lens, and consecutive Tabs inside the repeat threshold switch to a spring short enough to
    /// keep up, so rapid Tab presses read as one continuous glide rather than a lens that lags behind.
    /// </summary>
    private void ApplySelection(int index)
    {
        if (index < 0 || index >= _currentSuggestions.Count || index >= Chips.Count)
        {
            if (_selectedIndex >= 0) ClearSelection();
            return;
        }

        if (index == _selectedIndex) return;

        var wasActive = _selectedIndex >= 0;
        if (wasActive && _selectedIndex < Chips.Count)
        {
            Chips[_selectedIndex].IsSelected = false;
            Chips[_selectedIndex].Foreground = _restingTextBrush;
        }

        _selectedIndex = index;
        Chips[index].IsSelected = true;
        Chips[index].Foreground = _selectedTextBrush;

        var now = DateTime.UtcNow;
        var isRepeat = wasActive && now - _lastCycleAt < MotionProfile.RepeatThreshold;
        _lastCycleAt = now;

        var motion = isRepeat ? _motion.ForRepeat() : _motion;
        MovePillTo(index, motion);

        // Sample only while the lens is actually moving; frames after it settles are idle frames.
        FrameProbe.Record(isRepeat ? "tab-repeat" : "tab-cycle", TimeSpan.FromSeconds(motion.LensSeconds));
    }

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
        ApplySlotLayout();

        // Chip sizing lives on the view models, so the pool is rebuilt to pick up new metrics and the last
        // update rendered into it again.
        Chips.Clear();
        if (_lastUpdate.Suggestions.Count > 0 && IsVisible) ShowSuggestions(_lastUpdate);

        SlotCountChanged?.Invoke(this, EventArgs.Empty);

        UpdateLayout();
        Reposition();
    }

    /// <summary>
    /// Gives the strip a compact, constant-width column per slot, or releases it to size itself to its
    /// content with no per-slot structure at all.
    ///
    /// <para><b>The bar width setting is a ceiling, not a literal width.</b> An earlier version of this set
    /// <c>RootHost.Width</c> to that pixel value directly, which forced every slot to stretch out and fill it
    /// — three short suggestions ended up exactly as wide as seven, because the columns had nothing to do but
    /// expand into the leftover room. Setting <see cref="FrameworkElement.MaxWidth"/> instead leaves
    /// <c>Width</c> on <see cref="double.NaN"/> (auto), so the strip sizes to however many compact columns
    /// <see cref="SlotPanel"/> actually needs — the setting only stops it from growing past that ceiling when
    /// a word or two genuinely needs the room.</para>
    ///
    /// <para>The width is set on the root element rather than the window, because the window is
    /// <c>SizeToContent="WidthAndHeight"</c> and takes its size from what it contains.</para>
    /// </summary>
    private void ApplyFixedWidth()
    {
        if (!_settings.FixedBarWidth)
        {
            RootHost.Width = double.NaN;
            RootHost.MaxWidth = double.PositiveInfinity;
            // Centred, so that while the width is being held above what the words need (see
            // UpdateDynamicWidth) the spare room is shared between both ends rather than left hanging off
            // the right.
            // Qualified: UseWindowsForms adds an implicit global using that makes the bare name ambiguous.
            ChipList.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            ContentLayer.ClipToBounds = false;
            return;
        }

        ReleaseDynamicWidth();

        RootHost.Width = double.NaN;

        // Device-independent units, which is what WPF lays out in — the work area is already in those, so no
        // DPI conversion belongs here.
        RootHost.MaxWidth = Math.Round(SystemParameters.WorkArea.Width * _settings.BarWidthFraction);

        // Stretch vs Center makes no visible difference once RootHost sizes to its own content rather than to
        // a fixed pixel width — there is no leftover space for either to distribute. Left as Stretch so
        // SlotPanel still receives the ceiling as its available width to grow into, rather than infinity.
        ChipList.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        ContentLayer.ClipToBounds = true;
    }

    /// <summary>
    /// The width one ordinary-weight slot asks for before any borrowing or growth: roughly seven characters
    /// plus the chip's own padding, which is enough for "through" or "because" to land without shrinking,
    /// while staying far short of stretching to fill whatever the bar's width ceiling happens to be.
    ///
    /// <para>The single source both <see cref="EffectiveSlotCount"/> and <see cref="ApplySlotLayout"/> read
    /// from, so how many columns are allowed to exist and how wide each one actually renders can never drift
    /// apart from each other.</para>
    /// </summary>
    private double PreferredSlotWidth() =>
        (_metrics.FontSize * 4.2) + (_metrics.ChipPaddingX * 2) + (_metrics.ChipMarginX * 2);

    /// <summary>
    /// How many slots the strip can carry: what the user asked for, capped by how many can still be read.
    ///
    /// <para>Seven columns across a narrow strip would leave each one too thin to show a whole short word,
    /// so every suggestion would arrive pre-shortened and the strip would say nothing. The cap depends only
    /// on the width setting and the font, never on the words themselves — which is what keeps the column
    /// count from changing while someone is typing.</para>
    /// </summary>
    private int EffectiveSlotCount()
    {
        var requested = Math.Clamp(_settings.SuggestionCount,
            AppSettings.MinSuggestionCount, AppSettings.MaxSuggestionCount);

        if (!_settings.FixedBarWidth) return requested;

        var usable = Math.Round(SystemParameters.WorkArea.Width * _settings.BarWidthFraction)
                     - ((_metrics.Inset + _metrics.RimThickness) * 2);

        var fits = (int)Math.Floor(usable / Math.Max(1, PreferredSlotWidth()));
        return Math.Clamp(Math.Min(requested, fits), 1, requested);
    }

    /// <summary>
    /// Holds the width the bar reached until typing pauses, for the mode where the bar sizes itself to its
    /// words.
    ///
    /// <para><b>Why hold it.</b> Sized purely to its content, the bar changes width on most keystrokes, and
    /// because it is centred both edges move in opposite directions each time. At typing speed that reads as
    /// the strip flickering rather than as it responding, which is the complaint that prompted the fixed-width
    /// mode in the first place. Growing is different from shrinking: a longer suggestion has to be readable on
    /// the keystroke that produced it, whereas nothing is lost by staying wide a moment longer than needed. So
    /// the bar grows immediately and only gives width back once the words have stopped changing.</para>
    ///
    /// <para>Returns whether the held width changed, so the caller knows whether the layout it has already
    /// computed is still good.</para>
    /// </summary>
    private bool UpdateDynamicWidth()
    {
        if (_settings.FixedBarWidth) return false;
        if (FindSlotPanel() is not { } slots) return false;

        var natural = slots.DesiredSize.Width + ContentLayer.Margin.Left + ContentLayer.Margin.Right;

        if (natural >= _heldWidth - 0.5)
        {
            _relaxTimer?.Stop();
            if (Math.Abs(natural - _heldWidth) < 0.5) return false;

            _heldWidth = natural;
            RootHost.MinWidth = natural;
            return true;
        }

        // Restarted rather than left running, so the countdown measures the time since the last change —
        // continuous typing never reaches it, and a pause reaches it exactly once.
        _relaxTimer ??= CreateRelaxTimer();
        _relaxTimer.Stop();
        _relaxTimer.Start();
        return false;
    }

    private System.Windows.Threading.DispatcherTimer CreateRelaxTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            // Long enough that it never fires between keystrokes at speed, short enough that the bar has
            // settled before anyone looks back at it.
            Interval = TimeSpan.FromMilliseconds(650),
        };

        timer.Tick += (_, _) =>
        {
            ReleaseDynamicWidth();
            UpdateLayout();
            Reposition();
        };

        return timer;
    }

    private void ReleaseDynamicWidth()
    {
        _relaxTimer?.Stop();
        _heldWidth = 0;
        RootHost.MinWidth = 0;
    }

    /// <summary>Pushes the current metrics, theme and mode into the slot panel once it exists.</summary>
    private void ApplySlotLayout()
    {
        if (FindSlotPanel() is not { } slots) return;

        slots.UseSlots = _settings.FixedBarWidth;
        slots.SlotCount = EffectiveSlotCount();
        slots.PreferredSlotWidth = PreferredSlotWidth();
        slots.DividerBrush = _brushes.Divider;
        slots.DividerThickness = Math.Max(1, _metrics.RimThickness);
    }

    /// <summary>
    /// The panel lives inside an <c>ItemsPanelTemplate</c>, so it has its own name scope and cannot be
    /// reached as a field. It only exists once the items control has generated its layout, which is why the
    /// lookup is cached rather than resolved once at construction.
    /// </summary>
    private SlotPanel? FindSlotPanel() => _slots ??= FindDescendant<SlotPanel>(ChipList);

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;

            if (FindDescendant<T>(child) is { } nested) return nested;
        }

        return null;
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
        if (vm.Word.Length > 0 && index >= 0 && index < _currentSuggestions.Count)
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

        // In slot mode the container is the whole column, which is wider than the chip sitting centred
        // inside it. Tracing the button keeps the pill the size of the word, the way every theme was drawn,
        // rather than inflating it into a full-column block.
        var target = FindDescendant<System.Windows.Controls.Button>(container) ?? container;

        var origin = target.TranslatePoint(new System.Windows.Point(0, 0), Lens);
        var targetWidth = target.ActualWidth;
        var targetHeight = target.ActualHeight;

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

    /// <summary>
    /// Takes the lens away. Leaving the active state fades it briefly rather than blinking it out, since that
    /// follows the user's own action; anything structural hides it at once.
    /// </summary>
    private void HidePill(bool fade = false)
    {
        if (fade && UseMotion && Lens.Opacity > 0.01)
        {
            Lens.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(Math.Min(0.14, _motion.FadeInSeconds)))));
            return;
        }

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
        var moved = false;
        if (Math.Abs(Left - left) > 0.5) { Left = left; moved = true; }
        if (Math.Abs(Top - top) > 0.5) { Top = top; moved = true; }
        if (moved) FrameProbe.CountMove();
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
