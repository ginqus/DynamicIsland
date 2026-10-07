namespace DynamicIsland;

public sealed class SpectrumLevels
{
    public const int Bands = 16;
    const double AttackSeconds = 0.03, ReleaseSeconds = 0.17;
    const double StillBelow = 0.002;

    readonly BandMeter _meter = new(Bands);
    readonly double[] _levels = new double[Bands], _targets = new double[Bands];

    public bool Moved { get; private set; }

    public double this[int band] => _levels[band];

    public double At(double share)
    {
        double at = Math.Clamp(share, 0, 1) * (Bands - 1);
        int low = Math.Min((int)at, Bands - 2);
        return _levels[low] + (_levels[low + 1] - _levels[low]) * (at - low);
    }

    public double Average(int lowest)
    {
        double sum = 0;
        for (int i = 0; i < lowest; i++) sum += _levels[i];
        return sum / lowest;
    }

    public void Follow(float[]? spectrum, Equalizer fallback, bool playing, double dt)
    {
        if (!playing) Array.Clear(_targets);
        else if (spectrum != null)
        {
            _meter.Measure(spectrum, dt);
            Array.Copy(_meter.Heights, _targets, Bands);
        }
        else FollowEqualizer(fallback);

        double rise = 1 - Math.Exp(-dt / AttackSeconds), fall = 1 - Math.Exp(-dt / ReleaseSeconds);
        Moved = false;
        for (int i = 0; i < Bands; i++)
        {
            double next = _levels[i] + (_targets[i] - _levels[i]) * (_targets[i] > _levels[i] ? rise : fall);
            if (Math.Abs(next - _levels[i]) > StillBelow) Moved = true;
            _levels[i] = next;
        }
    }

    void FollowEqualizer(Equalizer source)
    {
        int last = source.Bars - 1;
        for (int i = 0; i < Bands; i++)
        {
            double at = (double)i * last / (Bands - 1);
            int low = Math.Min((int)at, last - 1);
            _targets[i] = source.Level(low) + (source.Level(low + 1) - source.Level(low)) * (at - low);
        }
    }
}
