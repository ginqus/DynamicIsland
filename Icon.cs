using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public enum Glyph
{
    Mute, Quiet, Mid, Loud, Headphones, Speaker, Vpn, Offline, Wifi, Wired, Bell, Note, Battery, Minus, Plus, Chevron, Back,
    Clock, Gear, Lines, Sparkle, Rim, Expand, Windows, Power, Look, Size, Gap, Drop, Moon, Tray, Cross, Pulse, Bolt, VpnOff,
}

/// <summary>
/// The island's own icons: filled shapes with rounded corners on a 24-unit grid, in the manner of the
/// player's buttons, so nothing depends on which icon font the system has. Some are made of pieces that come and
/// go one at a time: the speaker's waves with the volume, the arcs of the Wi-Fi sign as it comes up.
/// </summary>
public sealed class Icon : FrameworkElement
{
    const double Grid = 24;
    const double Soft = 1.5;       // outline that rounds the corners of a solid shape off
    const double Tolerance = 0.01; // of the curves, in grid units

    /// <param name="Solid">Closed shapes, filled.</param>
    /// <param name="Lines">Strokes with round ends, <paramref name="Line"/> thick.</param>
    /// <param name="Cut">Strokes taken out of all that, <paramref name="Gap"/> thick.</param>
    /// <param name="Over">Strokes put back over the cut.</param>
    readonly record struct Art(string Solid = "", string Lines = "", double Line = 2, string Cut = "", double Gap = 2, string Over = "");

    /// <summary>One piece of an icon made of several: it comes and goes by itself.</summary>
    /// <param name="Solid">Closed shapes, filled.</param>
    /// <param name="Lines">Strokes with round ends, <paramref name="Line"/> thick.</param>
    /// <param name="Cut">Strokes taken out of the pieces before this one, <paramref name="Gap"/> thick.</param>
    /// <param name="Drawn">Its strokes run from their start to their end as it comes, and back as it goes; otherwise it fades.</param>
    readonly record struct Piece(string Solid = "", string Lines = "", double Line = 2, string Cut = "", double Gap = 2, bool Drawn = false);

    const double Take = 0.16; // seconds a piece takes to come or to go...
    const double Lag = 0.06;  // ...and each starts this long after the one before it

    // the speaker keeps its place while the waves come and go with the volume, the nearest to it first to come and
    // last to go; muted, a cross is drawn where they were
    static readonly Piece[] Sound =
    [
        new("M2.5,9.6 H6 L10.6,5.6 V18.4 L6,14.4 H2.5 Z"),
        new(Lines: "M13.3,9.3 A3.8,3.8 0 0 1 13.3,14.7"),
        new(Lines: "M15.7,6.9 A7.2,7.2 0 0 1 15.7,17.1"),
        new(Lines: "M18.1,4.5 A10.6,10.6 0 0 1 18.1,19.5"),
        new(Lines: "M14.8,9.2 L20.4,14.8 M20.4,9.2 L14.8,14.8", Drawn: true),
    ];

    // the dot, the arcs over it from the smallest up, and the stroke across them all when there is no network
    const string Slash = "M4,3.5 L20,20.5";
    static readonly Piece[] Net =
    [
        new("M12,17.7 A1,1 0 1 0 12,19.7 A1,1 0 1 0 12,17.7 Z"),
        new(Lines: "M8.5,15.2 A4.9,4.9 0 0 1 15.5,15.2", Line: 2.2),
        new(Lines: "M5.4,12.1 A9.3,9.3 0 0 1 18.6,12.1", Line: 2.2),
        new(Lines: "M2.3,9 A13.7,13.7 0 0 1 21.7,9", Line: 2.2),
        new(Lines: Slash, Line: 2.2, Cut: Slash, Gap: 5.4, Drawn: true),
    ];

    // the shield, and the tick cut into it while the VPN is on
    static readonly Piece[] Guard =
    [
        new("M12,2.8 L19.6,5.6 V11.4 C19.6,16.2 16.4,19.6 12,21.4 C7.6,19.6 4.4,16.2 4.4,11.4 V5.6 Z"),
        new(Cut: "M8.6,11.9 L11,14.3 L15.6,9.4", Drawn: true),
    ];

