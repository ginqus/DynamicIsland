using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

sealed class Liquid
{
    const double Fill = 0.14;
    const double PressedSize = 0.82;
    const double MagnetLean = 0.35, FlowLean = 0.15;
    const double EntryShare = 0.85;
    const double LeadShare = 0.24, LeadGrowth = 0.16, SlurpedLead = 0.5;
    const double Squeeze = 0.12, FlingReach = 1.8;
    const double TailShare = 0.8, TailShrinkReach = 6;
    const double MagnetTear = 0.7, FlowTear = 1.2;
    const double Gone = 0.01;

    static readonly double[] TrailShares = [1, 0.72, 0.52];
    static readonly (double Stiffness, double Damping)[] TrailFeel = [(700, 22), (420, 20), (260, 16)];
    static readonly (double Stiffness, double Damping) Whip = (700, 22), Slurp = (600, 32), Fling = (160, 12);
    static readonly (double Stiffness, double Damping) Swell = (300, 18), Fade = (90, 14);

    sealed class Drop
    {
        public readonly Spring X, Y, Size;

        public Drop(Point at, (double Stiffness, double Damping) feel)
        {
            X = new Spring(at.X, feel.Stiffness, feel.Damping);
            Y = new Spring(at.Y, feel.Stiffness, feel.Damping);
            Size = new Spring(0, Swell.Stiffness, Swell.Damping);
        }

        public Point At => new(X.Value, Y.Value);

        public void Tune((double Stiffness, double Damping) feel)
        {
            X.Tune(feel.Stiffness, feel.Damping);
            Y.Tune(feel.Stiffness, feel.Damping);
        }

        public void Follow(Point at)
        {
            X.Target = at.X;
            Y.Target = at.Y;
        }

        public void Snap(Point at)
        {
            X.Snap(at.X);
            Y.Snap(at.Y);
            Size.Snap(0);
        }
    }

    sealed class Blob
    {
        public readonly Spring X, Y;
        public readonly Spring Size = new(0, 380, 20), Shown = new(0);
        public readonly Drop[] Drops;
        public readonly Spring[] All;
        public bool Over;
        public Point Last;

        public Blob(Point center)
        {
            X = new Spring(center.X, 260, 13);
            Y = new Spring(center.Y, 260, 13);
            Drops = [.. TrailFeel.Select(feel => new Drop(center, feel))];
            All = [X, Y, Size, Shown, .. Drops.SelectMany(drop => new[] { drop.X, drop.Y, drop.Size })];
        }

        public Point At => new(X.Value, Y.Value);
    }

    readonly Spring _headX = new(0, 520, 36), _headY = new(0, 520, 36);
    readonly Spring _tailX = new(0, 190, 20), _tailY = new(0, 190, 20);
    readonly Spring _size = new(0, 420, 28);
    readonly Spring[] _flow;
    readonly Shades _flowShades = new();
    Shades[] _shades = [];
    Blob[] _blobs = [];
    Rect[] _targets = [];
    Color[] _tints = [];
    Hover _style;

    public Liquid() => _flow = [_headX, _headY, _tailX, _tailY, _size];

    public Hover Style
    {
        get => _style;
        set
        {
            if (value == _style) return;
            _style = value;
            Rest();
        }
    }

    public void Rest()
    {
        for (int i = 0; i < _blobs.Length; i++)
        {
            Blob blob = _blobs[i];
            Point center = Center(_targets[i]);
            blob.X.Snap(center.X);
            blob.Y.Snap(center.Y);
            blob.Size.Snap(_style == Hover.Disc ? 1 : 0);
            blob.Shown.Snap(0);
            blob.Over = false;
            foreach (Drop drop in blob.Drops) drop.Snap(center);
        }
        _size.Snap(0);
    }

    public void Aim(Rect[] targets, Color[] tints, int over, Point pointer, bool pressed)
    {
        bool fresh = targets.Length != _blobs.Length;
        _targets = targets;
        _tints = tints;
        if (fresh)
        {
            _blobs = [.. targets.Select(target => new Blob(Center(target)))];
            _shades = [.. targets.Select(_ => new Shades())];
            Rest();
        }

        for (int i = 0; i < targets.Length; i++) AimBlob(_blobs[i], targets[i], i == over, pointer, pressed);
        AimFlow(over >= 0 ? targets[over] : Rect.Empty, pointer, pressed);
    }

    void AimBlob(Blob blob, Rect target, bool over, Point pointer, bool pressed)
    {
        double full = over && pressed ? PressedSize : 1;
        if (over != (blob.Shown.Target > 0)) blob.Shown.Tune(over ? 900 : 130, over ? 60 : 23);
        blob.Shown.Target = over ? 1 : 0;
        blob.Size.Tune(over && pressed ? 700 : 380, over && pressed ? 44 : 16);
        blob.Size.Target = _style == Hover.Disc || over ? full : 0;

        if (_style == Hover.Magnet) AimMagnet(blob, Center(target), Radius(target), over, pointer, pressed);
        else foreach (Drop drop in blob.Drops) drop.Size.Target = 0;
        blob.Over = over;
    }

