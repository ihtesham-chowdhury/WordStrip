using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WordStrip.App.UI;

/// <summary>
/// One slot on the bar. The bar keeps a fixed pool of these — one per slot — and changes their text in place
/// as predictions change, rather than recreating them. Recreating chips regenerates their containers, and
/// that is a layout pass through the whole window on every keystroke; changing a string is not.
///
/// <para>Three visual weights, none of which look like a selection: the first slot reads slightly stronger
/// than the rest (it is what Tab takes), and firms up to semibold when Space would commit it. Only
/// <see cref="IsSelected"/> — set solely while the user is cycling with Tab — carries the selection
/// treatment.</para>
/// </summary>
public sealed class SuggestionChipViewModel : INotifyPropertyChanged
{
    /// <summary>How much alternates recede behind the first slot. Enough to rank them at a glance, not enough to hide them.</summary>
    private const double AlternateOpacity = 0.74;

    private string _word = string.Empty;
    private bool _isEmoji;
    private bool _isPrimary;
    private bool _isArmed;
    private bool _isSelected;
    private bool _collapseWhenEmpty;
    private Brush _foreground = Brushes.White;

    public string Word
    {
        get => _word;
        set
        {
            if (string.Equals(_word, value, StringComparison.Ordinal)) return;
            _word = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChipVisibility));
        }
    }

    /// <summary>Emoji stay visually secondary: never emphasised, even in the first slot.</summary>
    public bool IsEmoji
    {
        get => _isEmoji;
        set { if (Set(ref _isEmoji, value)) RaiseAppearance(); }
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set { if (Set(ref _isPrimary, value)) RaiseAppearance(); }
    }

    /// <summary>Space or closing punctuation would commit this chip. Only ever set on the first slot.</summary>
    public bool IsArmed
    {
        get => _isArmed;
        set { if (Set(ref _isArmed, value)) RaiseAppearance(); }
    }

    /// <summary>The user has just put this candidate into their text with Tab. The only selected state there is.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) RaiseAppearance(); }
    }

    /// <summary>
    /// Empty slots keep their room when the bar has fixed geometry, so nothing shifts when fewer candidates
    /// arrive; when the bar sizes to its content they take none.
    /// </summary>
    public bool CollapseWhenEmpty
    {
        get => _collapseWhenEmpty;
        set { if (Set(ref _collapseWhenEmpty, value)) OnPropertyChanged(nameof(ChipVisibility)); }
    }

    public Visibility ChipVisibility => _word.Length > 0
        ? Visibility.Visible
        : _collapseWhenEmpty ? Visibility.Collapsed : Visibility.Hidden;

    /// <summary>Sizing comes from the shared metrics so a thickness change reflows every chip identically.</summary>
    public required GlassMetrics Metrics { get; init; }

    /// <summary>Theme-provided hover tint. Bound rather than baked into the template so themes can differ.</summary>
    public required Brush HoverBrush { get; init; }

    public Thickness Padding => new(Metrics.ChipPaddingX, Metrics.ChipPaddingY, Metrics.ChipPaddingX, Metrics.ChipPaddingY);
    public Thickness Margin => new(Metrics.ChipMarginX, 0, Metrics.ChipMarginX, 0);
    public CornerRadius CornerRadius => new(Metrics.ChipRadius);
    public double MinHeight => Metrics.ChipMinHeight;
    public double FontSize => Metrics.FontSize;

    public FontWeight FontWeight => _isSelected || (_isArmed && !_isEmoji) ? FontWeights.SemiBold : FontWeights.Medium;

    public double TextOpacity => _isSelected || (_isPrimary && !_isEmoji) ? 1.0 : AlternateOpacity;

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

    private void RaiseAppearance()
    {
        OnPropertyChanged(nameof(FontWeight));
        OnPropertyChanged(nameof(TextOpacity));
    }

    private bool Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
