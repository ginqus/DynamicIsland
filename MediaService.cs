using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIsland;

/// <summary>Now-playing info from whatever app owns the system media session (Spotify, browser, ...).</summary>
sealed class MediaService
{
    static readonly Color[] Plain = [Colors.White];
    const double Turn = 28; // degrees to either side on the colour wheel, for the neighbours of a lone hue

    readonly Dispatcher _ui;
    Manager? _manager;
    Session? _session;
    Session? _chosen; // the app the island was turned to by hand: it stays on show until another one starts to play
    int _version;

    TimeSpan _position, _duration;
    DateTime _positionAt = DateTime.UtcNow;
    DateTimeOffset _timelineStamp;
    double _rate = 1;

    public MediaService(Dispatcher ui) => _ui = ui;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public ImageSource? Art { get; private set; }
    /// <summary>Colours of the cover; the first stands for the whole of it.</summary>
    public Color[] Palette { get; private set; } = Plain;
    public Color Accent => Palette[0];
    public bool IsPlaying { get; private set; }
    public bool HasTrack => _session != null && Title.Length > 0;
    public TimeSpan Duration => _duration;

    /// <summary>Id of the app that plays, as the session gives it; empty when there is none.</summary>
    public string Source
    {
        get
        {
            try { return _session?.SourceAppUserModelId ?? ""; }
            catch { return ""; }
        }
    }

    public TimeSpan Position
    {
        get
        {
            TimeSpan p = _position;
            if (IsPlaying) p += (DateTime.UtcNow - _positionAt) * _rate;
            if (p < TimeSpan.Zero) return TimeSpan.Zero;
            return p > _duration ? _duration : p;
        }
    }

    /// <summary>Raised on the UI thread.</summary>
    public event Action? Changed;

    public async Task StartAsync()
    {
        _manager = await Manager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(() =>
        {
            Yield();
            Attach();
        });
        _manager.SessionsChanged += (_, _) => _ui.InvokeAsync(Attach);
        Attach();
    }

    /// <summary>Turns to the next app with a media session, or the previous one; they go round in a circle.</summary>
    /// <returns>False when there is no other app to turn to.</returns>
    public bool Switch(int direction)
    {
        try
        {
            var sessions = _manager?.GetSessions();
            if (sessions == null || sessions.Count < 2) return false;

            int count = sessions.Count, at = -1;
            for (int i = 0; i < count && at < 0; i++)
                if (ReferenceEquals(sessions[i], _session)) at = i;
            for (int i = 0; i < count && at < 0; i++)
                if (Same(sessions[i], _session)) at = i;

            // nothing on show yet: start from either end
            _chosen = sessions[at < 0 ? (direction > 0 ? 0 : count - 1) : ((at + direction) % count + count) % count];
            Attach();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // an app that starts to play takes the island over, as it always did, whatever it had been turned to
    void Yield()
    {
        try
        {
            Session? current = _manager?.GetCurrentSession();
            if (current != null && !Same(current, _chosen) && Playing(current)) _chosen = null;
        }
        catch { }
    }

    static bool Same(Session? a, Session? b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        try { return a.SourceAppUserModelId == b.SourceAppUserModelId; }
        catch { return false; }
    }

    void Attach()
    {
        if (_session != null)
        {
            try
            {
                _session.MediaPropertiesChanged -= OnProperties;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }

        _session = Pick();
        _timelineStamp = default;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnProperties;
            _session.PlaybackInfoChanged += OnPlayback;
            _session.TimelinePropertiesChanged += OnTimeline;
        }
        _ = RefreshAsync();
    }

    Session? Pick()
    {
        try
        {
            if (_chosen != null)
            {
                foreach (Session s in _manager!.GetSessions())
                    if (Same(s, _chosen)) return s;
                // its app is gone
                _chosen = null;
            }

            Session? current = _manager?.GetCurrentSession();
            if (current != null && Playing(current)) return current;
            foreach (Session s in _manager!.GetSessions())
                if (Playing(s)) return s;
            return current;
        }
        catch
        {
            return null;
        }
    }

    static bool Playing(Session s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == Status.Playing; }
        catch { return false; }
    }

    void OnProperties(Session s, MediaPropertiesChangedEventArgs e) => _ui.InvokeAsync(() => _ = RefreshAsync());

    void OnPlayback(Session s, PlaybackInfoChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        ReadPlayback();
        ReadTimeline();
        Changed?.Invoke();
    });

