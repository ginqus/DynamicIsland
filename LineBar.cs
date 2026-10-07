using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class LineBar : FrameworkElement
{
    const double MarkGap = 2;
    const double MinSegmentWidth = 6;
    const double SnapReach = 5;
    const double PassedOpacity = 0.85;
    const double AheadOpacity = 0.3;

    static readonly Brush Unplayed = Shades.Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(SolidColorBrush), typeof(LineBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly Shades _shades = new();

    double[] _starts = [];
    double _fill;

    public SolidColorBrush Accent
    {
        get => (SolidColorBrush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public bool HasMarks => _starts.Length > 0;

    public double Fill
    {
        get => _fill;
        set
        {
            if (value == _fill) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    public void SetStarts(double[] fractions)
    {
        _starts = fractions;
        InvalidateVisual();
    }

    public double Snap(double x)
    {
        List<double> edges = Edges(ActualWidth);
        double nearest = x;
        for (int i = 1; i < edges.Count - 1; i++)
            if (Math.Abs(edges[i] - x) <= SnapReach && Math.Abs(edges[i] - x) < Math.Abs(nearest - x)) nearest = edges[i];
        return nearest;
    }

    List<double> Edges(double width)
    {
        var edges = new List<double> { 0 };
        foreach (double start in _starts)
        {
            double x = start * width;
            if (x - edges[^1] >= MinSegmentWidth && width - x >= MinSegmentWidth) edges.Add(x);
        }
        edges.Add(width);
        return edges;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        List<double> edges = Edges(width);
        int last = edges.Count - 2;
        double fill = Math.Clamp(_fill, 0, width);
        int current = 0;
        while (current < last && edges[current + 1] <= fill) current++;

        for (int i = 0; i <= last; i++)
        {
            double left = edges[i] + (i > 0 ? MarkGap / 2 : 0), right = edges[i + 1] - (i < last ? MarkGap / 2 : 0);
            if (right <= left) continue;
            var segment = new Rect(left, 0, right - left, height);
            double radius = Math.Min(height, segment.Width) / 2;

            if (i != current)
            {
                dc.DrawRoundedRectangle(i < current ? Shades.White(PassedOpacity) : Unplayed, null, segment, radius, radius);
                continue;
            }

            Color accent = Accent.Color;
            dc.DrawRoundedRectangle(_shades.Of(accent, AheadOpacity), null, segment, radius, radius);
            if (fill <= left) continue;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, fill, height)));
            dc.DrawRoundedRectangle(_shades.Of(accent, 1), null, segment, radius, radius);
            dc.Pop();
        }
    }
}
