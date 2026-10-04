using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// A rectangle with its corners rounded the way Apple rounds them: the curve does not start all at once where the
/// straight edge ends, it eases out of the edge, runs through a short arc and eases into the next edge. A shape too
/// low for that (the compact pill, whose ends are half circles) keeps plain arcs, and gets the easing by degrees as
/// it grows.
/// </summary>
static class Squircle
{
    const double Smoothing = 0.6; // how far past the start of a plain arc the curve reaches along the edge, as a share of the radius

    /// <summary>The outline, from the middle of the top edge and clockwise back to it.</summary>
    public static Geometry Of(Rect rect, double radius)
    {
        double room = Math.Min(rect.Width, rect.Height) / 2;
        double r = Math.Min(radius, room);
        if (r <= 0.01) return new RectangleGeometry(rect);

        // only as smooth as there is edge to ease along
        double smooth = Math.Clamp(room / r - 1, 0, Smoothing);
        double reach = Math.Min((1 + smooth) * r, room);                  // of the corner along each edge
        double sweep = Math.PI / 2 * (1 - smooth);                         // what is left of the arc in the middle of it
        double arc = Math.Sin(sweep / 2) * r * Math.Sqrt(2);               // ...and how far that goes along each edge
        double lean = Math.PI / 4 * smooth;
        double c = r * Math.Tan(lean / 2) * Math.Cos(lean), d = c * Math.Tan(lean);
        double b = (reach - arc - c - d) / 3, a = 2 * b;

        var start = new Point(rect.Left + rect.Width / 2, rect.Top);
        var outline = new StreamGeometry();
        using (StreamGeometryContext g = outline.Open())
        {
            g.BeginFigure(start, true, true);
            Corner(new Point(rect.Right - reach, rect.Top), new Vector(1, 0), new Vector(0, 1));
            Corner(new Point(rect.Right, rect.Bottom - reach), new Vector(0, 1), new Vector(-1, 0));
            Corner(new Point(rect.Left + reach, rect.Bottom), new Vector(-1, 0), new Vector(0, -1));
            Corner(new Point(rect.Left, rect.Top + reach), new Vector(0, -1), new Vector(1, 0));
            // back along the top edge by a line of its own, so the start of the outline is nowhere but here
            g.LineTo(start, true, false);

            // from where the corner leaves one edge, going along it, to where it joins the next
            void Corner(Point from, Vector along, Vector turn)
            {
                Point At(Point p, double x, double y) => p + along * x + turn * y;

                g.LineTo(from, true, false);
                Point p1 = At(from, a + b + c, d), p2 = At(p1, arc, arc);
                if (smooth > 0.001) g.BezierTo(At(from, a, 0), At(from, a + b, 0), p1, true, true);
                g.ArcTo(p2, new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                if (smooth > 0.001) g.BezierTo(At(p2, d, c), At(p2, d, b + c), At(p2, d, a + b + c), true, true);
            }
        }
        outline.Freeze();
        return outline;
    }
}
