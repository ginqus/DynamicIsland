using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public enum Glyph
{
    Mute, Quiet, Mid, Loud, Headphones, Speaker, Vpn, Offline, Wifi, Wired, Bell, Note, Battery, Minus, Plus, Chevron, Back,
    Clock, Gear, Lines, Sparkle, Rim, Expand, Windows, Power, Look, Size, Gap, Drop, Moon, Tray, Cross, Bolt, VpnOff, Bars, Update,
    Segments,
}

public sealed class Icon : FrameworkElement
{
    const double GridSize = 24;
    const double CornerSoftening = 1.5;
    const double Tolerance = 0.01;
    const double PieceSeconds = 0.16;
    const double PieceStagger = 0.06;
    const double Absent = 0.001, Complete = 0.999;

    readonly record struct Art(string Solid = "", string Lines = "", double Line = 2, string Cut = "", double Gap = 2);

    readonly record struct Piece(string Solid = "", string Lines = "", double Line = 2, string Cut = "", double Gap = 2, bool Drawn = false);

    struct Stage
    {
        public double Shown, From, To, Start;
    }

    static readonly Piece[] SoundPieces =
    [
        new("M2.5,9.6 H6 L10.6,5.6 V18.4 L6,14.4 H2.5 Z"),
        new(Lines: "M13.3,9.3 A3.8,3.8 0 0 1 13.3,14.7"),
        new(Lines: "M15.7,6.9 A7.2,7.2 0 0 1 15.7,17.1"),
        new(Lines: "M18.1,4.5 A10.6,10.6 0 0 1 18.1,19.5"),
        new(Lines: "M14.8,9.2 L20.4,14.8 M20.4,9.2 L14.8,14.8", Drawn: true),
    ];

    const string Slash = "M4,3.5 L20,20.5";
    static readonly Piece[] NetworkPieces =
    [
        new("M12,17.7 A1,1 0 1 0 12,19.7 A1,1 0 1 0 12,17.7 Z"),
        new(Lines: "M8.5,15.2 A4.9,4.9 0 0 1 15.5,15.2", Line: 2.2),
        new(Lines: "M5.4,12.1 A9.3,9.3 0 0 1 18.6,12.1", Line: 2.2),
        new(Lines: "M2.3,9 A13.7,13.7 0 0 1 21.7,9", Line: 2.2),
        new(Lines: Slash, Line: 2.2, Cut: Slash, Gap: 5.4, Drawn: true),
    ];

    static readonly Piece[] ShieldPieces =
    [
        new("M12,2.8 L19.6,5.6 V11.4 C19.6,16.2 16.4,19.6 12,21.4 C7.6,19.6 4.4,16.2 4.4,11.4 V5.6 Z"),
        new(Cut: "M8.6,11.9 L11,14.3 L15.6,9.4", Drawn: true),
    ];

    static readonly Dictionary<Glyph, (Piece[] Pieces, int Mask)> Composite = new()
    {
        [Glyph.Mute] = (SoundPieces, 0b10001),
        [Glyph.Quiet] = (SoundPieces, 0b00011),
        [Glyph.Mid] = (SoundPieces, 0b00111),
        [Glyph.Loud] = (SoundPieces, 0b01111),
        [Glyph.Wifi] = (NetworkPieces, 0b01111),
        [Glyph.Offline] = (NetworkPieces, 0b11111),
        [Glyph.Vpn] = (ShieldPieces, 0b11),
        [Glyph.VpnOff] = (ShieldPieces, 0b01),
    };