    /// <summary>The icons made of pieces: the set each is from, and which of its pieces it has, the first being the lowest bit.</summary>
    static readonly Dictionary<Glyph, (Piece[] Set, int Has)> Made = new()
    {
        [Glyph.Mute] = (Sound, 0b10001),
        [Glyph.Quiet] = (Sound, 0b00011),
        [Glyph.Mid] = (Sound, 0b00111),
        [Glyph.Loud] = (Sound, 0b01111),
        [Glyph.Wifi] = (Net, 0b01111),
        [Glyph.Offline] = (Net, 0b11111),
        [Glyph.Vpn] = (Guard, 0b11),
        [Glyph.VpnOff] = (Guard, 0b01),
    };

    static readonly Dictionary<Glyph, Art> Arts = new()
    {
        [Glyph.Headphones] = new("M3.6,14 H7 V19.6 H3.6 Z M17,14 H20.4 V19.6 H17 Z", "M4.6,14 V12.2 A7.4,7.4 0 0 1 19.4,12.2 V14"),
        // a box with its two drivers left open
        [Glyph.Speaker] = new("F0 M7.2,3.4 H16.8 V20.6 H7.2 Z M12,5.4 A2.1,2.1 0 1 0 12,9.6 A2.1,2.1 0 1 0 12,5.4 Z"
            + " M12,11 A3.9,3.9 0 1 0 12,18.8 A3.9,3.9 0 1 0 12,11 Z"),
        // the plug, pins up
        [Glyph.Wired] = new("M4.5,6.5 H19.5 V14.5 H16 V18 H8 V14.5 H4.5 Z", Cut: "M8.5,6 V9.6 M12,6 V9.6 M15.5,6 V9.6", Gap: 1.5),
        [Glyph.Bell] = new("M12,3.2 C8.6,3.2 6.6,5.8 6.6,9.2 V12.8 L4.8,16.2 H19.2 L17.4,12.8 V9.2 C17.4,5.8 15.4,3.2 12,3.2 Z"
            + " M10,18.9 A2,2 0 0 0 14,18.9 Z"),
        [Glyph.Note] = new("M7.2,15 A2.5,2.5 0 1 0 7.2,20 A2.5,2.5 0 1 0 7.2,15 Z M16.6,13 A2.5,2.5 0 1 0 16.6,18 A2.5,2.5 0 1 0 16.6,13 Z"
            + " M9.2,5.4 L18.6,3.4 V6.8 L9.2,8.8 Z", "M9.3,17.5 V6 M18.7,15.5 V4", 1.8),
        [Glyph.Battery] = new("M5.1,10.7 H16.5 V13.3 H5.1 Z",
            "M4.2,7.6 H17.4 A2.2,2.2 0 0 1 19.6,9.8 V14.2 A2.2,2.2 0 0 1 17.4,16.4 H4.2 A2.2,2.2 0 0 1 2,14.2 V9.8 A2.2,2.2 0 0 1 4.2,7.6 Z"
            + " M21.9,10.7 V13.3", 1.5),
        [Glyph.Minus] = new(Lines: "M5.5,12 H18.5", Line: 2.4),
        [Glyph.Plus] = new(Lines: "M5.5,12 H18.5 M12,5.5 V18.5", Line: 2.4),
        [Glyph.Chevron] = new(Lines: "M9,5 L16,12 L9,19", Line: 2.6),
        [Glyph.Back] = new(Lines: "M15,5 L8,12 L15,19", Line: 2.6),

        // the rows of the menu
        [Glyph.Clock] = new(Lines: "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,8 V12.2 L14.9,14"),
        // a disc, eight teeth across it and the hole in the middle
        [Glyph.Gear] = new("M12,5.8 A6.2,6.2 0 1 0 12,18.2 A6.2,6.2 0 1 0 12,5.8 Z",
            "M12,3.8 V20.2 M3.8,12 H20.2 M6.2,6.2 L17.8,17.8 M17.8,6.2 L6.2,17.8", 3.2, "M12,12 L12.01,12", 5.6),
        [Glyph.Lines] = new(Lines: "M4.5,7 H19.5 M4.5,12 H19.5 M4.5,17 H13", Line: 2.2),
        [Glyph.Sparkle] = new("M12,3.4 C12.6,8.4 15.6,11.4 20.6,12 C15.6,12.6 12.6,15.6 12,20.6 C11.4,15.6 8.4,12.6 3.4,12 C8.4,11.4 11.4,8.4 12,3.4 Z"),
        // the island itself, as an outline
        [Glyph.Rim] = new(Lines: "M8,7.5 H16 A4.5,4.5 0 0 1 16,16.5 H8 A4.5,4.5 0 0 1 8,7.5 Z", Line: 2.2),
        [Glyph.Expand] = new(Lines: "M4.5,9.5 V4.5 H9.5 M14.5,4.5 H19.5 V9.5 M19.5,14.5 V19.5 H14.5 M9.5,19.5 H4.5 V14.5", Line: 2.2),
        [Glyph.Windows] = new("M4.6,4.6 H10.4 V10.4 H4.6 Z M13.6,4.6 H19.4 V10.4 H13.6 Z M4.6,13.6 H10.4 V19.4 H4.6 Z M13.6,13.6 H19.4 V19.4 H13.6 Z"),
        [Glyph.Power] = new(Lines: "M12,3.8 V11.4 M7.4,6.9 A7.2,7.2 0 1 0 16.6,6.9", Line: 2.2),
        // a disc, half of it filled
        [Glyph.Look] = new("M12,5 A7,7 0 0 1 12,19 Z", "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z"),
        [Glyph.Size] = new(Lines: "M6,18 L18,6 M12,5 H19 V12 M12,19 H5 V12", Line: 2.2),
        // the edge of the screen and the island under it
        [Glyph.Gap] = new(Lines: "M4,4.6 H20 M8.5,12.6 H15.5 A3.2,3.2 0 0 1 15.5,19 H8.5 A3.2,3.2 0 0 1 8.5,12.6 Z", Line: 2.2),
        [Glyph.Drop] = new("M12,4 C9.6,7.4 6.2,11 6.2,14.4 A5.8,5.8 0 0 0 17.8,14.4 C17.8,11 14.4,7.4 12,4 Z"),
        [Glyph.Bolt] = new("M13.6,3 L6.2,13.3 H11.3 L10.4,21 L17.8,10.7 H12.7 Z"),
        // a level line with one beat in it
        [Glyph.Pulse] = new(Lines: "M3,12.5 H7.6 L10.2,6 L13.8,18.5 L16.2,12.5 H21", Line: 2.2),

        // "Do not disturb": a disc with a smaller one bitten out of its top right
        [Glyph.Moon] = new("M10.58,4.44 A8.2,8.2 0 1 0 19.52,13.74 A6.8,6.8 0 0 1 10.58,4.44 Z"),
        // the shelf: an open tray, its rim dipping where things are put in
        [Glyph.Tray] = new(Lines: "M4,13.5 L6.4,6.2 A1.6,1.6 0 0 1 7.9,5.1 H16.1 A1.6,1.6 0 0 1 17.6,6.2 L20,13.5 V17.6 A2,2 0 0 1 18,19.6"
            + " H6 A2,2 0 0 1 4,17.6 Z M4,13.5 H8.6 L9.8,15.6 H14.2 L15.4,13.5 H20"),
        [Glyph.Cross] = new(Lines: "M7.5,7.5 L16.5,16.5 M16.5,7.5 L7.5,16.5", Line: 2.6),
    };