    void OnTimeline(Session s, TimelinePropertiesChangedEventArgs e) => _ui.InvokeAsync(() =>
    {
        ReadTimeline();
        Changed?.Invoke();
    });

    async Task RefreshAsync()
    {
        Session? session = _session;
        int version = ++_version;

        if (session == null)
        {
            Title = Artist = "";
            Art = null;
            Palette = Plain;
            IsPlaying = false;
            Changed?.Invoke();
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _version) return;

            string title = props.Title ?? "";
            bool sameTrack = title == Title;
            ImageSource? art = null;
            Color[] palette = Plain;

            if (props.Thumbnail != null)
            {
                try
                {
                    using var source = await props.Thumbnail.OpenReadAsync();
                    using var stream = source.AsStreamForRead();
                    var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer);
                    buffer.Position = 0;
                    // off the UI thread: the old cover is sliding out meanwhile, and must not stutter
                    (art, palette) = await Task.Run(() => Decode(buffer));
                }
                catch { }
                if (version != _version) return;
            }

            Title = title;
            Artist = props.Artist ?? "";
            // browsers briefly drop the thumbnail while updating metadata — keep the old one
            if (art != null || !sameTrack)
            {
                Art = art;
                Palette = palette;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        ReadPlayback();
        ReadTimeline();
        Changed?.Invoke();
    }

    void ReadPlayback()
    {
        if (_session == null) return;
        try
        {
            var info = _session.GetPlaybackInfo();
            bool playing = info.PlaybackStatus == Status.Playing;
            if (playing != IsPlaying)
            {
                // freeze / resume our own clock: not every app pushes a timeline update here
                _position = Position;
                _positionAt = DateTime.UtcNow;
                IsPlaying = playing;
            }
            _rate = info.PlaybackRate ?? 1;
        }
        catch { }
    }

    void ReadTimeline()
    {
        if (_session == null) return;
        try
        {
            var t = _session.GetTimelineProperties();
            _duration = t.EndTime - t.StartTime;
            if (t.LastUpdatedTime == _timelineStamp) return;

            _timelineStamp = t.LastUpdatedTime;
            _position = t.Position - t.StartTime;
            DateTime at = t.LastUpdatedTime.UtcDateTime;
            _positionAt = at.Year < 2000 ? DateTime.UtcNow : at;
        }
        catch { }
    }

    public async void TogglePlay()
    {
        try { if (_session != null) await _session.TryTogglePlayPauseAsync(); }
        catch { }
    }

    public async void Next()
    {
        try { if (_session != null) await _session.TrySkipNextAsync(); }
        catch { }
    }

    public async void Previous()
    {
        try { if (_session != null) await _session.TrySkipPreviousAsync(); }
        catch { }
    }