    static readonly Dictionary<Glyph, Art> Arts = new()
    {
        [Glyph.Headphones] = new("M3.6,14 H7 V19.6 H3.6 Z M17,14 H20.4 V19.6 H17 Z", "M4.6,14 V12.2 A7.4,7.4 0 0 1 19.4,12.2 V14"),
        [Glyph.Speaker] = new("F0 M7.2,3.4 H16.8 V20.6 H7.2 Z M12,5.4 A2.1,2.1 0 1 0 12,9.6 A2.1,2.1 0 1 0 12,5.4 Z"
            + " M12,11 A3.9,3.9 0 1 0 12,18.8 A3.9,3.9 0 1 0 12,11 Z"),
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
        [Glyph.Clock] = new(Lines: "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,8 V12.2 L14.9,14"),
        [Glyph.Gear] = new("M12,5.8 A6.2,6.2 0 1 0 12,18.2 A6.2,6.2 0 1 0 12,5.8 Z",
            "M12,3.8 V20.2 M3.8,12 H20.2 M6.2,6.2 L17.8,17.8 M17.8,6.2 L6.2,17.8", 3.2, "M12,12 L12.01,12", 5.6),
        [Glyph.Lines] = new(Lines: "M4.5,7 H19.5 M4.5,12 H19.5 M4.5,17 H13", Line: 2.2),
        [Glyph.Sparkle] = new("M12,3.4 C12.6,8.4 15.6,11.4 20.6,12 C15.6,12.6 12.6,15.6 12,20.6 C11.4,15.6 8.4,12.6 3.4,12 C8.4,11.4 11.4,8.4 12,3.4 Z"),
        [Glyph.Rim] = new(Lines: "M8,7.5 H16 A4.5,4.5 0 0 1 16,16.5 H8 A4.5,4.5 0 0 1 8,7.5 Z", Line: 2.2),
        [Glyph.Expand] = new(Lines: "M4.5,9.5 V4.5 H9.5 M14.5,4.5 H19.5 V9.5 M19.5,14.5 V19.5 H14.5 M9.5,19.5 H4.5 V14.5", Line: 2.2),
        [Glyph.Windows] = new("M4.6,4.6 H10.4 V10.4 H4.6 Z M13.6,4.6 H19.4 V10.4 H13.6 Z M4.6,13.6 H10.4 V19.4 H4.6 Z M13.6,13.6 H19.4 V19.4 H13.6 Z"),
        [Glyph.Update] = new(Lines: "M12,4.2 V14.6 M7.6,10.4 L12,14.8 L16.4,10.4 M5.2,19.4 H18.8", Line: 2.2),
        [Glyph.Power] = new(Lines: "M12,3.8 V11.4 M7.4,6.9 A7.2,7.2 0 1 0 16.6,6.9", Line: 2.2),
        [Glyph.Look] = new("M12,5 A7,7 0 0 1 12,19 Z", "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z"),
        [Glyph.Size] = new(Lines: "M6,18 L18,6 M12,5 H19 V12 M12,19 H5 V12", Line: 2.2),
        [Glyph.Gap] = new(Lines: "M4,4.6 H20 M8.5,12.6 H15.5 A3.2,3.2 0 0 1 15.5,19 H8.5 A3.2,3.2 0 0 1 8.5,12.6 Z", Line: 2.2),
        [Glyph.Bars] = new(Lines: "M5.5,10 V14 M12,5.5 V18.5 M18.5,8.5 V15.5", Line: 2.6),
        [Glyph.Segments] = new("M14.4,9.4 A2.6,2.6 0 1 0 14.4,14.6 A2.6,2.6 0 1 0 14.4,9.4 Z", "M4.2,12 H7.6 M10.4,12 H11.4 M18.4,12 H19.8", 2.4),
        [Glyph.Drop] = new("M12,4 C9.6,7.4 6.2,11 6.2,14.4 A5.8,5.8 0 0 0 17.8,14.4 C17.8,11 14.4,7.4 12,4 Z"),
        [Glyph.Bolt] = new("M13.6,3 L6.2,13.3 H11.3 L10.4,21 L17.8,10.7 H12.7 Z"),
        [Glyph.Moon] = new("M10.58,4.44 A8.2,8.2 0 1 0 19.52,13.74 A6.8,6.8 0 0 1 10.58,4.44 Z"),
        [Glyph.Tray] = new(Lines: "M4,13.5 L6.4,6.2 A1.6,1.6 0 0 1 7.9,5.1 H16.1 A1.6,1.6 0 0 1 17.6,6.2 L20,13.5 V17.6 A2,2 0 0 1 18,19.6"
            + " H6 A2,2 0 0 1 4,17.6 Z M4,13.5 H8.6 L9.8,15.6 H14.2 L15.4,13.5 H20"),
        [Glyph.Cross] = new(Lines: "M7.5,7.5 L16.5,16.5 M16.5,7.5 L7.5,16.5", Line: 2.6),
    };

