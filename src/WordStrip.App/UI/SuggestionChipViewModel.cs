using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WordStrip.App.UI.Design;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WordStrip.App.UI;

/// <summary>
/// One slot on the bar. The bar keeps a fixed pool of these — one per slot — and changes their text in place
/// as predictions change, rather than recreating them. Recreating chips regenerates their containers, and
/// that is a layout pass through the whole window on every keystroke; changing a string is not.
///
/// <para>The first slot reads slightly stronger than the rest (it is what Tab takes). The selection treatment
/// — <see cref="IsSelected"/>, drawn with the theme's selection surface — marks whatever the user's next key
/// will take: the first slot when Space would commit it, or the candidate a Tab cycle has just inserted.</para>
/// </summary>
public sealed class SuggestionChipViewModel : INotifyPropertyChanged
{
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

    /// <summary>
    /// Weight depends on the slot, never on the selection. A selected word that also got heavier would
    /// re-measure and shift inside its slot every time the selection moved, and the selection surface
    /// already says which word is taken. The first slot is the likely answer, so it carries the weight.
    /// </summary>
    public FontWeight FontWeight => _isPrimary && !_isEmoji ? FontWeights.SemiBold : FontWeights.Normal;

    public double TextOpacity => _isSelected || (_isPrimary && !_isEmoji) ? 1.0 : DesignTokens.Type.AlternateOpacity;

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
