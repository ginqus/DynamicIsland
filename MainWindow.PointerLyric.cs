using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public partial class MainWindow
{
    PointerLyric? _pointerLyric;
    bool _pointerLyricBroken;

    void PointerLyric_Click(object sender, RoutedEventArgs e)
    {
        int picked = PointerLyricSegments.PickUnderPointer();
        Settings.PointerLyric = picked != PointerLyricOff;
        if (Settings.PointerLyric) Settings.PointerLyricShape = (PointerLyricShape)(picked - FirstPointerLyricShape);
        UpdateSwitches(true);
        _pointerLyric?.Reshape();
        UpdatePointerLyric();
    }

    void UpdatePointerLyric()
    {
        (string text, double sung, double seconds) = PointerLyricLine();
        if ((text.Length == 0 && _pointerLyric == null) || _pointerLyricBroken) return;

        if (_pointerLyric == null)
        {
            try
            {
                _pointerLyric = new PointerLyric((FontFamily)FindResource("Display"), () => _hovered);
            }
            catch (Exception ex)
            {
                App.Log(ex);
                _pointerLyricBroken = true;
                return;
            }
            StayOnTop();
        }
        try
        {
            _pointerLyric.ShowLine(text, sung, seconds);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _pointerLyricBroken = true;
            _pointerLyric.ShowLine("", 0, 0);
        }
    }

    void StayOnTop()
    {
        Native.KeepOnTop(_hwnd);
        _glass.Place();
        if (_pointerLyric != null) Native.PutBehind(_pointerLyric.Handle, _glass.IsOn ? _glass.Handle : _hwnd);
    }

    (string Text, double Sung, double Seconds) PointerLyricLine()
    {
        if (!Settings.PointerLyric || !Settings.Lyrics || !_media.IsPlaying || _hiddenByFullscreen) return ("", 0, 0);

        LyricsService.Line[] lines = _lyrics.LinesFor(_media.Duration);
        TimeSpan at = LyricTime;
        int index = LineAt(lines, at);
        string text = index < 0 ? "" : lines[index].Text.Trim();
        if (Lyric.IsWordless(text)) return ("", 0, 0);

        TimeSpan start = lines[index].Time, end = index + 1 < lines.Length ? lines[index + 1].Time : _media.Duration;
        double seconds = Math.Clamp((end - start).TotalSeconds, MinLineFillSeconds, MaxLineFillSeconds);
        return (text, (at - start).TotalSeconds, seconds);
    }
}