    public async void Seek(double fraction)
    {
        Session? session = _session;
        if (session == null || _duration <= TimeSpan.Zero) return;
        try
        {
            var target = TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(fraction, 0, 1)));
            TimeSpan start = session.GetTimelineProperties().StartTime;
            if (await session.TryChangePlaybackPositionAsync((start + target).Ticks))
            {
                _position = target;
                _positionAt = DateTime.UtcNow;
                Changed?.Invoke();
            }
        }
        catch { }
    }

    static (ImageSource, Color[]) Decode(MemoryStream data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 192;
        bitmap.StreamSource = data;
        bitmap.EndInit();
        bitmap.Freeze();
        return (bitmap, PaletteOf(bitmap));
    }

    /// <summary>
    /// Up to three colours of the cover, lifted so they read on black: the saturation-weighted average of the
    /// whole of it, then the hues that stand out in it. A cover of one hue is filled up with that hue's neighbours.
    /// </summary>
    static Color[] PaletteOf(BitmapSource source)
    {
        const int Slices = 12, Wanted = 3; // of the colour wheel; colours in the palette
        const double Share = 0.08;         // of the cover's weight a hue needs to count
        const double Apart = 64;           // ...and how far it has to be from the others, as a distance in RGB
        try
        {
            var small = new TransformedBitmap(source,
                new ScaleTransform(24.0 / source.PixelWidth, 24.0 / source.PixelHeight));
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);

            // weighted sums of red, green and blue, and the weight: per slice of the wheel, the whole cover last
            var sums = new double[Slices + 1, 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                double r = px[i + 2], g = px[i + 1], b = px[i];
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                double sat = max == 0 ? 0 : (max - min) / max;
                double weight = sat * sat * (max / 255) + 0.01;
                foreach (int row in new[] { Slice(r, g, b), Slices })
                {
                    sums[row, 0] += r * weight;
                    sums[row, 1] += g * weight;
                    sums[row, 2] += b * weight;
                    sums[row, 3] += weight;
                }
            }

            int Slice(double r, double g, double b) => (int)(Hue(r, g, b) / 360 * Slices) % Slices;
            Color? Mean(int row) => Lift(sums[row, 0] / sums[row, 3], sums[row, 1] / sums[row, 3], sums[row, 2] / sums[row, 3]);

            if (Mean(Slices) is not { } accent) return Plain;
            var palette = new List<Color> { accent };
            // the heaviest hues first, each far enough from the colours already in to be told from them
            foreach (int slice in Enumerable.Range(0, Slices).OrderByDescending(s => sums[s, 3]))
            {
                if (palette.Count == Wanted || sums[slice, 3] < Share * sums[Slices, 3]) break;
                if (Mean(slice) is not { } colour) continue;
                if (palette.Any(c => Math.Sqrt(Math.Pow(c.R - colour.R, 2) + Math.Pow(c.G - colour.G, 2) + Math.Pow(c.B - colour.B, 2)) < Apart)) continue;
                palette.Add(colour);
            }
            for (double turn = Turn; palette.Count < Wanted; turn = -turn) palette.Add(Turned(accent, turn));
            return palette.ToArray();
        }
        catch
        {
            return Plain;
        }
    }

    /// <summary>The palette of a colour picked by hand instead of a cover: the colour and its neighbours on the wheel.</summary>
    public static Color[] Around(Color colour) => [colour, Turned(colour, Turn), Turned(colour, -Turn)];

    /// <summary>Brightens a colour to read on black, then pulls it a little towards white to keep it from going fully neon.</summary>
    static Color? Lift(double r, double g, double b)
    {
        double peak = Math.Max(r, Math.Max(g, b));
        if (!(peak >= 1)) return null;
        double lift = 235 / peak;
        r *= lift; g *= lift; b *= lift;

        const double white = 0.18;
        return Color.FromRgb(
            (byte)(r + (255 - r) * white),
            (byte)(g + (255 - g) * white),
            (byte)(b + (255 - b) * white));
    }

    /// <summary>Where a colour sits on the wheel, in degrees; greys sit at 0.</summary>
    static double Hue(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), span = max - Math.Min(r, Math.Min(g, b));
        if (span <= 0) return 0;
        double hue = max == r ? (g - b) / span : max == g ? 2 + (b - r) / span : 4 + (r - g) / span;
        return (hue * 60 + 360) % 360;
    }

    /// <summary>The same colour further round the wheel: as light and as saturated, another hue.</summary>
    static Color Turned(Color c, double degrees)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        double hue = (Hue(c.R, c.G, c.B) + degrees + 360) % 360 / 60;
        double mid = min + (max - min) * (1 - Math.Abs(hue % 2 - 1)); // the channel between the strongest and the weakest
        (double r, double g, double b) = (int)hue switch
        {
            0 => (max, mid, min),
            1 => (mid, max, min),
            2 => (min, max, mid),
            3 => (min, mid, max),
            4 => (mid, min, max),
            _ => (max, min, mid),
        };
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }
}
