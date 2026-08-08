using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WordStrip.App.UI;

public sealed class SuggestionChipViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private Brush _foreground = Brushes.White;

    public required string Word { get; init; }

    /// <summary>Sizing comes from the shared metrics so a thickness change reflows every chip identically.</summary>
    public required GlassMetrics Metrics { get; init; }

    /// <summary>Theme-provided hover tint. Bound rather than baked into the template so themes can differ.</summary>
    public required Brush HoverBrush { get; init; }

    public Thickness Padding => new(Metrics.ChipPaddingX, Metrics.ChipPaddingY, Metrics.ChipPaddingX, Metrics.ChipPaddingY);
    public Thickness Margin => new(Metrics.ChipMarginX, 0, Metrics.ChipMarginX, 0);
    public CornerRadius CornerRadius => new(Metrics.ChipRadius);
    public double MinHeight => Metrics.ChipMinHeight;
    public double FontSize => Metrics.FontSize;

    /// <summary>
    /// Whether the selection surface is currently over this chip. Drives both the text colour and a one-step
    /// weight change: selection is carried by surface, position and motion, so the type only needs to firm
    /// up slightly rather than jump to bold.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontWeight));
        }
    }

    public FontWeight FontWeight => _isSelected ? FontWeights.SemiBold : FontWeights.Medium;

    public Brush Foreground
    {
        get => _foreground;
        set
        {
            if (ReferenceEquals(_foreground, value)) return;
            _foreground = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