    static readonly Dictionary<Glyph, Geometry> Shapes = new();
    static readonly Dictionary<Piece, Geometry> Whole = new(); // each piece all there
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(Glyph), typeof(Icon),
        new FrameworkPropertyMetadata(Glyph.Note, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Icon)d).Turn()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Glyph Kind
    {
        get => (Glyph)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    // of an icon made of pieces: the set they are from, how far each has come (0 → 1), and where it is on its way
    // from and to, setting off at its own moment
    Piece[]? _set;
    double[] _there = [], _from = [], _to = [], _start = [];
    bool _moving;

    /// <summary>
    /// Brings the icon in piece by piece: out of nothing, or out of another icon made of the same pieces, the ones
    /// they differ in coming and going.
    /// </summary>
    /// <param name="wait">Seconds before the first of them sets off.</param>
    public void Play(Glyph? from = null, double wait = 0)
    {
        if (_set == null || !Made.TryGetValue(Kind, out var made)) return;
        int had = from is { } other && Made.TryGetValue(other, out var before) && before.Set == made.Set ? before.Has : 0;
        for (int i = 0; i < _there.Length; i++) _there[i] = had >> i & 1;
        Go(made.Has, wait);
    }

    // another icon has been asked for: one made of the same pieces is turned into, piece by piece; any other is
    // just there. Off screen there is nobody to animate for
    void Turn()
    {
        if (!Made.TryGetValue(Kind, out var made))
        {
            _set = null;
            Halt();
            return;
        }

        if (made.Set == _set && IsVisible)
        {
            Go(made.Has, 0);
            return;
        }
        _set = made.Set;
        _there = new double[_set.Length];
        _from = new double[_set.Length];
        _to = new double[_set.Length];
        _start = new double[_set.Length];
        for (int i = 0; i < _there.Length; i++) _there[i] = made.Has >> i & 1;
        Halt();
    }

    /// <summary>Sends the pieces on their way: the ones that go first, the last of them leading, then the ones that come, in their order.</summary>
    void Go(int has, double wait)
    {
        double at = Clock.Elapsed.TotalSeconds + wait;
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < _there.Length; k++)
            {
                int i = pass == 0 ? _there.Length - 1 - k : k;
                double to = has >> i & 1;
                if (to > 0 != (pass == 1)) continue;
                _from[i] = _there[i];
                _to[i] = to;
                _start[i] = at;
                if (Math.Abs(to - _there[i]) > 0.001) at += Lag;
            }
        InvalidateVisual();
        if (_moving) return;
        _moving = true;
        CompositionTarget.Rendering += OnFrame;
    }

    void Halt()
    {
        if (!_moving) return;
        _moving = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool moving = false;
        for (int i = 0; i < _there.Length; i++)
        {
            double t = Math.Clamp((now - _start[i]) / Take, 0, 1);
            _there[i] = _from[i] + (_to[i] - _from[i]) * t * t * (3 - 2 * t);
            moving |= t < 1;
        }
        InvalidateVisual();
        if (!moving) Halt();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / Grid, size / Grid));
        if (_moving && _set != null) Draw(dc, _set);
        else
        {
            if (!Shapes.TryGetValue(Kind, out Geometry? shape))
                Shapes[Kind] = shape = Made.TryGetValue(Kind, out var made) ? Build(made.Set, made.Has) : Build(Arts[Kind]);
            dc.DrawGeometry(Fill, null, shape);
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>The pieces as far as each has come: a fading one see-through, a drawn one as much of its strokes as it has got to.</summary>
    void Draw(DrawingContext dc, Piece[] set)
    {
        // from the last piece back, so that each is cut by the ones after it
        var parts = new (Geometry Shape, double Opacity)[set.Length];
        Geometry? cuts = null;
        for (int i = set.Length - 1; i >= 0; i--)
        {
            Piece piece = set[i];
            double there = _there[i];
            if (there <= 0.001) continue;

            Geometry shape = Shape(piece, piece.Drawn ? there : 1);
            if (cuts != null && !shape.IsEmpty()) shape = Join(shape, cuts, GeometryCombineMode.Exclude);
            parts[i] = (shape, piece.Drawn ? 1 : there);
            if (piece.Cut.Length == 0) continue;
            Geometry cut = Stroke(Trim(Geometry.Parse(piece.Cut), piece.Drawn ? there : 1), piece.Gap);
            cuts = cuts == null ? cut : Join(cuts, cut, GeometryCombineMode.Union);
        }

        foreach ((Geometry? shape, double opacity) in parts)
        {
            if (shape == null || shape.IsEmpty()) continue;
            dc.PushOpacity(opacity);
            dc.DrawGeometry(Fill, null, shape);
            dc.Pop();
        }
    }

    // everything ends up in one outline: a see-through brush would show where a fill and its stroke overlap
    static Geometry Build(Art art)
    {
        Geometry shape = Geometry.Empty;
        if (art.Solid.Length > 0)
        {
            Geometry solid = Geometry.Parse(art.Solid);
            shape = Join(solid, Stroke(solid, Soft), GeometryCombineMode.Union);
        }
        if (art.Lines.Length > 0) shape = Join(shape, Stroke(Geometry.Parse(art.Lines), art.Line), GeometryCombineMode.Union);
        if (art.Cut.Length > 0) shape = Join(shape, Stroke(Geometry.Parse(art.Cut), art.Gap), GeometryCombineMode.Exclude);
        if (art.Over.Length > 0) shape = Join(shape, Stroke(Geometry.Parse(art.Over), art.Line), GeometryCombineMode.Union);
        shape.Freeze();
        return shape;
    }

    /// <summary>An icon made of pieces, at rest: the ones it has, in one outline like any other.</summary>
    static Geometry Build(Piece[] set, int has)
    {
        Geometry shape = Geometry.Empty;
        for (int i = 0; i < set.Length; i++)
        {
            if ((has >> i & 1) == 0) continue;
            if (set[i].Cut.Length > 0) shape = Join(shape, Stroke(Geometry.Parse(set[i].Cut), set[i].Gap), GeometryCombineMode.Exclude);
            shape = Join(shape, Shape(set[i], 1), GeometryCombineMode.Union);
        }
        shape.Freeze();
        return shape;
    }

    /// <summary>What a piece itself draws, its strokes run as far as the given share of their length.</summary>
    static Geometry Shape(Piece piece, double share)
    {
        bool whole = share >= 0.999;
        if (whole && Whole.TryGetValue(piece, out Geometry? kept)) return kept;

        Geometry shape = Geometry.Empty;
        if (piece.Solid.Length > 0)
        {
            Geometry solid = Geometry.Parse(piece.Solid);
            shape = Join(solid, Stroke(solid, Soft), GeometryCombineMode.Union);
        }
        if (piece.Lines.Length > 0)
            shape = Join(shape, Stroke(Trim(Geometry.Parse(piece.Lines), share), piece.Line), GeometryCombineMode.Union);
        if (!whole) return shape;
        shape.Freeze();
        return Whole[piece] = shape;
    }

    /// <summary>The start of a path, as far as the given share of its length: its figures one after another.</summary>
    static Geometry Trim(Geometry path, double share)
    {
        if (share >= 0.999) return path;

        var runs = new List<List<Point>>();
        double length = 0;
        foreach (PathFigure figure in path.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute).Figures)
        {
            var run = new List<Point> { figure.StartPoint };
            foreach (PathSegment segment in figure.Segments)
                if (segment is PolyLineSegment poly) run.AddRange(poly.Points);
                else if (segment is LineSegment line) run.Add(line.Point);
            for (int i = 1; i < run.Count; i++) length += (run[i] - run[i - 1]).Length;
            runs.Add(run);
        }

        double left = length * Math.Max(share, 0);
        var start = new StreamGeometry();
        using (StreamGeometryContext g = start.Open())
            foreach (List<Point> run in runs)
            {
                if (left <= 0) break;
                g.BeginFigure(run[0], false, false);
                for (int i = 1; i < run.Count && left > 0; i++)
                {
                    Vector step = run[i] - run[i - 1];
                    g.LineTo(step.Length <= left ? run[i] : run[i - 1] + step * (left / step.Length), true, true);
                    left -= step.Length;
                }
            }
        return start;
    }

    static Geometry Stroke(Geometry path, double thickness) => path.GetWidenedPathGeometry(
        new Pen(Brushes.Black, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
        Tolerance, ToleranceType.Absolute);

    static Geometry Join(Geometry a, Geometry b, GeometryCombineMode mode) =>
        Geometry.Combine(a, b, mode, null, Tolerance, ToleranceType.Absolute);
}
