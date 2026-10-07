using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class StarField : FrameworkElement
{
    const int BassBands = 3;
    const double WakeSeconds = 0.8, SleepSeconds = 1.2;
    const double StillBelow = 0.002;

    const int StarCount = 44, StarSeed = 7;
    const double StarMargin = 10, StarTop = 6, FloorOverMatrix = 40, FloorOverEdge = 16, SkyBias = 1.4;
    const double SmallestStar = 0.45, StarSpread = 0.65;
    const double DimmestStar = 0.2, StarBrightening = 0.4;
    const double SlowestTwinkle = 0.7, TwinkleSpread = 1.7, TwinkleDepth = 0.5;
    const double StarCalm = 0.45, StarLevelGain = 0.75;
    const double HaloFrom = 0.55, HaloReach = 4, HaloGain = 0.8, HaloCore = 0.6;

    const double OnsetJump = 0.22, BassAverageSeconds = 0.5, MeteorWakefulness = 0.5;
    const double ShortestMeteorRest = 6, LongestMeteorRest = 14;
    const double MeteorSeconds = 0.6, MeteorSpeed = 260, MeteorTail = 46, MeteorSlope = 0.35;
    const double MeteorWidth = 1.2, MeteorHeadRadius = 1.1, MeteorSkyShare = 0.35, MeteorStartSpread = 0.6;

    readonly record struct Star(double X, double Y, double Radius, double Glow, int Band, double Speed, double Phase);

    const int HaloLevels = 255;

    static readonly Brush?[] Halos = new Brush?[HaloLevels + 1];
    static readonly Star[] Stars = Scatter();

    readonly Random _random = new();
    SpectrumLevels? _levels;
    bool _aboveMatrix = true;
    double _time, _activity, _bassAverage;
    double _meteorRest = ShortestMeteorRest, _meteorAge = -1;
    Point _meteorStart;
    Vector _meteorHeading;

    public bool AboveMatrix
    {
        get => _aboveMatrix;
        set
        {
            if (value == _aboveMatrix) return;
            _aboveMatrix = value;
            InvalidateVisual();
        }
    }

    double Floor => _aboveMatrix ? FloorOverMatrix : FloorOverEdge;

    public bool Tick(SpectrumLevels levels, bool playing, double t, double dt)
    {
        _levels = levels;
        bool moved = levels.Moved;

        _activity += ((playing ? 1 : 0) - _activity) * (1 - Math.Exp(-dt / (playing ? WakeSeconds : SleepSeconds)));
        if (_activity < StillBelow) _activity = 0;
        if (_activity > 0) moved = true;

        moved |= AdvanceMeteor(levels.Average(BassBands), dt);
        _time = t;
        if (moved) InvalidateVisual();
        return moved;
    }

    bool AdvanceMeteor(double bass, double dt)
    {
        bool onset = bass - _bassAverage > OnsetJump;
        _bassAverage += (bass - _bassAverage) * (1 - Math.Exp(-dt / BassAverageSeconds));
        _meteorRest -= dt;
        if (_meteorAge < 0 && onset && _meteorRest <= 0 && _activity > MeteorWakefulness) Launch();
        if (_meteorAge < 0) return false;
        _meteorAge += dt;
        if (_meteorAge >= MeteorSeconds) _meteorAge = -1;
        return true;
    }

    void Launch()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        _meteorAge = 0;
        _meteorRest = ShortestMeteorRest + _random.NextDouble() * (LongestMeteorRest - ShortestMeteorRest);
        double x = w * (0.5 + MeteorStartSpread * (_random.NextDouble() - 0.5));
        double y = StarTop + (h - Floor - StarTop) * MeteorSkyShare * _random.NextDouble();
        _meteorStart = new Point(x, y);
        _meteorHeading = new Vector(x < w / 2 ? 1 : -1, MeteorSlope);
        _meteorHeading.Normalize();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        DrawStars(dc, w, h);
        DrawMeteor(dc);
    }

    void DrawStars(DrawingContext dc, double w, double h)
    {
        double width = w - 2 * StarMargin, height = h - Floor - StarTop;
        foreach (Star star in Stars)
        {
            double twinkle = 0.5 + 0.5 * Math.Sin(_time * star.Speed + star.Phase);
            double level = _levels?[star.Band] ?? 0;
            double flare = Math.Clamp((level - StarCalm) / (1 - StarCalm), 0, 1);
            double glow = Math.Min(star.Glow * (1 - TwinkleDepth * _activity * twinkle) + StarLevelGain * flare, 1);
            var center = new Point(StarMargin + width * star.X, StarTop + height * star.Y);

            dc.DrawEllipse(Shades.White(glow), null, center, star.Radius, star.Radius);

            double halo = (glow - HaloFrom) / (1 - HaloFrom);
            if (halo <= 0) continue;
            dc.DrawEllipse(Halo(halo * HaloGain), null, center, star.Radius * HaloReach, star.Radius * HaloReach);
        }
    }

    static Brush Halo(double opacity)
    {
        int level = (int)Math.Round(Math.Clamp(opacity, 0, 1) * HaloLevels);
        return Halos[level] ??= Shades.Frozen(new RadialGradientBrush(Colors.White.WithAlpha(HaloCore), Colors.White.WithAlpha(0))
        {
            Opacity = (double)level / HaloLevels,
        });
    }

    void DrawMeteor(DrawingContext dc)
    {
        if (_meteorAge < 0) return;
        double fade = Math.Sin(Math.PI * _meteorAge / MeteorSeconds);
        Point head = _meteorStart + _meteorHeading * MeteorSpeed * _meteorAge;
        Point tail = head - _meteorHeading * MeteorTail * (0.4 + 0.6 * fade);

        var trail = new LinearGradientBrush(Colors.White.WithAlpha(0), Colors.White.WithAlpha(fade), tail, head)
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        var pen = new Pen(trail, MeteorWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawLine(pen, tail, head);

        dc.DrawEllipse(Shades.White(fade), null, head, MeteorHeadRadius, MeteorHeadRadius);
    }

    static Star[] Scatter()
    {
        var random = new Random(StarSeed);
        var stars = new Star[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            double size = random.NextDouble();
            stars[i] = new Star(
                random.NextDouble(),
                Math.Pow(random.NextDouble(), SkyBias),
                SmallestStar + StarSpread * size * size * size,
                DimmestStar + StarBrightening * size,
                BassBands + random.Next(SpectrumLevels.Bands - BassBands),
                SlowestTwinkle + TwinkleSpread * random.NextDouble(),
                2 * Math.PI * random.NextDouble());
        }
        return stars;
    }
}
