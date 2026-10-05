using System.Windows.Threading;

namespace DynamicIsland;

sealed class DelayedAction
{
    readonly DispatcherTimer _timer = new();

    public DelayedAction(Action action) => _timer.Tick += (_, _) =>
    {
        _timer.Stop();
        action();
    };

    public void Start(TimeSpan delay)
    {
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    public void Cancel() => _timer.Stop();
}
