using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

sealed class Shades
{
    const int Levels = 255;

    static readonly Shades Whites = new();

    readonly Brush?[] _brushes = new Brush?[Levels + 1];
    Color _color = Colors.White;

    public static Brush White(double opacity) => Whites.Of(Colors.White, opacity);

    public Brush Of(Color color, double opacity)
    {
        if (color != _color)
        {
            _color = color;
            Array.Clear(_brushes);
        }
        int level = (int)Math.Round(Math.Clamp(opacity, 0, 1) * Levels);
        return _brushes[level] ??= Frozen(new SolidColorBrush(Color.FromArgb((byte)(color.A * level / Levels), color.R, color.G, color.B)));
    }

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
