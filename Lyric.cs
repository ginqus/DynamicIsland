using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// A line of the lyrics in the expanded player: it wraps once, a longer one is cut. While it is sung it fills
/// with light from its first letter to its last, the second row of a wrapped line after the first. A line with
/// no words is a break in the singing: three dots that breathe while it lasts, and go out one by one, the last of
/// them first, as the next line comes near.
/// </summary>
public sealed class Lyric : FrameworkElement
{
    const double FontSize = 14, LineHeight = 18;
    const int MaxLines = 2;
    const double Edge = 26; // width of the soft boundary between what has been sung and what has not

    const int Dots = 3;                   // of a break in the singing
    const double Dot = 6, DotGap = 5;     // across each, and between two
    const double Faint = 1.75;            // a dot that has gone out loses this many times what the words not sung yet lose
    const double Dying = 0.3;             // share of a dot's turn that it takes to go out
    const double Swell = 0.24;            // how far the dots swell with each breath...
    const double Breath = 1.8;            // ...and seconds a breath takes

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(Lyric), new FrameworkPropertyMetadata(0.0, (d, _) => ((Lyric)d).Sweep()));

    public static readonly DependencyProperty UnsungProperty = DependencyProperty.Register(
        nameof(Unsung), typeof(double), typeof(Lyric),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Lyric)d).Sweep()));

    readonly string _words;
    readonly bool _break;               // it has no words: a break in the singing
    FormattedText? _text;
    double[] _rows = [];                // how far the words reach in each row of the line
    LinearGradientBrush[] _masks = [];  // one per row: opaque up to the boundary, Unsung past it
    // the dots of a break: each has its own light, and they swell and shrink together, each where it stands
    readonly SolidColorBrush[] _dots = [];
    readonly ScaleTransform _breath = new(1, 1);

    public Lyric(string words)
    {
        _words = words;
        _break = Wordless(words);
        if (_break) _dots = Enumerable.Range(0, Dots).Select(_ => new SolidColorBrush(Colors.White)).ToArray();
    }

    /// <summary>Whether a line of the lyrics has nothing to sing in it: it is empty, or all notes and dashes.</summary>
    public static bool Wordless(string words) => !words.Any(char.IsLetterOrDigit);

    /// <summary>How much of the line has been sung, 0 → 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Opacity of the part not sung yet. At 1 the line is evenly lit, whatever its progress.</summary>
    public double Unsung
    {
        get => (double)GetValue(UnsungProperty);
        set => SetValue(UnsungProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 0 : available.Width;
        if (_break)
        {
            Sweep();
            return new Size(width, LineHeight);
        }

        var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _text = new FormattedText(_words, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(width, 1),
            MaxLineCount = MaxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            LineHeight = LineHeight,
        };

        int rows = Math.Clamp((int)Math.Round(_text.Height / LineHeight), 1, MaxLines);
        _rows = new double[rows];
        _masks = new LinearGradientBrush[rows];
        // the highlight is a box per row, each as wide as its words: the top edge of the lot belongs to the first
        // row and the bottom edge to the last
        if (_text.BuildHighlightGeometry(new Point()) is { } boxes)
        {
            Rect all = boxes.Bounds;
            foreach (PathFigure figure in boxes.GetFlattenedPathGeometry().Figures)
            {
                Reach(figure.StartPoint, all);
                foreach (PathSegment segment in figure.Segments)
                    if (segment is PolyLineSegment poly) foreach (Point p in poly.Points) Reach(p, all);
                    else if (segment is LineSegment line) Reach(line.Point, all);
            }
        }
        for (int i = 0; i < rows; i++)
        {
            _masks[i] = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, EndPoint = new Point(Math.Max(width, 1), 0) };
            // lit from the start to the boundary, which the two stops in the middle are the sides of
            foreach (double offset in new[] { 0.0, 0, 1, 1 }) _masks[i].GradientStops.Add(new GradientStop(Colors.Black, offset));
        }
        Sweep();
        return new Size(width, rows * LineHeight);
    }

    void Reach(Point p, Rect all)
    {
        if (p.Y < all.Top + 0.5) _rows[0] = Math.Max(_rows[0], p.X);
        if (p.Y > all.Bottom - 0.5) _rows[^1] = Math.Max(_rows[^1], p.X);
    }

    // moves the boundary along the rows: the brushes are already in the picture, so nothing is drawn again
    void Sweep()
    {
        if (_break)
        {
            Breathe();
            return;
        }

        double width = _masks.Length > 0 ? _masks[0].EndPoint.X : 0;
        Color rest = Color.FromArgb((byte)(255 * Math.Clamp(Unsung, 0, 1)), 0, 0, 0);
        // each row is swept from a boundary's width before its start, so it opens with nothing lit
        double at = Math.Clamp(Progress, 0, 1) * (_rows.Sum() + Edge * _rows.Length);
        for (int i = 0; i < _masks.Length; i++)
        {
            double from = Math.Clamp(at, 0, _rows[i] + Edge) - Edge;
            at -= _rows[i] + Edge;
            GradientStopCollection stops = _masks[i].GradientStops;
            stops[1].Offset = Math.Clamp(from / width, 0, 1);
            stops[2].Offset = Math.Clamp((from + Edge) / width, 0, 1);
            stops[2].Color = stops[3].Color = rest;
        }
    }

    /// <summary>
    /// The dots of a break. Among the other lines they are evenly lit and still. Once the break has come they
    /// breathe, and of the time until the next line each has its third: the last dot goes out over the first of
    /// them, the first over the last. Asked on every frame the playback moves, so they hold their breath while it is paused.
    /// </summary>
    void Breathe()
    {
        // how bright a dot is once it has gone out: as bright as the rest while the row sits among the others
        double gone = Math.Clamp(1 - Faint * (1 - Unsung), 0, 1);
        double left = (1 - Math.Clamp(Progress, 0, 1)) * Dots;
        for (int i = 0; i < Dots; i++)
            _dots[i].Opacity = gone + (1 - gone) * Math.Clamp((left - i) / Dying, 0, 1);

        double breath = (1 - Math.Cos(Clock.Elapsed.TotalSeconds * 2 * Math.PI / Breath)) / 2;
        _breath.ScaleX = _breath.ScaleY = 1 + Swell * (1 - gone) * breath;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_break)
        {
            for (int i = 0; i < Dots; i++)
            {
                // a px in from the edge the words start at: the room the first dot swells into, or it would be cut
                dc.PushTransform(new TranslateTransform(1 + Dot / 2 + i * (Dot + DotGap), LineHeight / 2));
                dc.PushTransform(_breath);
                dc.DrawEllipse(_dots[i], null, new Point(), Dot / 2, Dot / 2);
                dc.Pop();
                dc.Pop();
            }
            return;
        }

        if (_text == null) return;
        if (Unsung >= 1)
        {
            dc.DrawText(_text, new Point());
            return;
        }

        for (int i = 0; i < _masks.Length; i++)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, i * LineHeight, ActualWidth, LineHeight)));
            dc.PushOpacityMask(_masks[i]);
            dc.DrawText(_text, new Point());
            dc.Pop();
            dc.Pop();
        }
    }
}