    static readonly Dictionary<Glyph, Geometry> Outlines = [];
    static readonly Dictionary<Piece, Geometry> PieceOutlines = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(Glyph), typeof(Icon),
        new FrameworkPropertyMetadata(Glyph.Note, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Icon)d).OnKindChanged()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly FrameLoop _loop;
    Piece[]? _pieces;
    Stage[] _stages = [];

    public Icon() => _loop = new FrameLoop(_ => Advance());

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

    public void Play(Glyph? from, double wait)
    {
        if (_pieces == null || !Composite.TryGetValue(Kind, out var composite)) return;
        int had = from is { } other && Composite.TryGetValue(other, out var before) && before.Pieces == composite.Pieces ? before.Mask : 0;
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(had, i);
        Animate(composite.Mask, wait);
    }

    void OnKindChanged()
    {
        if (!Composite.TryGetValue(Kind, out var composite))
        {
            _pieces = null;
            _loop.Stop();
            return;
        }

        if (composite.Pieces == _pieces && IsVisible)
        {
            Animate(composite.Mask, 0);
            return;
        }
        _pieces = composite.Pieces;
        _stages = new Stage[_pieces.Length];
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(composite.Mask, i);
        _loop.Stop();
    }

    void Animate(int mask, double wait)
    {
        double at = Clock.Elapsed.TotalSeconds + wait;
        for (int i = _stages.Length - 1; i >= 0; i--)
            if (Bit(mask, i) == 0) at = Schedule(ref _stages[i], 0, at);
        for (int i = 0; i < _stages.Length; i++)
            if (Bit(mask, i) != 0) at = Schedule(ref _stages[i], 1, at);
        InvalidateVisual();
        _loop.Start();
    }

    static double Schedule(ref Stage stage, double to, double at)
    {
        stage.From = stage.Shown;
        stage.To = to;
        stage.Start = at;
        return Math.Abs(to - stage.Shown) > Absent ? at + PieceStagger : at;
    }

    bool Advance()
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool moving = false;
        for (int i = 0; i < _stages.Length; i++)
        {
            ref Stage stage = ref _stages[i];
            double t = Math.Clamp((now - stage.Start) / PieceSeconds, 0, 1);
            stage.Shown = stage.From + (stage.To - stage.From) * t * t * (3 - 2 * t);
            moving |= t < 1;
        }
        InvalidateVisual();
        return moving;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / GridSize, size / GridSize));
        if (_loop.Running && _pieces != null) DrawPieces(dc, _pieces);
        else dc.DrawGeometry(Fill, null, Outline(Kind));
        dc.Pop();
        dc.Pop();
    }

    void DrawPieces(DrawingContext dc, Piece[] pieces)
    {
        var parts = new (Geometry? Shape, double Opacity)[pieces.Length];
        Geometry? cuts = null;
        for (int i = pieces.Length - 1; i >= 0; i--)
        {
            Piece piece = pieces[i];
            double shown = _stages[i].Shown;
            if (shown <= Absent) continue;

            double share = piece.Drawn ? shown : 1;
            Geometry shape = PieceOutline(piece, share);
            if (cuts != null && !shape.IsEmpty()) shape = Exclude(shape, cuts);
            parts[i] = (shape, piece.Drawn ? 1 : shown);
            if (piece.Cut.Length == 0) continue;
            Geometry cut = Stroke(Trim(Geometry.Parse(piece.Cut), share), piece.Gap);
            cuts = cuts == null ? cut : Union(cuts, cut);
        }

        foreach ((Geometry? shape, double opacity) in parts)
        {
            if (shape == null || shape.IsEmpty()) continue;
            dc.PushOpacity(opacity);
            dc.DrawGeometry(Fill, null, shape);
            dc.Pop();
        }
    }

    static int Bit(int mask, int index) => mask >> index & 1;

    static Geometry Outline(Glyph kind)
    {
        if (Outlines.TryGetValue(kind, out Geometry? outline)) return outline;
        outline = Composite.TryGetValue(kind, out var composite) ? Outline(composite.Pieces, composite.Mask) : Outline(Arts[kind]);
        return Outlines[kind] = outline;
    }

    static Geometry Outline(Art art)
    {
        Geometry shape = BuildShape(art.Solid, art.Lines, art.Line, 1);
        if (art.Cut.Length > 0) shape = Exclude(shape, Stroke(Geometry.Parse(art.Cut), art.Gap));
        shape.Freeze();
        return shape;
    }

    static Geometry Outline(Piece[] pieces, int mask)
    {
        Geometry shape = Geometry.Empty;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (Bit(mask, i) == 0) continue;
            if (pieces[i].Cut.Length > 0) shape = Exclude(shape, Stroke(Geometry.Parse(pieces[i].Cut), pieces[i].Gap));
            shape = Union(shape, PieceOutline(pieces[i], 1));
        }
        shape.Freeze();
        return shape;
    }

    static Geometry PieceOutline(Piece piece, double share)
    {
        bool complete = share >= Complete;
        if (complete && PieceOutlines.TryGetValue(piece, out Geometry? kept)) return kept;

        Geometry shape = BuildShape(piece.Solid, piece.Lines, piece.Line, share);
        if (!complete) return shape;
        shape.Freeze();
        return PieceOutlines[piece] = shape;
    }

    static Geometry BuildShape(string solid, string lines, double thickness, double share)
    {
        Geometry shape = Geometry.Empty;
        if (solid.Length > 0)
        {
            Geometry filled = Geometry.Parse(solid);
            shape = Union(filled, Stroke(filled, CornerSoftening));
        }
        if (lines.Length > 0) shape = Union(shape, Stroke(Trim(Geometry.Parse(lines), share), thickness));
        return shape;
    }

    static Geometry Trim(Geometry path, double share)
    {
        if (share >= Complete) return path;

        var runs = new List<List<Point>>();
        double length = 0;
        foreach (PathFigure figure in path.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute).Figures)
        {
            List<Point> run = figure.Points().ToList();
            for (int i = 1; i < run.Count; i++) length += (run[i] - run[i - 1]).Length;
            runs.Add(run);
        }

        double left = length * Math.Max(share, 0);
        var start = new StreamGeometry();
        using (StreamGeometryContext g = start.Open())
        {
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
        }
        return start;
    }

    static Geometry Stroke(Geometry path, double thickness) => path.GetWidenedPathGeometry(
        new Pen(Brushes.Black, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
        Tolerance, ToleranceType.Absolute);

    static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);

    static Geometry Exclude(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);
}
