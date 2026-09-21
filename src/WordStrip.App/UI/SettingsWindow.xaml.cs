using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WordStrip.App.UI.Theming;
using WordStrip.Core.Settings;
using Brushes = System.Windows.Media.Brushes;
using Colors = System.Windows.Media.Colors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace WordStrip.App.UI;

public partial class SettingsWindow : Window
{
    private static readonly string[] PreviewWords = { "is", "issues", "issue", "island", "islands", "isolated", "issued" };

    private readonly AppSettings _settings;

    public SettingsWindow(SettingsViewModel viewModel, AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        // Cap the height to the desktop so the window can never open taller than the screen; each page
        // scrolls from there.
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60);

        ApplyPalette();
        BuildShortcutList();
        ShowSection(0);

        viewModel.PropertyChanged += (_, _) => RebuildPreview();
        RebuildPreview();

        // TipRegistered reads the registry directly rather than a cached field, so the only thing needed to
        // keep the card honest is asking WPF to re-read it — which happens whenever the window regains
        // focus, covering "registered from a different session" and "the elevated regsvr32 child process
        // just finished" alike.
        Activated += (_, _) => viewModel.RefreshTipStatus();
    }

    /// <summary>
    /// Draws the preview strips from the same theme tokens, metrics and renderers the real bar uses, so what
    /// is shown here cannot drift from what appears while typing.
    ///
    /// <para>Both the light and dark variants are shown at once, because this strip floats over whatever
    /// application the user is in and a theme that only works over one of them isn't finished. Seeing both
    /// side by side is also the fastest way to judge whether a theme is actually legible.</para>
    /// </summary>
    private void RebuildPreview()
    {
        var theme = ThemeCatalog.Get(_settings.Theme);

        PreviewLight.Content = BuildStrip(theme, GlassAppearance.OverLight);
        PreviewDark.Content = BuildStrip(theme, GlassAppearance.OverDark);
    }

    private UIElement BuildStrip(ThemeDefinition theme, GlassAppearance appearance)
    {
        // The preview's own cards are the backdrop, so their luminance is known exactly: the separation
        // floor is applied here too, and the preview shows what the bar will actually do over a white page
        // and over a dark editor rather than the theme's unadjusted colours.
        var brushes = ThemeBrushes.Build(
            theme, appearance, _settings.GlassTint,
            allowTransparency: SystemAppearance.TransparencyEnabled,
            highContrast: SystemAppearance.HighContrast,
            backdropLuminance: appearance == GlassAppearance.OverDark ? 0.10 : 1.0);

        var metrics = GlassMetrics.ForScale(_settings.BarScale, theme.CornerRadius, theme.ShowIndicator);

        var count = Math.Clamp(_settings.SuggestionCount, AppSettings.MinSuggestionCount, AppSettings.MaxSuggestionCount);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        for (var i = 0; i < count && i < PreviewWords.Length; i++)
        {
            var selected = i == 0;
            chips.Children.Add(new Border
            {
                Padding = new Thickness(metrics.ChipPaddingX, metrics.ChipPaddingY, metrics.ChipPaddingX, metrics.ChipPaddingY),
                Margin = new Thickness(metrics.ChipMarginX, 0, metrics.ChipMarginX, 0),
                MinHeight = metrics.ChipMinHeight,
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = PreviewWords[i],
                    FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                    FontSize = metrics.FontSize,
                    FontWeight = selected ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground = new SolidColorBrush(selected ? brushes.SelectedTextColor : brushes.TextColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        var plate = new GlassPlate
        {
            Fill = brushes.Scrim,
            Rim = brushes.Hairline,
            Sheen = brushes.Sheen,
            Bezel = brushes.Bezel,
            RimThickness = metrics.RimThickness,
            CornerRadius = metrics.PlateRadius,
        };

        // The lens is positioned once layout has measured the first chip, mirroring how the bar places it.
        var lens = new SelectionLens
        {
            Fill = brushes.Pill,
            Rim = brushes.PillRim,
            Indicator = brushes.ShowIndicator ? brushes.Indicator : null,
            CornerRadius = metrics.ChipRadius,
            IndicatorThickness = metrics.IndicatorThickness,
            IndicatorWidthFactor = metrics.IndicatorWidthFactor,
            IndicatorGap = Math.Max(2, metrics.IndicatorReserve * 0.45),
            Opacity = 1,
        };

        var edge = metrics.Inset + metrics.RimThickness;
        var content = new Grid { Margin = new Thickness(edge, edge, edge, edge + metrics.IndicatorReserve) };
        content.Children.Add(chips);

        var layers = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = brushes.ShadowOpacity > 0
                ? new DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = brushes.ShadowOpacity,
                    BlurRadius = brushes.ShadowBlur,
                    ShadowDepth = brushes.ShadowDepth,
                    Direction = 270,
                    RenderingBias = RenderingBias.Performance,
                }
                : null,
        };
        layers.Children.Add(plate);
        layers.Children.Add(lens);
        layers.Children.Add(content);

        layers.Loaded += (_, _) => PlaceLens(lens, chips);
        layers.SizeChanged += (_, _) => PlaceLens(lens, chips);

        return layers;
    }

    private static void PlaceLens(SelectionLens lens, Panel chips)
    {
        if (chips.Children.Count == 0 || chips.Children[0] is not FrameworkElement first) return;
        if (first.ActualWidth <= 0) return;

        var origin = first.TranslatePoint(new System.Windows.Point(0, 0), lens);
        lens.LensX = origin.X;
        lens.LensY = origin.Y;
        lens.LensWidth = first.ActualWidth;
        lens.LensHeight = first.ActualHeight;
    }

    // --- Chrome and navigation ----------------------------------------------------------------------

    /// <summary>
    /// Follows the system's app mode, because this is an ordinary application window and every other one on
    /// the desktop does. Only the palette dictionary is swapped: each style reads its colours through
    /// DynamicResource, so nothing else has to know which one is loaded. The bar is deliberately unaffected —
    /// it follows what is behind it, which is a different question with a different answer.
    /// </summary>
    private void ApplyPalette()
    {
        if (!SystemAppearance.AppsUseDarkTheme) return;

        var dark = new ResourceDictionary { Source = new Uri("UI/Design/Palette.Dark.xaml", UriKind.Relative) };
        var merged = Resources.MergedDictionaries;

        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source?.OriginalString.EndsWith("Palette.Light.xaml", StringComparison.Ordinal) != true) continue;
            merged[i] = dark;
            break;
        }

        // The title bar is drawn by Windows, not by WPF, so it has to be told separately or a dark window
        // keeps a white caption.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var on = 1;
            Interop.DwmNativeMethods.DwmSetWindowAttribute(
                hwnd, Interop.DwmNativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        };
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedIndex: >= 0 } list) ShowSection(list.SelectedIndex);
    }

    /// <summary>
    /// One page visible at a time. Kept as plain visibility rather than a tab control so that every page's
    /// bindings stay live: the preview on the Appearance page is driven by the view model, and a page that
    /// had been unloaded would rebuild itself from scratch each time it was selected.
    /// </summary>
    private void ShowSection(int index)
    {
        // The rail's SelectedIndex is coerced as its items are parsed, so its SelectionChanged fires from
        // inside InitializeComponent - before the pages below it exist as fields. The constructor calls this
        // again once the window is built.
        if (PageAppearance is null) return;

        var pages = new[] { PageAppearance, PageSuggestions, PageWords, PageLearning, PageModel, PageIntegrations, PageGeneral };

        for (var i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        if (index >= 0 && index < pages.Length) pages[index].ScrollToTop();
    }

    /// <summary>The keys, as keys. A table of sentences describing keystrokes is harder to scan than the keycaps.</summary>
    private void BuildShortcutList()
    {
        (string Keys, string Does)[] shortcuts =
        {
            ("Space", "Finishes the word when WordStrip is sure"),
            ("Tab", "Takes the first suggestion"),
            ("Tab Tab", "Moves to the next suggestion"),
            ("Shift+Tab", "Moves back one"),
            ("Esc", "Puts the bar away"),
        };

        foreach (var (keys, does) in shortcuts)
        {
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Width = 140 };
            foreach (var key in keys.Split(' '))
            {
                caps.Children.Add(new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("Ws.Control"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("Ws.Control.Border"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 2, 8, 3),
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock
                    {
                        Text = key,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (System.Windows.Media.Brush)FindResource("Ws.Text"),
                    },
                });
            }

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(caps, Dock.Left);
            row.Children.Add(caps);
            row.Children.Add(new TextBlock
            {
                Text = does,
                FontSize = 13,
                Foreground = (System.Windows.Media.Brush)FindResource("Ws.Text.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });

            ShortcutList.Children.Add(row);
        }
    }

    // --- Personal vocabulary and learning -----------------------------------------------------------

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void OnAddPersonalWordClick(object sender, RoutedEventArgs e) => AddPersonalWord();

    private void OnNewWordKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter adds, because typing a word and reaching for the mouse to confirm it is the wrong shape for
        // a box someone will use several times in a row.
        if (e.Key != System.Windows.Input.Key.Enter) return;

        AddPersonalWord();
        e.Handled = true;
    }

    private void AddPersonalWord()
    {
        if (!ViewModel.AddPersonalWord()) return;
        NewWordBox.Focus();
    }

    private void OnRemovePersonalWordClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string word })
            ViewModel.RemovePersonalWord(word);
    }

    private void OnImportWordsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import personal words",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return;

        var added = ViewModel.ImportPersonalWords(dialog.FileName);
        System.Windows.MessageBox.Show(this,
            added == 0 ? "No new words were found in that file." : $"Added {added} new word{(added == 1 ? "" : "s")}.",
            "WordStrip", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportWordsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export personal words",
            Filter = "Text files (*.txt)|*.txt",
            FileName = "wordstrip-personal-words.txt",
        };

        if (dialog.ShowDialog(this) != true) return;

        ViewModel.ExportPersonalWords(dialog.FileName);
    }

    private async void OnDownloadModelClick(object sender, RoutedEventArgs e)
    {
        // Confirmed with the size stated, because a quarter of a gigabyte on a metered or slow connection is
        // not something to start on a single stray click.
        var model = ViewModel.NeuralModel;
        var confirmed = System.Windows.MessageBox.Show(
            this,
            $"Download {model.Name} ({model.DownloadMegabytes} MB)?\n\n" +
            $"Licence: {model.License}\nFrom: {model.SourceUrl}\n\n" +
            "This is the only time WordStrip connects to the internet. Nothing about you or what you type " +
            "is sent — it is a plain download of a public file.",
            "Download language model",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirmed != MessageBoxResult.OK) return;

        await ViewModel.DownloadNeuralModelAsync();
    }

    private void OnDeleteModelClick(object sender, RoutedEventArgs e)
    {
        var confirmed = System.Windows.MessageBox.Show(
            this,
            "Delete the downloaded language model?\n\nSuggestions will go back to how they work without it. " +
            "You can download it again at any time.",
            "Delete model",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes) return;

        ViewModel.DeleteNeuralModel();
    }

    private void OnEnableTipClick(object sender, RoutedEventArgs e)
    {
        // Confirmed because a UAC prompt is about to appear and it should not come as a surprise, and
        // because it is worth being explicit that the tray app itself is not what is being elevated.
        var confirmed = System.Windows.MessageBox.Show(
            this,
            "Enable suggestions in Chrome, Edge and Word?\n\n" +
            "Windows will ask you to approve a permission request for a single small helper step " +
            "(regsvr32) that registers the component — WordStrip itself keeps running exactly as it does " +
            "now, and never runs elevated.\n\n" +
            "Autocorrect and personal learning do not use this path yet; only suggestions do. " +
            "Notepad and other classic Windows text boxes are unaffected either way.",
            "Enable browser and Office support",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (confirmed != MessageBoxResult.OK) return;

        ViewModel.RegisterTip();
    }

    private void OnDisableTipClick(object sender, RoutedEventArgs e)
    {
        ViewModel.UnregisterTip();
    }

    private void OnSwitchTipOnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddTipToKeyboards();
    }

    private void OnClearLearnedDataClick(object sender, RoutedEventArgs e)
    {
        // Confirmed because it cannot be undone, and because the user may not realise how much has built up.
        var confirmed = System.Windows.MessageBox.Show(
            this,
            "Delete everything WordStrip has learned from your typing?\n\n" +
            "This cannot be undone. Your personal word list is not affected.",
            "Clear learned data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes) return;

        ViewModel.ClearLearnedData();
    }
}
