using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public sealed class Shimmer : FrameworkElement
{
    const double RowHeight = 18, RowGap = 4, BarHeight = 10;
    const double SheenWidth = 170;
    const double Slant = 0.35;
    const double Whitening = 0.35;
    static readonly TimeSpan SweepTime = TimeSpan.FromSeconds(1.5), PauseTime = TimeSpan.FromSeconds(0.6);

    static readonly (double Width, double Alpha)[] Rows = [(0.58, 0.55), (0.84, 1), (0.42, 0.55)];
    static readonly double[] StopOffsets = [0, 0.3, 0.5, 0.7, 1];
    static readonly double RowsHeight = Rows.Length * RowHeight + (Rows.Length - 1) * RowGap;

    readonly SolidColorBrush _base = new(Color.FromArgb(26, 255, 255, 255));
    readonly LinearGradientBrush _sheen;
    readonly TranslateTransform _move = new() { X = -SheenWidth * 2 };
    bool _running;

    public Shimmer()
    {
        _sheen = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(SheenWidth, SheenWidth * Slant),
            Transform = _move,
        };
        Color[] colors = Sheen(Colors.White.WithAlpha(0.35), Colors.White.WithAlpha(0.8), Colors.White.WithAlpha(0.35));
        for (int i = 0; i < colors.Length; i++) _sheen.GradientStops.Add(new GradientStop(colors[i], StopOffsets[i]));
    }

    public void Tint(IReadOnlyList<Color> colors, Duration time)
    {
        Color first = Whiten(colors[0], Whitening), second = Whiten(colors[Math.Min(1, colors.Count - 1)], Whitening);
        Color[] stops = Sheen(first.WithAlpha(0.45), Whiten(first, 0.6).WithAlpha(0.85), second.WithAlpha(0.45));
        for (int i = 0; i < stops.Length; i++)
            _sheen.GradientStops[i].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(stops[i], time));
    }

    public void SetRunning(bool on)
    {
        if (on == _running) return;
        _running = on;
        if (!on)
        {
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }

        double from = -SheenWidth - Rows.Length * (RowHeight + RowGap) * Slant;
        double to = (double.IsNaN(Width) ? ActualWidth : Width) + SheenWidth;
        var sweep = new DoubleAnimationUsingKeyFrames { Duration = SweepTime + PauseTime, RepeatBehavior = RepeatBehavior.Forever };
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(SweepTime), new SineEase { EasingMode = EasingMode.EaseInOut }));
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(to, KeyTime.FromTimeSpan(SweepTime + PauseTime)));
        _move.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 0 : available.Width, RowsHeight);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, top = (ActualHeight - RowsHeight) / 2;
        if (w <= 0) return;

        for (int i = 0; i < Rows.Length; i++)
        {
            var bar = new Rect(0, top + i * (RowHeight + RowGap) + (RowHeight - BarHeight) / 2, Math.Round(w * Rows[i].Width), BarHeight);
            dc.PushOpacity(Rows[i].Alpha);
            dc.DrawRoundedRectangle(_base, null, bar, BarHeight / 2, BarHeight / 2);
            dc.DrawRoundedRectangle(_sheen, null, bar, BarHeight / 2, BarHeight / 2);
            dc.Pop();
        }
    }

    static Color[] Sheen(Color rise, Color crest, Color fall) => [Colors.Transparent, rise, crest, fall, Colors.Transparent];

    static Color Whiten(Color color, double share) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * share), (byte)(color.G + (255 - color.G) * share), (byte)(color.B + (255 - color.B) * share));
}
