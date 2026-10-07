using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

sealed class HoverPreview : FrameworkElement
{
    const double Rounding = 8;
    const double Pitch = 27, SideRadius = 9, PlayRadius = 10.5;
    const double MarkOpacity = 0.85, SideMarkRadius = 1.7;
    const double PointerScale = 0.5, PointerTipX = 7, PointerTipY = 4;

    readonly record struct Waypoint(double At, double X, double Y, bool Pressed = false);

    static readonly Waypoint[] Path =
    [
        new(0, 1.9, 11), new(0.5, 1, 2), new(1.1, 1.05, -2), new(1.5, 0, -1), new(1.9, 0.08, 2),
        new(2.0, 0.08, 2, true), new(2.25, 0.08, 2), new(2.7, -1, 1), new(3.3, -0.95, -3), new(3.8, -1.9, 11),
        new(5.2, 1.9, 11),
    ];

    static readonly Geometry PlayMark = Shades.Frozen(Geometry.Parse("M-2.6,-3.6 L3.6,0 L-2.6,3.6 Z"));
    static readonly Geometry Arrow = Shades.Frozen(Geometry.Parse("M7,4 V18.6 L10.6,15.2 L13,20.4 L15.6,19.2 L13.2,14.1 H18.2 Z"));
    static readonly Pen ArrowEdge = Shades.Frozen(new Pen(Brushes.Black, 1.6) { LineJoin = PenLineJoin.Round });

    static readonly Color[] Whites = [Colors.White, Colors.White, Colors.White];

    readonly Liquid _liquid = new();
    readonly FrameLoop _loop;
    ButtonBase? _tile;
    Point _pointer;
    bool _scripted;
    double _time;

    public HoverPreview()
    {
        _loop = new FrameLoop(Advance);
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _loop.Start();
        };
    }

    public Hover Kind
    {
        get => _liquid.Style;
        set => _liquid.Style = value;
    }

    Rect[] Targets()
    {
        var middle = new Point(ActualWidth / 2, ActualHeight / 2);
        return [Around(middle.X - Pitch, SideRadius), Around(middle.X, PlayRadius), Around(middle.X + Pitch, SideRadius)];

        Rect Around(double x, double r) => new(x - r, middle.Y - r, 2 * r, 2 * r);
    }

    bool Advance(double dt)
    {
        _time = (_time + dt) % Path[^1].At;
        _tile ??= FindTile();
        Rect[] targets = Targets();
        bool pressed;
        _scripted = _tile is not { IsMouseOver: true };
        if (_scripted) (_pointer, pressed) = Scripted();
        else (_pointer, pressed) = (Mouse.GetPosition(this), Mouse.LeftButton == MouseButtonState.Pressed);

        int over = Array.FindIndex(targets, target => target.Contains(_pointer));
        _liquid.Aim(targets, Whites, over, _pointer, pressed);
        _liquid.Advance(dt);
        InvalidateVisual();
        return IsVisible;
    }

    (Point At, bool Pressed) Scripted()
    {
        int i = 0;
        while (i < Path.Length - 2 && Path[i + 1].At <= _time) i++;
        Waypoint from = Path[i], to = Path[i + 1];
        double t = Math.Clamp((_time - from.At) / (to.At - from.At), 0, 1), eased = t * t * (3 - 2 * t);
        double x = from.X + (to.X - from.X) * eased, y = from.Y + (to.Y - from.Y) * eased;
        return (new Point(ActualWidth / 2 + x * Pitch, ActualHeight / 2 + y), from.Pressed);
    }

    ButtonBase? FindTile()
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase tile) return tile;
        return null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), Rounding, Rounding));
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
        _liquid.Draw(dc);

        Brush mark = Shades.White(MarkOpacity);
        double y = h / 2;
        dc.DrawEllipse(mark, null, new Point(w / 2 - Pitch, y), SideMarkRadius, SideMarkRadius);
        dc.DrawEllipse(mark, null, new Point(w / 2 + Pitch, y), SideMarkRadius, SideMarkRadius);
        dc.PushTransform(new TranslateTransform(w / 2, y));
        dc.DrawGeometry(mark, null, PlayMark);
        dc.Pop();

        if (_scripted)
        {
            dc.PushTransform(new TranslateTransform(_pointer.X - PointerTipX * PointerScale, _pointer.Y - PointerTipY * PointerScale));
            dc.PushTransform(new ScaleTransform(PointerScale, PointerScale));
            dc.DrawGeometry(Brushes.White, ArrowEdge, Arrow);
            dc.Pop();
            dc.Pop();
        }
        dc.Pop();
    }
}
