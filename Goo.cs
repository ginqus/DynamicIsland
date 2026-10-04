using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>
/// The island's black body. The pill and the bubble that splits off it are one piece of liquid: while their
/// round ends are close a neck joins them, thinning as they part until it snaps. Its light edge is also how the
/// island says things without words: it glows on the bass of the music, and flashes while an alarm rings.
/// </summary>
public sealed class Goo : FrameworkElement
{
    const double Rim = 1;          // the light edge around the body
    const double Tear = 7.5;       // gap between the two round ends at which the neck snaps
    const double Hold = 0.5;       // how far round each end the neck reaches while it is thick
    const double Handle = 2.4;     // how long the neck's curves keep to the direction they leave an end in
    const double Tolerance = 0.02; // of the merged outline

    static readonly Color Plain = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
    const byte Tinted = 0x8C;      // alpha of the edge while it takes a colour: one hue needs more than white to show

    // light the lit edge gives off: a haze to either side of it, laid on in strokes each reaching a px further than
    // the one before, so it thins out away from the edge
    static readonly double[] Reach = [2, 3, 4];
    const double Haze = 0.14;      // how bright each of those strokes is, next to the edge itself
    // an alarm lights the edge twice at the start of each of its rounds: (share of the round, how bright)
    static readonly (double At, double Level)[] Flash = [(0, 0), (0.04, 1), (0.13, 0.3), (0.19, 0.9), (0.5, 0), (1, 0)];

    readonly SolidColorBrush _rim = new(Plain);
    readonly Light _beat = new(), _flash = new(); // the edge lit: by the bass of the music, by an alarm
    readonly Pen _edge;

    Rect _pill = Rect.Empty, _bubble = Rect.Empty;
    double _radius;

    public Goo() => _edge = new Pen(_rim, 2 * Rim) { LineJoin = PenLineJoin.Round };

