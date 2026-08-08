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

        // Cap the height to the desktop so the window can never grow past the bottom of the screen; the
        // scroll viewer takes over from there.
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60);

        viewModel.PropertyChanged += (_, _) => RebuildPreview();
        RebuildPreview();
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
        var brushes = ThemeBrushes.Build(
            theme, appearance, _settings.GlassTint,
            allowTransparency: SystemAppearance.TransparencyEnabled,
            highContrast: SystemAppearance.HighContrast);

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

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
