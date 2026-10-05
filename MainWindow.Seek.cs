using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double SeekTrackWidth = 260;
    const double SeekHeight = 6, SeekHoverHeight = 9, SeekDragHeight = 12, MinSeekHeight = 2;
    const double SeekSettledFraction = 0.02;
    const double SeekHoldSeconds = 1;
    const int NoSeconds = -1;
    const string NoTime = "–:––";

    readonly Spring _seekFill = new(0, 170, 26), _seekHeight = new(SeekHeight, 420, 26);
    readonly FrameLoop _seekLoop;
    bool _scrubbing;
    double _scrubFraction, _scrubHeldUntil;
    (int At, int Total) _seekLabel = (NoSeconds, NoSeconds);
    LyricsService.Line[] _seekLinesFrom = [];
    TimeSpan _seekLinesSpan;
    bool _seekLined;

    void StartSeekLoop()
    {
        _seekFill.Value = _seekFill.Velocity = 0;
        _seekLoop.Start();
    }

    bool AdvanceSeek(double dt)
    {
        if (_view != View.MediaBig && !_scrubbing) return false;

        double now = _clock.Elapsed.TotalSeconds;
        TimeSpan duration = _media.Duration;
        bool known = duration.TotalSeconds >= 1;
        bool counted = known || _media.HasBarPosition;
        double played = _media.Progress;
        if (!_scrubbing && now < _scrubHeldUntil && Math.Abs(played - _scrubFraction) < SeekSettledFraction) _scrubHeldUntil = 0;
        double shown = _scrubbing || now < _scrubHeldUntil ? _scrubFraction : played;

        double track = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : SeekTrackWidth;
        _seekFill.Target = track * shown;
        _seekHeight.Target = _scrubbing ? SeekDragHeight : SeekArea.IsMouseOver ? SeekHoverHeight : SeekHeight;
        _seekFill.Advance(dt);
        _seekHeight.Advance(dt);

        double thick = Math.Max(_seekHeight.Value, MinSeekHeight);
        SeekBar.Height = thick;
        SeekBack.CornerRadius = SeekFill.CornerRadius = new CornerRadius(thick / 2);
        SeekFill.Width = SeekLines.Fill = Math.Clamp(_seekFill.Value, 0, track);
        MarkLineStarts(duration);
        FillSungLine();
        AdvancePlayerLyrics(now, dt);

        TimeSpan at = known ? duration * shown : _media.Position;
        var label = counted ? ((int)at.TotalSeconds, known ? (int)duration.TotalSeconds : NoSeconds) : (NoSeconds, NoSeconds);
        if (label == _seekLabel) return true;

        _seekLabel = label;
        PosText.Text = counted ? FormatTime(at) : NoTime;
        RemText.Text = known ? "-" + FormatTime(duration - at) : NoTime;
        return true;
    }

    void MarkLineStarts(TimeSpan duration)
    {
        if (ReferenceEquals(_playerLines, _seekLinesFrom) && duration == _seekLinesSpan) return;

        _seekLinesFrom = _playerLines;
        _seekLinesSpan = duration;
        double seconds = duration.TotalSeconds;
        SeekLines.SetStarts(seconds < 1 ? [] : _playerLines.Select(line => line.Time.TotalSeconds / seconds).ToArray());
        SyncSeekStyle(true);
    }

    void SyncSeekStyle(bool animate)
    {
        bool lined = Settings.LineBar && SeekLines.HasMarks;
        if (lined == _seekLined) return;

        _seekLined = lined;
        Duration time = Ms(animate ? 260 : 0);
        SeekLines.BeginAnimation(OpacityProperty, new DoubleAnimation(lined ? 1 : 0, time));
        foreach (UIElement plain in new UIElement[] { SeekBack, SeekFill })
            plain.BeginAnimation(OpacityProperty, new DoubleAnimation(lined ? 0 : 1, time));
    }

    static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    double SeekFraction(MouseEventArgs e)
    {
        double x = e.GetPosition(SeekArea).X;
        if (_seekLined) x = SeekLines.Snap(x);
        return Math.Clamp(x / SeekArea.ActualWidth, 0, 1);
    }

    void Seek_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SeekArea.ActualWidth <= 0 || !_media.Seekable) return;

        _scrubbing = true;
        _scrubFraction = SeekFraction(e);
        _seekFill.Tune(900, 60);
        PosText.Foreground = RemText.Foreground = Brushes.White;
        SeekArea.CaptureMouse();
    }

    void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) _scrubFraction = SeekFraction(e);
    }

    void Seek_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_scrubbing) return;

        EndScrub();
        _scrubHeldUntil = _clock.Elapsed.TotalSeconds + SeekHoldSeconds;
        _media.Seek(_scrubFraction);
        SeekArea.ReleaseMouseCapture();
    }

    void Seek_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scrubbing) EndScrub();
    }

    void EndScrub()
    {
        _scrubbing = false;
        _seekFill.Tune(170, 26);
        PosText.Foreground = RemText.Foreground = _dim;
    }
}
