namespace DynamicIsland;

sealed class Countdown
{
    TimeSpan _remaining;
    DateTime _resumedAt;

    public TimeSpan Total { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsRunning { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            TimeSpan left = IsRunning ? _remaining - (DateTime.UtcNow - _resumedAt) : _remaining;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public double RemainingShare => Total > TimeSpan.Zero ? Remaining / Total : 0;

    public void Start(TimeSpan total)
    {
        Total = _remaining = total;
        _resumedAt = DateTime.UtcNow;
        IsActive = IsRunning = true;
    }

    public void Toggle()
    {
        if (!IsActive) return;
        if (IsRunning) _remaining = Remaining;
        else _resumedAt = DateTime.UtcNow;
        IsRunning = !IsRunning;
    }

    public void Stop()
    {
        IsActive = IsRunning = false;
        _remaining = TimeSpan.Zero;
    }
}
