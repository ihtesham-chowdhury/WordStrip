using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace WordStrip.App.UI;

/// <summary>
/// Builds a rounded rectangle with <b>continuous</b> corner curvature — the "squircle" shape Apple uses —
/// rather than the circular arcs WPF's <c>CornerRadius</c> produces.
///
/// <para>The difference is curvature continuity. A circular corner jumps from zero curvature along the
/// straight edge to a constant curvature the instant the arc begins, and the eye reads that discontinuity as
/// a slightly pinched, "stuck-on" corner. A superellipse eases curvature in, so the straight edge flows into
/// the corner. It is subtle per-corner and very obvious across a whole shape.</para>
///
/// <para>Each corner is one cubic Bézier fitted to the superellipse |x/r|⁴ + |y/r|⁴ = 1. That curve passes
/// through r·0.5^¼ ≈ 0.8409r at 45°; solving the Bézier midpoint for that point gives a control-point offset
/// of 0.909r, against 0.5523r for a true circular arc. One curve per corner keeps this cheap enough to
/// regenerate whenever the bar resizes.</para>
/// </summary>
public static class SquircleGeometry
{
    /// <summary>Control-point offset as a fraction of the radius, fitted to a fourth-order superellipse.</summary>
    private const double ControlOffsetRatio = 0.909;

    public static Geometry Create(double width, double height, double radius)
    {
        if (width <= 0 || height <= 0)
            return Geometry.Empty;

        // A radius past half the shorter side has no room left for the straight section between corners.
        radius = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));

        if (radius <= 0.01)
            return new RectangleGeometry(new Rect(0, 0, width, height));

        var c = radius * ControlOffsetRatio;

        var figure = new PathFigure { StartPoint = new Point(radius, 0), IsClosed = true, IsFilled = true };

        // Top edge, then top-right corner.
        figure.Segments.Add(new LineSegment(new Point(width - radius, 0), isStroked: true));
        figure.Segments.Add(new BezierSegment(
            new Point(width - radius + c, 0),
            new Point(width, radius - c),
            new Point(width, radius),
            isStroked: true));

        // Right edge, then bottom-right corner.
        figure.Segments.Add(new LineSegment(new Point(width, height - radius), isStroked: true));
        figure.Segments.Add(new BezierSegment(
            new Point(width, height - radius + c),
            new Point(width - radius + c, height),
            new Point(width - radius, height),
            isStroked: true));

        // Bottom edge, then bottom-left corner.
        figure.Segments.Add(new LineSegment(new Point(radius, height), isStroked: true));
        figure.Segments.Add(new BezierSegment(
            new Point(radius - c, height),
            new Point(0, height - radius + c),
            new Point(0, height - radius),
            isStroked: true));

        // Left edge, then top-left corner back to the start.
        figure.Segments.Add(new LineSegment(new Point(0, radius), isStroked: true));
        figure.Segments.Add(new BezierSegment(
            new Point(0, radius - c),
            new Point(radius - c, 0),
            new Point(radius, 0),
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
