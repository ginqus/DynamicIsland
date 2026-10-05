using System.Diagnostics;
using System.Windows.Media;

namespace DynamicIsland;

sealed class FrameLoop
{
    const double LongestStep = 0.05;

    readonly Func<double, bool> _advance;
    readonly double _shortestStep;
    long _last;

    public FrameLoop(Func<double, bool> advance, double shortestStep = 0)
    {
        _advance = advance;
        _shortestStep = shortestStep;
    }

    public bool Running { get; private set; }

    public void Start()
    {
        if (Running) return;
        Running = true;
        _last = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = Stopwatch.GetElapsedTime(_last, now).TotalSeconds;
        if (elapsed <= 0 || elapsed < _shortestStep) return;

        _last = now;
        if (!_advance(Math.Min(elapsed, LongestStep))) Stop();
    }
}
