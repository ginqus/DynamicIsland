using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

static class Visuals
{
    public static Color WithAlpha(this Color color, double alpha) =>
        Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B);

    public static IEnumerable<Point> Points(this PathFigure figure)
    {
        yield return figure.StartPoint;
        foreach (PathSegment segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly)
            {
                foreach (Point point in poly.Points) yield return point;
            }
            else if (segment is LineSegment line)
            {
                yield return line.Point;
            }
        }
    }

    public static void SetVisible(this UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}