    /// <summary>Turns the light edge to a colour, or back to its own faint white with null.</summary>
    public void Tint(Color? colour, Duration time)
    {
        Color to = colour is { } c ? Color.FromArgb(Tinted, c.R, c.G, c.B) : Plain;
        // the brushes are already in the picture, so nothing is drawn again
        _rim.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, time));
        // the beat lights the edge in the colour it has
        var lit = new ColorAnimation(colour ?? Colors.White, time);
        _beat.Edge.BeginAnimation(SolidColorBrush.ColorProperty, lit);
        _beat.Mist.BeginAnimation(SolidColorBrush.ColorProperty, lit);
    }

    /// <summary>Lights the edge up by this much, 0 → 1: asked on every frame of the music, with the weight of its bass.</summary>
    public void Beat(double level)
    {
        level = Math.Clamp(level, 0, 1);
        _beat.Edge.Opacity = level;
        _beat.Mist.Opacity = level * Haze;
    }

    /// <summary>Raises the alarm: the edge flashes at the start of every <paramref name="round"/>, until it is told to <see cref="Still"/>.</summary>
    public void Alarm(Color colour, TimeSpan round)
    {
        _flash.Edge.Color = _flash.Mist.Color = colour;
        _flash.Edge.BeginAnimation(Brush.OpacityProperty, Flashes(1));
        _flash.Mist.BeginAnimation(Brush.OpacityProperty, Flashes(Haze));

        DoubleAnimationUsingKeyFrames Flashes(double share)
        {
            var flashes = new DoubleAnimationUsingKeyFrames { Duration = round, RepeatBehavior = RepeatBehavior.Forever, FillBehavior = FillBehavior.Stop };
            foreach ((double at, double level) in Flash)
                flashes.KeyFrames.Add(new LinearDoubleKeyFrame(level * share, KeyTime.FromPercent(at)));
            return flashes;
        }
    }

    /// <summary>Calls the alarm off.</summary>
    public void Still()
    {
        _flash.Edge.BeginAnimation(Brush.OpacityProperty, null);
        _flash.Mist.BeginAnimation(Brush.OpacityProperty, null);
    }

    /// <summary>Where the two are, in this element's own coordinates. An empty bubble is one tucked away out of sight.</summary>
    public void Shape(Rect pill, double radius, Rect bubble)
    {
        if (pill == _pill && radius == _radius && bubble == _bubble) return;
        _pill = pill;
        _radius = radius;
        _bubble = bubble;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_pill.IsEmpty) return;
        Geometry pill = Outline(_pill, _radius);
        if (_bubble.IsEmpty)
        {
            Draw(dc, pill);
            return;
        }

        Geometry bubble = Outline(_bubble, _bubble.Height / 2);
        Geometry? neck = Neck();
        if (neck == null && !_pill.IntersectsWith(_bubble))
        {
            Draw(dc, pill);
            Draw(dc, bubble);
            return;
        }

        // one outline for the lot, so the rim runs round the whole shape instead of across the joint
        Geometry body = Geometry.Combine(pill, bubble, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);
        if (neck != null) body = Geometry.Combine(body, neck, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);
        Draw(dc, body);
    }

    // the rim is the outer half of a line along the edge: it lightens what is behind the island, not its black
    void Draw(DrawingContext dc, Geometry body)
    {
        Rect around = body.Bounds;
        around.Inflate(2 * Rim, 2 * Rim);
        var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
        outside.Children.Add(new RectangleGeometry(around));
        outside.Children.Add(body);

        dc.PushClip(outside);
        dc.DrawGeometry(null, _edge, body);
        dc.DrawGeometry(null, _beat.Line, body);
        dc.DrawGeometry(null, _flash.Line, body);
        dc.Pop();
        dc.DrawGeometry(Brushes.Black, null, body);
        // the haze of a lit edge lies on the black as much as around it: over a light window that is where it shows
        foreach (Pen mist in _beat.Mists) dc.DrawGeometry(null, mist, body);
        foreach (Pen mist in _flash.Mists) dc.DrawGeometry(null, mist, body);
    }

    /// <summary>The whole edge lit: the edge itself drawn over, and a haze to either side of it. See-through until it is turned up.</summary>
    sealed class Light
    {
        public readonly SolidColorBrush Edge = new(Colors.White) { Opacity = 0 }, Mist = new(Colors.White) { Opacity = 0 };
        public readonly Pen Line;
        public readonly Pen[] Mists;

        public Light()
        {
            Line = new Pen(Edge, 2 * Rim) { LineJoin = PenLineJoin.Round };
            Mists = Reach.Select(reach => new Pen(Mist, 2 * reach) { LineJoin = PenLineJoin.Round }).ToArray();
        }
    }

    static Geometry Outline(Rect rect, double radius)
    {
        rect.Inflate(-Math.Min(Rim, rect.Width / 2), -Math.Min(Rim, rect.Height / 2));
        return Squircle.Of(rect, Math.Max(radius - Rim, 0));
    }

    /// <summary>The bridge between the pill's top right corner and the bubble's left end, each taken as a circle.</summary>
    Geometry? Neck()
    {
        double r1 = _radius - Rim, r2 = _bubble.Height / 2 - Rim;
        var c1 = new Point(_pill.Right - _radius, _pill.Top + _radius);
        var c2 = new Point(_bubble.Left + _bubble.Height / 2, _bubble.Top + _bubble.Height / 2);
        Vector between = c2 - c1;
        double d = between.Length, gap = d - r1 - r2;
        // still tucked behind the pill, one end inside the other, or pulled clear
        if (r1 <= 0 || r2 <= 0 || between.X <= 0 || d <= Math.Abs(r1 - r2) || gap >= Tear) return null;

        // where the circles cross, as an angle off the line between their centres; none once they have parted
        double u1 = 0, u2 = 0;
        if (gap < 0)
        {
            u1 = Math.Acos(Math.Clamp((r1 * r1 + d * d - r2 * r2) / (2 * r1 * d), -1, 1));
            u2 = Math.Acos(Math.Clamp((r2 * r2 + d * d - r1 * r1) / (2 * r2 * d), -1, 1));
        }

        // the neck lets go of the ends as the gap opens, and is down to nothing at the tear
        double hold = Hold * (1 - Math.Clamp(gap / Tear, 0, 1));
        double axis = Math.Atan2(between.Y, between.X), wide = Math.Acos((r1 - r2) / d);
        double a1 = axis + u1 + (wide - u1) * hold, a2 = axis - u1 - (wide - u1) * hold;
        double a3 = axis + Math.PI - u2 - (Math.PI - u2 - wide) * hold, a4 = axis - Math.PI + u2 + (Math.PI - u2 - wide) * hold;
        Point p1 = On(c1, a1, r1), p2 = On(c1, a2, r1), p3 = On(c2, a3, r2), p4 = On(c2, a4, r2);

        double reach = Math.Min(hold * Handle, (p1 - p3).Length / (r1 + r2)) * Math.Min(1, 2 * d / (r1 + r2));
        const double Quarter = Math.PI / 2;
        var neck = new StreamGeometry();
        using (StreamGeometryContext g = neck.Open())
        {
            g.BeginFigure(p1, true, true);
            g.BezierTo(On(p1, a1 - Quarter, r1 * reach), On(p3, a3 + Quarter, r2 * reach), p3, true, true);
            g.LineTo(p4, true, true);
            g.BezierTo(On(p4, a4 - Quarter, r2 * reach), On(p2, a2 + Quarter, r1 * reach), p2, true, true);
        }
        return neck;
    }

    static Point On(Point centre, double angle, double radius) =>
        new(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle));
}