    void AimMagnet(Blob blob, Point center, double radius, bool over, Point pointer, bool pressed)
    {
        Drop lead = blob.Drops[0];
        if (over)
        {
            if (!blob.Over && blob.Size.Value < Gone)
            {
                Point entry = center + (pointer - center) * EntryShare;
                blob.X.Snap(entry.X);
                blob.Y.Snap(entry.Y);
                foreach (Drop drop in blob.Drops) drop.Snap(pointer);
            }

            Point leaned = center + (pointer - center) * MagnetLean;
            double pull = Math.Min((pointer - center).Length / radius, 1);
            blob.X.Target = leaned.X;
            blob.Y.Target = leaned.Y;
            blob.Last = pointer;
            lead.Tune(pressed ? Slurp : Whip);
            lead.Follow(pressed ? center : pointer);
            lead.Size.Tune(Swell.Stiffness, Swell.Damping);
            lead.Size.Target = radius * (LeadShare + LeadGrowth * pull) * (pressed ? SlurpedLead : 1);
        }
        else if (blob.Over)
        {
            Vector away = blob.Last - center;
            if (away.Length < Gone) away = new Vector(0, 1);
            away.Normalize();
            blob.X.Target = center.X;
            blob.Y.Target = center.Y;
            lead.Tune(Fling);
            lead.Follow(center + away * radius * FlingReach);
            lead.Size.Tune(Fade.Stiffness, Fade.Damping);
            lead.Size.Target = 0;
        }

        for (int i = 1; i < blob.Drops.Length; i++) blob.Drops[i].Size.Target = lead.Size.Target * TrailShares[i];
    }

    void AimFlow(Rect target, Point pointer, bool pressed)
    {
        if (target.IsEmpty || _style != Hover.Flow)
        {
            _size.Target = 0;
            return;
        }

        Point center = Center(target), leaned = center + (pointer - center) * FlowLean;
        if (_size.Value < Gone)
        {
            _headX.Snap(leaned.X);
            _headY.Snap(leaned.Y);
            _tailX.Snap(leaned.X);
            _tailY.Snap(leaned.Y);
        }
        _headX.Target = _tailX.Target = leaned.X;
        _headY.Target = _tailY.Target = leaned.Y;
        _size.Target = Radius(target) * (pressed ? PressedSize : 1);
    }

    public bool Advance(double dt)
    {
        bool moving = false;
        foreach (Blob blob in _blobs)
        {
            for (int i = 1; i < blob.Drops.Length; i++) blob.Drops[i].Follow(blob.Drops[i - 1].At);
            foreach (Spring spring in blob.All) moving |= spring.Advance(dt);
        }
        foreach (Spring spring in _flow) moving |= spring.Advance(dt);
        return moving;
    }

    public void Draw(DrawingContext dc)
    {
        if (_style == Hover.Disc)
        {
            for (int i = 0; i < _blobs.Length; i++)
            {
                double shown = Math.Min(_blobs[i].Shown.Value, 1), r = Radius(_targets[i]) * _blobs[i].Size.Value;
                if (shown > Gone && r > Gone) dc.DrawEllipse(_shades[i].Of(_tints[i], Fill * shown), null, Center(_targets[i]), r, r);
            }
            return;
        }

        if (_style == Hover.Flow)
        {
            if (FlowBody() is { } flow) dc.DrawGeometry(_flowShades.Of(TintAt(_headX.Value), Fill), null, flow);
            return;
        }
        for (int i = 0; i < _blobs.Length; i++)
            if (MagnetBody(i) is { } body) dc.DrawGeometry(_shades[i].Of(_tints[i], Fill), null, body);
    }

    Geometry? MagnetBody(int i)
    {
        Geometry? body = null;
        Blob blob = _blobs[i];
        double radius = Radius(_targets[i]), tear = radius * MagnetTear;
        double reach = Math.Min((blob.Drops[0].At - blob.At).Length / radius, 1);
        double r = radius * blob.Size.Value * (1 - Squeeze * reach);
        if (r > Gone) Join(ref body, new EllipseGeometry(blob.At, r, r));

        Drop? previous = null;
        foreach (Drop drop in blob.Drops)
        {
            double dr = drop.Size.Value;
            if (dr <= Gone) continue;
            Join(ref body, new EllipseGeometry(drop.At, dr, dr));
            if (r > Gone && Goo.Neck(blob.At, r, drop.At, dr, tear) is { } stem) Join(ref body, stem);
            if (previous != null && Goo.Neck(previous.At, previous.Size.Value, drop.At, dr, tear) is { } link) Join(ref body, link);
            previous = drop;
        }
        return body;
    }

    Color TintAt(double x)
    {
        if (_tints.Length == 0) return Colors.White;
        for (int i = 1; i < _targets.Length; i++)
        {
            double from = Center(_targets[i - 1]).X, to = Center(_targets[i]).X;
            if (x >= to) continue;
            return Mix(_tints[i - 1], _tints[i], Math.Clamp((x - from) / (to - from), 0, 1));
        }
        return _tints[^1];
    }

    static Color Mix(Color a, Color b, double share) => share <= 0 ? a : share >= 1 ? b : Color.FromArgb(
        Blend(a.A, b.A, share), Blend(a.R, b.R, share), Blend(a.G, b.G, share), Blend(a.B, b.B, share));

    static byte Blend(byte a, byte b, double share) => (byte)Math.Round(a + (b - a) * share);

    Geometry? FlowBody()
    {
        double r = _size.Value;
        if (r <= Gone) return null;

        var head = new Point(_headX.Value, _headY.Value);
        var tail = new Point(_tailX.Value, _tailY.Value);
        double rt = r * TailShare / (1 + (head - tail).Length / (TailShrinkReach * r));
        Geometry? body = new EllipseGeometry(head, r, r);
        Join(ref body, new EllipseGeometry(tail, rt, rt));
        if (Goo.Neck(tail, rt, head, r, r * FlowTear) is { } neck) Join(ref body, neck);
        return body;
    }

    static void Join(ref Geometry? body, Geometry part) => body = body == null ? part : Goo.Union(body, part);

    static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    static double Radius(Rect rect) => Math.Min(rect.Width, rect.Height) / 2;
}
