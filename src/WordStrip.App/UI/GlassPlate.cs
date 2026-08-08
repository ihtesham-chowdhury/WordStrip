using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace WordStrip.App.UI;

/// <summary>
/// The glass surface itself: the tinted plate, its specular rim, and the sheen along the lit edge, all
/// drawn directly.
///
/// <para>This replaced a pair of <c>Path</c> elements, which caused a subtle but nasty layout bug. A Path
/// reports its geometry's bounds as its desired size, and that geometry was generated <em>from</em> the
/// bar's measured size — so once the bar grew wide for a long candidate list, the stale geometry kept
/// demanding that width and the window could never shrink again. Short words afterwards left a stretch of
/// empty glass on the right.</para>
///
/// <para>Reporting no desired size breaks the loop: the chips alone decide how wide the bar is, and this
/// element simply paints whatever area it is given.</para>
/// </summary>
public sealed class GlassPlate : FrameworkElement
{
    private static FrameworkPropertyMetadata Render(object? defaultValue = null) =>
        new(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(GlassPlate), Render());

    public static readonly DependencyProperty RimProperty =
        DependencyProperty.Register(nameof(Rim), typeof(Brush), typeof(GlassPlate), Render());

    public static readonly DependencyProperty SheenProperty =
        DependencyProperty.Register(nameof(Sheen), typeof(Brush), typeof(GlassPlate), Render());

    public static readonly DependencyProperty RimThicknessProperty =
        DependencyProperty.Register(nameof(RimThickness), typeof(double), typeof(GlassPlate),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(GlassPlate),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BezelProperty =
        DependencyProperty.Register(nameof(Bezel), typeof(Brush), typeof(GlassPlate), Render());

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Rim { get => (Brush?)GetValue(RimProperty); set => SetValue(RimProperty, value); }
    public Brush? Sheen { get => (Brush?)GetValue(SheenProperty); set => SetValue(SheenProperty, value); }

    /// <summary>
    /// The lensing band just inside the rim. Real glass bends light at its edge, so the perimeter reads
    /// brighter where light enters and darker opposite it; without this the surface looks like a flat tinted
    /// rectangle no matter how good the blur behind it is.
    /// </summary>
    public Brush? Bezel { get => (Brush?)GetValue(BezelProperty); set => SetValue(BezelProperty, value); }

    public double RimThickness { get => (double)GetValue(RimThicknessProperty); set => SetValue(RimThicknessProperty, value); }
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    private Geometry? _outline;
    private Geometry? _innerOutline;
    private Geometry? _bezelOutline;
    private Size _geometrySize;
    private double _geometryRadius;

    public GlassPlate()
    {
        IsHitTestVisible = false;
    }

    // Deliberately no desired size — see the class remarks.
    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        EnsureGeometry(new Size(width, height));
        if (_outline is null) return;

        var pen = Rim is null ? null : new Pen(Rim, RimThickness);
        pen?.Freeze();

        drawingContext.DrawGeometry(Fill, pen, _outline);

        if (Sheen is not null && _innerOutline is not null)
            drawingContext.DrawGeometry(Sheen, null, _innerOutline);

        if (Bezel is not null && _bezelOutline is not null)
        {
            var bezelPen = new Pen(Bezel, BezelThickness);
            bezelPen.Freeze();
            drawingContext.DrawGeometry(null, bezelPen, _bezelOutline);
        }
    }

    /// <summary>Width of the lensing band. Wide enough to read as depth, narrow enough not to look like a second border.</summary>
    private const double BezelThickness = 2.0;

    /// <summary>Rebuilds the squircle outlines only when the size or radius actually changed.</summary>
    private void EnsureGeometry(Size size)
    {
        if (_outline is not null && size == _geometrySize && Math.Abs(_geometryRadius - CornerRadius) < 0.01)
            return;

        _geometrySize = size;
        _geometryRadius = CornerRadius;

        // A stroke straddles its path, so inset by half the rim to keep it fully inside the window.
        var half = RimThickness / 2;
        _outline = Translate(SquircleGeometry.Create(size.Width - RimThickness, size.Height - RimThickness, CornerRadius), half, half);
        _innerOutline = Translate(
            SquircleGeometry.Create(size.Width - (RimThickness * 2), size.Height - (RimThickness * 2), CornerRadius - RimThickness),
            RimThickness, RimThickness);

        // Sits a bezel-width inside the rim, so the band is drawn wholly within the glass rather than
        // straddling its edge and reading as a second border.
        var bezelInset = RimThickness + (BezelThickness / 2);
        _bezelOutline = Translate(
            SquircleGeometry.Create(size.Width - (bezelInset * 2), size.Height - (bezelInset * 2), CornerRadius - bezelInset),
            bezelInset, bezelInset);
    }

    private static Geometry Translate(Geometry geometry, double x, double y)
    {
        if (geometry.IsEmpty()) return geometry;

        var moved = geometry.Clone();
        moved.Transform = new TranslateTransform(x, y);
        moved.Freeze();
        return moved;
    }
}
