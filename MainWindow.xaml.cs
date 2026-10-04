using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    enum View { Idle, Media, Timer, Volume, Charge, Focus, Toast, Notice, MediaBig, IdleBig, TimerBig, TimerSet, Menu, Settings, Look, Shelf }

    /// <summary>What a click has opened; None is the compact pill.</summary>
    enum Panel { None, Player, Timer, TimerSet, Menu, Settings, Look, Shelf }

    readonly record struct Dims(double W, double H, double R);

    static readonly Dictionary<View, Dims> Sizes = new()
    {
        [View.Idle] = new(118, 34, 17),
        [View.Media] = new(210, 34, 17),
        [View.Timer] = new(132, 34, 17),
        [View.Volume] = new(250, 34, 17),
        [View.Charge] = new(230, 34, 17),
        [View.Focus] = new(236, 34, 17),
        [View.Toast] = new(340, 68, 30),
        [View.Notice] = new(320, 64, 29),
        [View.MediaBig] = new(380, PlayerHeight, 40),
        [View.IdleBig] = new(320, 124, 38),
        [View.TimerBig] = new(330, 92, 40),
        [View.TimerSet] = new(300, 190, 38),
        [View.Menu] = new(300, 248, 34),
        [View.Settings] = new(320, 374, 34),
        [View.Look] = new(320, 208, 34),
        [View.Shelf] = new(380, 136, 34),
    };

    const double HostWidth = 620;
    static readonly int[] Scales = [85, 100, 115, 130]; // percent: the sizes to pick from
    static readonly int[] Gaps = [0, 4, 8, 12, 16, 24]; // px between the top of the screen and the island
    const double SourcePause = 0.25; // seconds between two turns to another app: a wheel sends its notches in bursts
    const double BubbleGap = 7; // between the split-off bubble and the pill
    const double CarryTimer = 78, CarryShelf = 54; // width of the bubble carrying either (the shelf's at the least: more digits widen it)...
    const double CarryBoth = 11; // ...and how much narrower it is than the two together, when it carries both
    const double ShelfEnd = 13, ShelfEndTimed = 10; // the count's room to the bubble's right end, alone and after the timer, whose own ends are narrower
    const double RideLeast = 0.5, RideMost = 1.15; // how far the content of the island is scaled as it rides a change of shape
    const double MotionPace = 650; // px per second of the island's edges that blur what it shows by 1 px...
    const double MotionMost = 4; // ...up to this
    const double ShelfStep = ShelfTile.Wide + 4; // a tile and the gap after it
    const double ShelfFade = 16; // px of tiles past an end of the row by which that end has faded out fully
    const int MaxMinutes = 99; // the countdown always reads mm:ss
    const int HeadsetEvery = 300; // ticks between looks at the headphones' charge: it moves slowly
    const int HeadsetLow = 20, HeadsetCritical = 10; // percent: passing each on the way down is worth a warning
    const int TimerLast = 10; // seconds: the end of a countdown turns red and beats
    const double SkipMemory = 3; // seconds a press of "previous" stays the reason for the cover that comes next
    const double VolumeTrack = 162;
    const double VolumePush = 7; // how far the bar gives when the volume is asked past an end of it
    const double SeekTrack = 260;
    const double SeekThin = 6, SeekHover = 9, SeekDrag = 12; // bar thickness: resting, under the pointer, while scrubbing
    const double EqFrame = 0.012; // seconds: caps the bars at 60–80 fps on high-refresh displays
    const double MediaWidth = 210; // compact player without a lyric line
    const double MediaMaxWidth = 440; // it widens to fit the line being sung, up to this
    const double MediaNameWidth = 300; // ...while a track name is cut off here: it just sits there, no need to be big
    const double LyricInset = 77; // cover on the left + bars on the right of the lyric box
    const double LyricEdge = 8; // faded strip on each side of the lyric box
    const double LyricSpeed = 36; // px per second, when the line lasts long enough to take it easy
    const double LyricGap = 4; // seconds of silence in the lyrics before the track name fills in
    const double PlayerHeight = 176; // expanded player without lyrics
    const double PlayerLyricRoom = 74; // it grows this much taller to fit three lines of them
    const double PlayerLyricGap = 4; // between two lines there
    const double PlayerLyricDim = 0.4; // opacity of the lines around the one being sung
    const double PlayerLyricSmall = 0.94; // ...their size next to it
    const double PlayerLyricBlur = 1.5; // ...and how far out of focus they are
    const double PlayerLyricAhead = 0.6; // opacity of the part of that line not sung yet
    const double PlayerLyricLongest = 8; // seconds a line takes to fill at most: what is left until the next one is a break
    static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(200); // the line lands as it is sung, not after
    static readonly TimeSpan PausedGrace = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(550); // open panel, pointer gone
    static readonly TimeSpan BubbleLinger = TimeSpan.FromSeconds(2.5); // ...longer when it was opened from the bubble
    static readonly TimeSpan AwayFor = TimeSpan.FromSeconds(5); // a middle click sends the island off screen for this long
    static readonly TimeSpan PushFor = TimeSpan.FromMilliseconds(140); // the bar stays stretched this long after the last push
    static readonly TimeSpan DropLinger = TimeSpan.FromMilliseconds(150);
    static readonly CultureInfo Ru = new("ru-RU");

    readonly Dictionary<View, FrameworkElement> _views;
    readonly Spring _w = new(34), _h = new(34), _r = new(17), _scale = new(1), _offset = new(0);
    readonly Spring _seekX = new(0), _seekH = new(SeekThin);
    readonly Spring _split = new(0); // 0: the bubble is tucked behind the pill, 1: it stands on its own
    readonly Spring _bubbleScale = new(1); // the bubble answers the pointer by itself, not along with the pill
    readonly Spring _carryTimer = new(0), _carryShelf = new(0); // 1: the bubble carries the timer, the shelf
    readonly Spring _shelfWide = new(CarryShelf); // px the shelf's count takes in the bubble, as many digits as it has
    readonly Spring _shelfScroll = new(0); // px the tiles are moved left by the wheel
    readonly Spring _push = new(0); // px the volume bar is stretched past its end
    readonly Spring _size = new(Settings.Scale / 100.0), _gap = new(Settings.Gap); // the looks picked in the menu
    readonly RectangleGeometry _clip = new();
    readonly SolidColorBrush _accent = new(Colors.White);
    readonly SolidColorBrush _timerTint; // everything a countdown shows is drawn with it: orange, red at the end
    readonly ScaleTransform _beat = new(1, 1), _beatBig = new(1, 1); // the rings and the big digits, on each of the last seconds
    readonly AudioService _audio = new();
    readonly SpectrumService _spectrum = new();
    readonly float[] _bands = new float[SpectrumService.Bands];
    readonly MediaService _media;
    readonly LyricsService _lyrics = new();
    readonly NetworkService _network;
    readonly Countdown _timer = new();
    readonly Shelf _shelf;
    readonly Dictionary<Shelf.Item, ShelfTile> _tiles = new();
    readonly BlurEffect _motion = new() { Radius = 0, RenderingBias = RenderingBias.Performance }; // over the content while the island changes shape
    readonly Alarm _alarm = new();
    readonly Stopwatch _time = Stopwatch.StartNew();
    readonly DispatcherTimer _tick, _transientTimer, _collapseTimer, _awayTimer, _pushTimer, _dropTimer;
    readonly View? _forced;
    readonly double _forcedTimer;

    View _current = View.Idle;
    View? _transient;
    Panel _panel;
    bool _hover, _pressed, _hidden, _animating, _eqRunning, _seekRunning, _scrubbing;
    bool _away; // sent off screen by a middle click
    bool _bubbleHover, _bubblePressed;
    bool _morph; // the island is changing from one view to another: what it shows rides the shape
    bool _ringing; // the countdown ran out and the alarm is still going
    bool _urgent; // ...and it is in its last seconds
    bool? _quiet; // "Do not disturb" is on; null until it is first read
    bool _dropping; // files are being dragged over the island
    bool _carrying; // ...or out of it, from the shelf
    bool _picking; // files are being picked for the shelf in the system's dialog
    Panel _beforeDrop; // what was open before the drag came over
    ShelfTile? _tilePressed; // the tile the pointer went down on...
    Point _tileFrom; // ...and where
    bool _playShown, _timerPauseShown = true; // which of the two icons each button shows
    int _minutes = 25, _timerShown = -1;
    int _shelfShown; // files the counters read
    double _lastFrame, _eqFrame, _seekFrame;
    double _scrub, _scrubUntil; // fraction under the pointer; it stays on the bar until the player reports the jump
    (int At, int Total) _seekLabel = (-1, -1);
    int _ticks;
    float _lastVolume = -1;
    bool _lastMuted, _lastPlugged, _powerKnown;
    int _headset = -1; // charge of the output device in percent; -1: it reports none
    Guid _headsetId; // ...and the device that number belongs to
    string _lastTitle = "";
    DateTime _lastPlaying = DateTime.MinValue;
    ImageSource? _cover;
    int _skip = 1; // the way the last skip went...
    double _skipAt = -SkipMemory; // ...and when
    double _sourceAt = -SkipMemory; // when the wheel last turned the island to another app
    string _source = "", _sourceName = ""; // the app on show, and what to call it once it has been turned to
    Color? _rim; // colour the island's edge has taken from the cover
    LyricsService.Line[] _lyricLines = [];
    int _lyricIndex = -1;
    string _lyricTitle = "";
    bool _lyricNamed;
    double _mediaWidth = MediaWidth;
    TextBlock _lyric;
    LyricsService.Line[] _playerLines = [];
    Lyric[] _playerRows = [];
    double[] _playerMiddles = []; // where each row's middle sits in the column of lines
    int _playerIndex = -1;
    bool _playerRoom; // the expanded player has made room for lyrics
    bool _playerWaiting; // ...and holds it with placeholder lines while they are looked up
    IntPtr _hwnd;
    int _shellMessage;

    public MainWindow()
    {
        InitializeComponent();
        // room for the island at its largest and furthest from the top of the screen: the window itself never
        // changes size, or everything in it would jump as it did
        Width *= Scales[^1] / 100.0;
        Height = Height * Scales[^1] / 100.0 + Gaps[^1];

        _views = new()
        {
            [View.Idle] = IdleView,
            [View.Media] = MediaView,
            [View.Timer] = TimerView,
            [View.Volume] = VolumeView,
            [View.Charge] = ChargeView,
            [View.Focus] = FocusView,
            [View.Toast] = ToastView,
            [View.Notice] = NoticeView,
            [View.MediaBig] = MediaBigView,
            [View.IdleBig] = IdleBigView,
            [View.TimerBig] = TimerBigView,
            [View.TimerSet] = TimerSetView,
            [View.Menu] = MenuView,
            [View.Settings] = SettingsView,
            [View.Look] = LookView,
            [View.Shelf] = ShelfView,
        };
        foreach (FrameworkElement v in _views.Values)
        {
            v.RenderTransformOrigin = new Point(0.5, 0.5);
            // its own fade in and out, then the ride on the shape of the pill
            v.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new ScaleTransform(1, 1), new TranslateTransform() } };
            v.Visibility = Visibility.Collapsed;
            v.Opacity = 0;
        }
        IdleView.Visibility = Visibility.Visible;
        IdleView.Opacity = 1;

        Host.Clip = _clip;
        EqSmall.Fill = EqToast.Fill = EqBig.Fill = _accent;

        _timerTint = new SolidColorBrush(((SolidColorBrush)FindResource("Orange")).Color);
        TimerRing.Stroke = BubbleRing.Stroke = _timerTint;
        TimerText.Foreground = BubbleText.Foreground = BigTimer.Foreground = BigTimerLabel.Foreground = MenuTimer.Foreground = _timerTint;
        TimerDisc.Fill = _timerTint;
        TimerPauseIcon.Fill = TimerPauseIcon.Stroke = TimerPlayIcon.Fill = TimerPlayIcon.Stroke = _timerTint;
        TimerRing.RenderTransformOrigin = BubbleRing.RenderTransformOrigin = new Point(0.5, 0.5);
        TimerRing.RenderTransform = BubbleRing.RenderTransform = _beat;
        // the digits are set against the right edge, so that is where they swell from
        BigTimer.RenderTransformOrigin = new Point(1, 0.5);
        BigTimer.RenderTransform = _beatBig;
        foreach (FrameworkElement icon in new FrameworkElement[] { PlayIcon, PauseIcon, TimerPlayIcon, TimerPauseIcon })
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new ScaleTransform(1, 1);
        }

        _lyric = LyricA;
        _scale.Tune(320, 20);
        _offset.Tune(260, 26);
        _r.Tune(300, 30);
        _seekX.Tune(170, 26);
        _seekH.Tune(420, 26);
        _split.Tune(140, 17); // unhurried: the neck between the two has to be seen stretching and snapping
        _bubbleScale.Tune(320, 20);
        _push.Tune(420, 18); // loose enough to wobble once it is let go
        _carryTimer.Tune(260, 24);
        _carryShelf.Tune(260, 24);
        _shelfWide.Tune(260, 24);
        _shelfScroll.Tune(260, 30);
        _size.Tune(240, 26);
        _gap.Tune(240, 26);

        // debug aid: `DynamicIsland.exe --view MediaBig` pins one state, `--timer 90` starts a 90 s countdown
        string[] args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "--view");
        if (flag >= 0 && flag + 1 < args.Length && Enum.TryParse(args[flag + 1], true, out View forced)) _forced = forced;
        flag = Array.IndexOf(args, "--timer");
        if (flag >= 0 && flag + 1 < args.Length && double.TryParse(args[flag + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            _forcedTimer = seconds;

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        _lyrics.Changed += () => UpdateLyric();
        _network = new NetworkService(Dispatcher);
        _network.Changed += OnNetworkChanged;
        _shelf = new Shelf(Dispatcher);
        _shelf.Changed += SyncShelf;
        _shelf.Pictured += item =>
        {
            if (_tiles.TryGetValue(item, out ShelfTile? tile)) tile.Show();
        };

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += (_, _) => Tick();
        _transientTimer = new DispatcherTimer();
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _transient = null;
            Quiet();
            UpdateView();
        };
        _collapseTimer = new DispatcherTimer { Interval = CollapseDelay };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            // the dialog that picks files for the shelf takes the pointer away: the shelf stays to receive them
            if (_hover || _picking) return;
            _panel = Panel.None;
            UpdateView();
        };
        _awayTimer = new DispatcherTimer { Interval = AwayFor };
        _awayTimer.Tick += (_, _) =>
        {
            _awayTimer.Stop();
            _away = false;
            SetTargets();
        };
        _pushTimer = new DispatcherTimer { Interval = PushFor };
        _pushTimer.Tick += (_, _) =>
        {
            _pushTimer.Stop();
            _push.Target = 0;
            Animate();
        };
        // the drag goes from one element of the island to the next as it moves: only one that is not followed by
        // another coming in has really left
        _dropTimer = new DispatcherTimer { Interval = DropLinger };
        _dropTimer.Tick += (_, _) =>
        {
            _dropTimer.Stop();
            EndDrop();
            _panel = _beforeDrop;
            UpdateView();
        };

        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // no Alt-Tab entry, and clicking the island never steals focus from the app you're in
        long ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE,
            new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));

        _shellMessage = (int)Native.RegisterWindowMessage("SHELLHOOK");
        if (Native.RegisterShellHookWindow(_hwnd)) HwndSource.FromHwnd(_hwnd).AddHook(OnShellMessage);
    }

    // the volume and track keys pass through the shell on their way to the system: the volume itself is polled,
    // but a key pressed at the end of its range changes nothing there, and nothing else says which way a track was skipped
    IntPtr OnShellMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != _shellMessage || wParam.ToInt64() != Native.HSHELL_APPCOMMAND) return IntPtr.Zero;
        switch (Native.AppCommand(lParam))
        {
            case Native.APPCOMMAND_VOLUME_UP when VolumeAtEnd(true): PushVolume(true); break;
            case Native.APPCOMMAND_VOLUME_DOWN when VolumeAtEnd(false): PushVolume(false); break;
            case Native.APPCOMMAND_MEDIA_NEXTTRACK: Skipped(1); break;
            case Native.APPCOMMAND_MEDIA_PREVIOUSTRACK: Skipped(-1); break;
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Place();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(Place);
        UpdateClock();
        UpdateSwitches(false);
        UpdateLook();
        SyncAccent(false);
        SyncShelf();
        Intro();
        _tick.Start();
        if (_forcedTimer > 0) StartTimer(TimeSpan.FromSeconds(_forcedTimer));

        try { await _media.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        try { await _network.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
    }

    void Place()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    void Intro()
    {
        _scale.Value = 0.3;
        _offset.Value = -50;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)));
        UpdateView();
        SetTargets();
    }

    // ───────────────────────── state ─────────────────────────

    bool MediaActive => _media.HasTrack && (_media.IsPlaying || DateTime.UtcNow - _lastPlaying < PausedGrace);

    void UpdateView()
    {
        View target = _forced ?? _panel switch
        {
            Panel.Menu => View.Menu,
            Panel.Settings => View.Settings,
            Panel.Look => View.Look,
            Panel.Shelf => View.Shelf,
            Panel.TimerSet => View.TimerSet,
            Panel.Timer when _timer.Active => View.TimerBig,
            Panel.Timer or Panel.Player => _media.HasTrack ? View.MediaBig : View.IdleBig,
            // the music keeps the pill; a running timer takes it only when nothing plays (otherwise it is the bubble)
            _ => _transient ?? (MediaActive ? View.Media : _timer.Active ? View.Timer : View.Idle),
        };
        if (target == View.Toast && !_media.HasTrack) target = View.Idle;
        if (target == _current)
        {
            SyncEq();
            SyncRim();
            return;
        }

        Dims from = SizeOf(_current);
        _current = target;
        // how tall the player opens depends on whether it has lyrics to show
        if (target == View.MediaBig) UpdatePlayerLyric(true);
        else WaitPlayerLyric(false);
        Dims to = SizeOf(target);
        bool growing = to.W * to.H >= from.W * from.H;
        // overshoot a little when growing, settle firmly when shrinking
        _w.Tune(growing ? 300 : 340, growing ? 22 : 30);
        _h.Tune(growing ? 300 : 340, growing ? 22 : 30);

        _morph = true;
        Swap(_views[target]);
        SetTargets();

        SyncEq();
        SyncRim();
        if (target == View.MediaBig) StartSeek();
        if (target == View.Media) UpdateLyric(true);
    }

    // the light edge takes the colour of the cover for as long as the island is about its music
    void SyncRim()
    {
        bool music = _media.HasTrack && (_cover != null || Settings.Accent != null) && (MediaActive || _current == View.MediaBig);
        Color? tint = Settings.Rim && music ? Accent : null;
        if (tint == _rim) return;
        _rim = tint;
        Body.Tint(tint, Ms(450));
    }

    /// <summary>What the music is drawn in: the colour picked in the menu, or the one that stands for the cover.</summary>
    Color Accent => Settings.Accent ?? _media.Accent;

    // the bars and the light of the player
    void SyncAccent(bool animate = true)
    {
        Duration time = Ms(animate ? 450 : 0);
        _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Accent, time));
        Color[] palette = Settings.Accent is { } own ? MediaService.Around(own) : _media.Palette;
        Glow.Tint(palette, time);
        PlayerLyricWait.Tint(palette, time);
    }

    bool EqVisible => _current is View.Media or View.Toast or View.MediaBig;

    // the bars only cost frames (and audio capture) while they are on screen; once paused they settle and stop
    void SyncEq()
    {
            // with the setting on, the bars move to the app that plays rather than to the whole system
            _spectrum.Source = Settings.AppSpectrum ? _media.Source : "";
            _spectrum.Active = EqVisible && _media.IsPlaying;
            if (!EqVisible || _eqRunning) return;
        _eqRunning = true;
        _eqFrame = _time.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnEqFrame;
    }

    /// <param name="force">Too important to skip: closes whatever is open instead of giving way to it.</param>
    void ShowTransient(View view, double seconds, bool force = false)
    {
        if (force) _panel = Panel.None;
        else if (_panel != Panel.None || _hidden || _away) return;
        _transient = view;
        _transientTimer.Stop();
        _transientTimer.Interval = TimeSpan.FromSeconds(seconds);
        _transientTimer.Start();
        UpdateView();
    }

    Dims SizeOf(View view) => view switch
    {
        View.Media => Sizes[view] with { W = _mediaWidth },
        View.MediaBig when _playerRoom => Sizes[view] with { H = PlayerHeight + PlayerLyricRoom },
        _ => Sizes[view],
    };

    void SetTargets()
    {
        Dims d = SizeOf(_current);
        bool compact = d.H < 40;
        _w.Target = d.W;
        _h.Target = d.H;
        _r.Target = d.R;
        // the timer splits off whenever the compact pill is showing something else, and the shelf whenever it holds anything
        bool timer = _timer.Active && _current != View.Timer, shelf = _shelf.Items.Count > 0;
        bool split = compact && (timer || shelf);
        _split.Target = split ? 1 : 0;
        if (split)
        {
            _carryTimer.Target = timer ? 1 : 0;
            _carryShelf.Target = shelf ? 1 : 0;
            if (shelf) _shelfWide.Target = ShelfWide();
            // tucked away, it comes out already the width of what it carries
            if (_split.Value < 0.05)
            {
                _carryTimer.Value = _carryTimer.Target;
                _carryShelf.Value = _carryShelf.Target;
                _shelfWide.Value = _shelfWide.Target;
                _carryTimer.Velocity = _carryShelf.Velocity = _shelfWide.Velocity = 0;
            }
        }
        // a ringing timer shows itself even over a fullscreen app. Out of sight is past the gap above it too,
        // counted in the island's own px
        _offset.Target = (_hidden || _away) && !_ringing ? -(d.H + 30 + Settings.Gap * 100.0 / Settings.Scale) : 0;
        _scale.Target = _pressed ? (compact ? 0.93 : 0.975) : _hover && compact ? 1.07 : 1;
        _bubbleScale.Target = _bubblePressed ? 0.93 : _bubbleHover ? 1.07 : 1;
        Animate();
    }

    /// <summary>The bubble's room for the shelf: the count with its icon, as far from the left end as from the right.</summary>
    double ShelfWide()
    {
        BubbleShelf.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(CarryShelf, BubbleShelf.DesiredSize.Width - BubbleShelf.Margin.Right + 2 * ShelfEnd);
    }

    // ───────────────────────── animation ─────────────────────────

    void Animate()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = _time.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = _time.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastFrame, 0.05);
        _lastFrame = now;
        if (dt <= 0) return;

        bool moving = _w.Advance(dt);
        moving |= _h.Advance(dt);
        moving |= _r.Advance(dt);
        moving |= _scale.Advance(dt);
        moving |= _offset.Advance(dt);
        moving |= _size.Advance(dt);
        moving |= _gap.Advance(dt);
        moving |= _split.Advance(dt);
        moving |= _bubbleScale.Advance(dt);
        moving |= _carryTimer.Advance(dt);
        moving |= _carryShelf.Advance(dt);
        moving |= _shelfWide.Advance(dt);
        moving |= _shelfScroll.Advance(dt);
        moving |= _push.Advance(dt);
        ApplyShape();

        if (!moving)
        {
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
            Settle();
        }
    }

    void ApplyShape()
    {
        double w = Math.Max(_w.Value, 24), h = Math.Max(_h.Value, 24);
        double r = Math.Clamp(_r.Value, 0, Math.Min(w, h) / 2);

        Pill.Width = Shadow.Width = w;
        Pill.Height = Shadow.Height = h;
        Pill.CornerRadius = Shadow.CornerRadius = new CornerRadius(r);
        var pill = new Rect((HostWidth - w) / 2, 0, w, h);
        Shadow.Opacity = Math.Clamp((h - 40) / 50, 0, 1);

        // the compact player is the one view that changes size on its own: keep its ends on the pill's ends
        if (_current == View.Media) MediaView.Width = w;
        // ...and the expanded one grows taller for lyrics: its controls ride the pill's bottom edge down,
        // and the lines come in once the room is half open
        if (_current == View.MediaBig)
        {
            MediaBigView.Height = Math.Max(h, PlayerHeight);
            PlayerLyricBox.Opacity = Math.Clamp((h - PlayerHeight) / PlayerLyricRoom * 2 - 1, 0, 1);
        }
        if (_morph) Ride(w, h);
        // the faster the edges go, the more what is inside smears, the way anything quick does to the eye
        double smear = _morph ? Math.Min(Math.Sqrt(_w.Velocity * _w.Velocity + _h.Velocity * _h.Velocity) / MotionPace, MotionMost) : 0;
        _motion.Radius = smear;
        Host.Effect = smear > 0.1 ? _motion : null;
        double scrolled = _shelfScroll.Value, ahead = ShelfOverflow - scrolled;
        ShelfMove.X = -scrolled;
        ShelfEdgeLeft.Color = Edge(scrolled);
        ShelfEdgeRight.Color = Edge(ahead);

        _clip.Rect = pill;
        _clip.RadiusX = _clip.RadiusY = r;

        // stretched past its end, the volume bar gets longer and thinner, like rubber
        double push = Math.Max(_push.Value, -VolumePush);
        VolStretch.ScaleX = 1 + push / VolumeTrack;
        VolStretch.ScaleY = 1 - push / VolumePush * 0.22;

        double scale = Math.Max(_scale.Value, 0.01);
        IslandScale.ScaleX = IslandScale.ScaleY = scale;
        // the gap is in px of the screen, whatever the size
        double size = Math.Max(_size.Value, 0.01);
        RootSize.ScaleX = RootSize.ScaleY = size;
        RootMove.Y = _offset.Value + _gap.Value / size;

        // the bubble rides the pill's right end: inside it, then out past the gap, its content fading in as it comes free.
        // It follows that end as the pill swells under the pointer, but keeps its own size. It is as wide as what it
        // carries, and gets wider or narrower as one of the two comes or goes, that one fading with it
        double split = _split.Value, bubble = Math.Max(_bubbleScale.Value, 0.01);
        double timer = Math.Max(_carryTimer.Value, 0), shelf = Math.Max(_carryShelf.Value, 0);
        double wide = Math.Max(CarryTimer * timer + _shelfWide.Value * shelf - CarryBoth * timer * shelf, Bubble.Height);
        bool apart = split > 0.01;
        Bubble.Visibility = apart ? Visibility.Visible : Visibility.Collapsed;
        Bubble.Width = wide;
        BubbleTimer.Opacity = Math.Clamp(timer * 2 - 1, 0, 1);
        BubbleShelf.Opacity = Math.Clamp(shelf * 2 - 1, 0, 1);
        // beside the timer the count keeps to the timer's ends, so the two sit as one row in the middle
        BubbleShelf.Margin = new Thickness(0, 0, ShelfEnd + (ShelfEndTimed - ShelfEnd) * Math.Clamp(timer, 0, 1), 0);
        BubbleMove.X = (w * scale - wide) / 2 + (BubbleGap + wide) * split;
        BubbleScale.ScaleX = BubbleScale.ScaleY = bubble;
        BubbleBody.Opacity = Math.Clamp(split * 4 - 3, 0, 1);
        // until then the pill's end is the pill's to click
        Bubble.IsHitTestVisible = split > 0.75;

        // the body is drawn in the pill's own scale, so the bubble is measured in it too
        double past = ((BubbleGap + wide) * split - wide) / scale; // of its left end beyond the pill's right one
        Body.Shape(pill, r, apart
            ? new Rect(pill.Right + past, 0, wide * bubble / scale, Bubble.Height * bubble / scale)
            : Rect.Empty);
    }

    /// <summary>What the island shows follows its shape as it changes: each view keeps to the middle of the pill and grows or shrinks with it.</summary>
    void Ride(double w, double h)
    {
        foreach (FrameworkElement v in _views.Values)
        {
            if (v.Visibility != Visibility.Visible) continue;
            Ride(v, Math.Clamp(Math.Min(w / v.Width, h / v.Height), RideLeast, RideMost), (h - v.Height) / 2);
        }
    }

    static void Ride(FrameworkElement v, double scale, double down)
    {
        TransformCollection parts = ((TransformGroup)v.RenderTransform).Children;
        var size = (ScaleTransform)parts[1];
        size.ScaleX = size.ScaleY = scale;
        ((TranslateTransform)parts[2]).Y = down;
    }

    // the shape has come to rest: everything stands where it is laid out, and sharp
    void Settle()
    {
        if (!_morph) return;
        _morph = false;
        foreach (FrameworkElement v in _views.Values) Ride(v, 1, 0);
        Host.Effect = null;
    }

    /// <summary>The view's own scale, the one it fades in and out with.</summary>
    static ScaleTransform Fade(FrameworkElement v) => (ScaleTransform)((TransformGroup)v.RenderTransform).Children[0];

    void Swap(FrameworkElement next)
    {
        foreach (FrameworkElement v in _views.Values)
            if (v != next && v.Visibility == Visibility.Visible) FadeOut(v);
        FadeIn(next);
    }

    void FadeOut(FrameworkElement v)
    {
        v.IsHitTestVisible = false;
        var blur = new BlurEffect { Radius = 0 };
        v.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(12, Ms(170)));

        ScaleTransform scale = Fade(v);
        var shrink = new DoubleAnimation(0.9, Ms(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        var fade = new DoubleAnimation(0, Ms(140));
        fade.Completed += (_, _) =>
        {
            if (_views[_current] == v) return;
            v.Visibility = Visibility.Collapsed;
            v.Effect = null;
        };
        v.BeginAnimation(OpacityProperty, fade);
    }

    void FadeIn(FrameworkElement v)
    {
        bool fresh = v.Visibility != Visibility.Visible || v.Opacity < 0.05;
        v.Visibility = Visibility.Visible;
        v.IsHitTestVisible = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan delay = TimeSpan.FromMilliseconds(70);

        var blur = new BlurEffect { Radius = 12 };
        v.Effect = blur;
        var sharpen = new DoubleAnimation(0, Ms(300)) { BeginTime = delay, EasingFunction = ease };
        sharpen.Completed += (_, _) =>
        {
            // drop the effect so text is rendered crisp again
            if (ReferenceEquals(v.Effect, blur)) v.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);

        ScaleTransform scale = Fade(v);
        var grow = new DoubleAnimation(1, Ms(380)) { BeginTime = delay, EasingFunction = ease };
        if (fresh) grow.From = 0.86;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        v.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(240)) { BeginTime = delay });
    }

    static Duration Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    // ───────────────────────── periodic work ─────────────────────────

    void Tick()
    {
        _ticks++;
        PollVolume();
        if (_ticks % HeadsetEvery == 1) ReadHeadset(_audio.Device);
        UpdateLyric();
        UpdateTimer();
        if (_ticks % 5 == 0)
        {
            CheckFullscreen();
            PollQuiet();
        }
        if (_ticks % 10 == 0)
        {
            UpdateClock();
            PollPower();
            Native.KeepOnTop(_hwnd);
            if (_media.IsPlaying) _lastPlaying = DateTime.UtcNow;
            UpdateView();
        }
    }

    void OnEqFrame(object? sender, EventArgs e)
    {
            // the app that plays can change under us, and the spectrum points at one process only
            _spectrum.Source = Settings.AppSpectrum ? _media.Source : "";

            double now = _time.Elapsed.TotalSeconds;
        double dt = now - _eqFrame;
        if (dt < EqFrame) return;
        _eqFrame = now;
        dt = Math.Min(dt, 0.05);

        bool playing = _media.IsPlaying;
        // no loopback capture (exotic device format): fall back to the plain output peak
        float[]? bands = _spectrum.Read(_bands) ? _bands : null;
        double level = 0;
        if (bands == null)
        {
            float peak = _audio.Peak();
            level = peak < 0 ? 0.4 : peak;
        }

        bool moving = EqSmall.Tick(bands, level, playing, now, dt);
        moving |= EqToast.Tick(bands, level, playing, now, dt);
        moving |= EqBig.Tick(bands, level, playing, now, dt);

        moving |= Glow.Tick(EqBig, now, dt);

        if (!EqVisible || (!playing && !moving))
        {
            CompositionTarget.Rendering -= OnEqFrame;
            _eqRunning = false;
        }
    }

    void UpdateClock()
    {
        DateTime now = DateTime.Now;
        IdleClock.Text = BigClock.Text = now.ToString("HH:mm");
        BigDate.Text = now.ToString("dddd, d MMMM", Ru);
    }

    void CheckFullscreen()
    {
        bool hidden = Settings.HideFullscreen && Native.IsForegroundFullscreen(_hwnd);
        if (hidden == _hidden) return;
        _hidden = hidden;
        SetTargets();
    }

    void PollVolume()
    {
        if (!_audio.TryGetVolume(out float level, out bool muted)) return;
        if (_audio.TakeSwitch(out AudioService.Output device))
        {
            // another device has its own level: that is not a volume change, so no HUD on top of the notice
            _lastVolume = -1;
            ReadHeadset(device, true);
        }
        bool first = _lastVolume < 0;
        if (!first && Math.Abs(level - _lastVolume) < 0.004 && muted == _lastMuted) return;

        _lastVolume = level;
        _lastMuted = muted;

        int percent = (int)Math.Round(level * 100);
        VolIcon.Kind = InfoVolIcon.Kind = muted || percent == 0 ? Glyph.Mute : level < 0.34 ? Glyph.Quiet : level < 0.67 ? Glyph.Mid : Glyph.Loud;
        VolText.Text = percent.ToString();
        InfoVol.Text = muted ? "выкл" : percent + "%";
        VolFill.BeginAnimation(WidthProperty, new DoubleAnimation(muted ? 0 : VolumeTrack * level, Ms(first ? 0 : 140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        if (first) return;
        ShowTransient(View.Volume, 1.6);
        ShowPlayerVolume(level, muted, false);
    }

    /// <summary>Nowhere further for the volume to go that way.</summary>
    bool VolumeAtEnd(bool up) => _lastVolume >= 0 && (up ? !_lastMuted && _lastVolume >= 0.999f : _lastVolume <= 0.001f);

    /// <summary>The volume is asked past an end of its range: the bar gives that way and springs back once the asking stops.</summary>
    void PushVolume(bool up)
    {
        // it holds on to its other end
        VolTrack.RenderTransformOrigin = new Point(up ? 0 : 1, 0.5);
        _push.Target = VolumePush;
        _pushTimer.Stop();
        _pushTimer.Start();
        ShowTransient(View.Volume, 1.6);
        ShowPlayerVolume(_lastVolume, _lastMuted, false);
        Animate();
    }

    /// <summary>The open player leaves no room for the HUD: there the level comes up beside the buttons for a moment.</summary>
    /// <param name="app">It is the app that plays that was turned up or down, not the whole system.</param>
    void ShowPlayerVolume(float level, bool muted, bool app)
    {
        if (_current != View.MediaBig) return;
        PlayerVolumeIcon.Kind = app ? Glyph.Note : VolIcon.Kind;
        PlayerVolumeIcon.Fill = PlayerVolumeText.Foreground = app ? _accent : (Brush)FindResource("Dim");
        PlayerVolumeText.Text = muted ? "выкл" : (int)Math.Round(level * 100) + "%";

        // from wherever it is: asked again while it is up, it just stays
        var show = new DoubleAnimationUsingKeyFrames { Duration = Ms(1900) };
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        PlayerVolume.BeginAnimation(OpacityProperty, show);
    }

    void PollPower()
    {
        if (!Native.TryGetBattery(out int percent, out bool plugged))
        {
            InfoBatRow.Visibility = Visibility.Collapsed;
            return;
        }

        InfoBatRow.Visibility = Visibility.Visible;
        InfoBat.Text = percent + "%";
        InfoBat.Foreground = plugged ? ChargeText.Foreground : Brushes.White;

        if (_powerKnown && plugged && !_lastPlugged)
        {
            ChargeText.Text = percent + "%";
            ChargeFill.BeginAnimation(WidthProperty, new DoubleAnimation(0, 20 * percent / 100.0, Ms(700))
            {
                BeginTime = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            ShowTransient(View.Charge, 3);
        }
        _powerKnown = true;
        _lastPlugged = plugged;
    }

    /// <summary>Puts the charge of the output device (Bluetooth headphones report one) into the expanded views.</summary>
    /// <param name="announce">The device has just taken over the sound: introduce it, charge included.</param>
    async void ReadHeadset(AudioService.Output? output, bool announce = false)
    {
        int level = output is { } bound ? await Headset.ChargeAsync(bound.Container) : -1;
        // swapped while this was being read: the new device gets a read of its own
        if (output != _audio.Device) return;

        AudioService.Output device = output ?? default;
        // another device's charge is nothing to compare with
        int was = device.Container == _headsetId ? _headset : -1;
        _headset = level;
        _headsetId = device.Container;

        bool known = level >= 0, low = known && level <= HeadsetLow;
        Glyph icon = device.Headphones ? Glyph.Headphones : Glyph.Speaker;
        Brush red = (Brush)FindResource("Red");
        InfoHeadsetRow.Visibility = PlayerHeadset.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        InfoHeadsetIcon.Kind = PlayerHeadsetIcon.Kind = icon;
        InfoHeadset.Text = PlayerHeadsetText.Text = level + "%";
        InfoHeadset.Foreground = low ? red : Brushes.White;
        PlayerHeadsetIcon.Fill = PlayerHeadsetText.Foreground = low ? red : (Brush)FindResource("Dim");
        if (output == null) return;

        string name = device.Name.Length > 0 ? device.Name : "Вывод звука";
        if (known) name += " · " + level + "%";
        bool Passed(int mark) => was > mark && level <= mark;
        if (announce)
            Notify(icon, Brushes.White, device.Kind.Length > 0 ? device.Kind : "Аудиоустройство", name);
        else if (known && (Passed(HeadsetLow) || Passed(HeadsetCritical)))
            Notify(icon, red, "Низкий заряд", name);
    }

    /// <summary>"Do not disturb" turned on or off: the moon comes up in the pill, and stays by the date in the expanded clock.</summary>
    void PollQuiet()
    {
        if (Native.DoNotDisturb() is not bool quiet || quiet == _quiet) return;
        bool first = _quiet == null;
        _quiet = quiet;
        InfoFocus.Visibility = quiet ? Visibility.Visible : Visibility.Collapsed;
        // the state it was in at start is no news
        if (first) return;

        FocusIcon.Fill = FocusText.Foreground = (Brush)FindResource(quiet ? "Indigo" : "Dim");
        FocusText.Text = quiet ? "Вкл." : "Выкл.";
        ShowTransient(View.Focus, 2.2);

        // on, the moon swings up into place; off, it sinks back and shrinks a little. Both as the view comes in
        TimeSpan delay = TimeSpan.FromMilliseconds(70);
        IEasingFunction ease = quiet ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } : new CubicEase { EasingMode = EasingMode.EaseOut };
        var swing = new DoubleAnimation(quiet ? -80 : 0, quiet ? 0 : 24, Ms(quiet ? 620 : 420)) { BeginTime = delay, EasingFunction = ease };
        var grow = new DoubleAnimation(quiet ? 0.4 : 1, quiet ? 1 : 0.84, Ms(quiet ? 520 : 420)) { BeginTime = delay, EasingFunction = ease };
        FocusTurn.BeginAnimation(RotateTransform.AngleProperty, swing);
        FocusSize.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        FocusSize.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    // ───────────────────────── notices ─────────────────────────

    /// <summary>One-off notice in the pill: an icon, a title and a line of detail.</summary>
    void Notify(Glyph icon, Brush tint, string title, string text, double seconds = 3.2, bool force = false)
    {
        // nothing talks over a ringing timer
        if (_ringing && !force) return;

        bool shown = _current == View.Notice;
        NoticeIcon.Kind = icon;
        NoticeIcon.Fill = tint;
        NoticeTitle.Text = title;
        NoticeText.Text = text;
        ShowTransient(View.Notice, seconds, force);
        // one notice replacing another: no view change to animate, so blur the new text in
        if (shown) FadeIn(NoticeView);
    }

    void OnNetworkChanged(NetworkService.State was, NetworkService.State now)
    {
        if (!Settings.Network) return;

        if (now.Vpn != was.Vpn)
        {
            string[] before = was.Vpn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string[] after = now.Vpn.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (after.Except(before).FirstOrDefault() is { } up)
                Notify(Glyph.Vpn, (Brush)FindResource("Green"), "VPN включён", up);
            else if (before.Except(after).FirstOrDefault() is { } down)
                Notify(Glyph.Vpn, (Brush)FindResource("Dim"), "VPN отключён", down);
            // a tunnel going up or down also reshuffles the connection underneath: one notice is enough
            return;
        }

        if (now.Link == NetworkService.Link.None)
        {
            Notify(Glyph.Offline, (Brush)FindResource("Red"), "Нет сети", "Подключение потеряно");
            return;
        }

        bool wifi = now.Link == NetworkService.Link.Wifi;
        string title = now.Link switch
        {
            NetworkService.Link.Wired => "Ethernet",
            _ when now.Name.Length > 0 => now.Name,
            NetworkService.Link.Wifi => "Wi-Fi",
            _ => "Мобильная сеть",
        };
        if (now.Internet)
            Notify(wifi ? Glyph.Wifi : Glyph.Wired, (Brush)FindResource("Green"), title, wifi ? "Wi-Fi подключён" : "Сеть подключена");
        else
            Notify(wifi ? Glyph.Wifi : Glyph.Wired, (Brush)FindResource("Orange"), title, "Без доступа к интернету");
    }

    // ───────────────────────── timer ─────────────────────────

    void StartTimer(TimeSpan total)
    {
        _timer.Start(total);
        BigTimerLabel.Text = "Таймер · " + Span(total);
        _panel = Panel.None;
        SyncTimer();
        UpdateView();
        SetTargets();
    }

    void StopTimer()
    {
        _timer.Stop();
        MenuTimer.Text = "";
        Urgent(false);
    }

    static string Span(TimeSpan t) =>
        t.TotalSeconds >= 60 ? (int)Math.Round(t.TotalMinutes) + " мин" : (int)t.TotalSeconds + " с";

    // running or paused: the pause button and how bright the digits are
    void SyncTimer()
    {
        bool running = _timer.Running;
        if (running != _timerPauseShown)
        {
            _timerPauseShown = running;
            Trade(running ? TimerPlayIcon : TimerPauseIcon, running ? TimerPauseIcon : TimerPlayIcon, TimerBigView.IsVisible);
        }
        TimerText.Opacity = BubbleText.Opacity = BigTimer.Opacity = running ? 1 : 0.5;
        _timerShown = -1;
        UpdateTimer();
    }

    // the countdown shows in the compact pill, the bubble, the expanded view and the menu row
    void UpdateTimer()
    {
        if (!_timer.Active) return;
        TimeSpan left = _timer.Left;
        if (left <= TimeSpan.Zero)
        {
            TimerDone();
            return;
        }

        TimerRing.Progress = BubbleRing.Progress = _timer.Share;
        // round up, so it opens on the full time and hits 0:00 as it rings
        int seconds = (int)Math.Ceiling(left.TotalSeconds);
        if (seconds == _timerShown) return;
        _timerShown = seconds;
        // minutes are not rolled over into hours: past the hour it reads 75:00, the way it was set, and still fits the bubble
        TimerText.Text = BubbleText.Text = BigTimer.Text = MenuTimer.Text = $"{seconds / 60}:{seconds % 60:00}";

        Urgent(seconds <= TimerLast);
        if (_urgent && _timer.Running) Beat();
    }

    /// <summary>The last seconds of a countdown are red; the rest of it, and the next one, orange.</summary>
    void Urgent(bool on)
    {
        if (on == _urgent) return;
        _urgent = on;
        Color to = ((SolidColorBrush)FindResource(on ? "Red" : "Orange")).Color;
        _timerTint.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Ms(300)));
    }

    // once a second: the ring swells and settles, the big digits give a little with it
    void Beat()
    {
        Swell(_beat, 1.24);
        Swell(_beatBig, 1.05);
    }

    static void Swell(ScaleTransform scale, double to)
    {
        var beat = new DoubleAnimationUsingKeyFrames { Duration = Ms(460) };
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
    }

    void TimerDone()
    {
        string total = Span(_timer.Total);
        StopTimer();
        _ringing = true;
        _alarm.Ring();

        var pulse = new DoubleAnimation(1, 1.2, Ms(420))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        NoticePulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        NoticePulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        Notify(Glyph.Bell, (Brush)FindResource("Orange"), "Таймер", "Время вышло · " + total, 12, true);
        SetTargets();
    }

    /// <summary>Silences the alarm of a finished timer.</summary>
    void Quiet()
    {
        if (!_ringing) return;
        _ringing = false;
        _alarm.Stop();
        NoticePulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        NoticePulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    void SetMinutes(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, MaxMinutes);
        _minutes = minutes;
        SetupText.Text = _minutes + ":00";
    }

    void TimerRow_Click(object sender, RoutedEventArgs e)
    {
        _panel = _timer.Active ? Panel.Timer : Panel.TimerSet;
        UpdateView();
    }

    void TimerLess_Click(object sender, RoutedEventArgs e) => SetMinutes(_minutes - 1);
    void TimerMore_Click(object sender, RoutedEventArgs e) => SetMinutes(_minutes + 1);
    void TimerPreset_Click(object sender, RoutedEventArgs e) => SetMinutes(int.Parse((string)((Button)sender).Tag));
    void TimerStart_Click(object sender, RoutedEventArgs e) => StartTimer(TimeSpan.FromMinutes(_minutes));

    void TimerToggle_Click(object sender, RoutedEventArgs e)
    {
        _timer.Toggle();
        SyncTimer();
    }

    void TimerCancel_Click(object sender, RoutedEventArgs e)
    {
        StopTimer();
        _panel = Panel.None;
        UpdateView();
        SetTargets();
    }

    // the bubble has its own hover and press: pointing at one of the two must not move the other

    void Bubble_MouseEnter(object sender, MouseEventArgs e)
    {
        _bubbleHover = true;
        SetTargets();
    }

    void Bubble_MouseLeave(object sender, MouseEventArgs e)
    {
        _bubbleHover = _bubblePressed = false;
        SetTargets();
    }

    void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // handled: the press stays with the bubble instead of squeezing the pill
        e.Handled = true;
        _bubblePressed = true;
        SetTargets();
    }

    void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_bubblePressed) return;
        // the bubble opens its own activity, not the pill's: the timer or the shelf, whichever half of it was clicked
        e.Handled = true;
        _bubblePressed = false;
        bool timer = _carryTimer.Target > 0 && (_carryShelf.Target == 0 || e.GetPosition(Bubble).X < CarryTimer - CarryBoth / 2);
        Open(timer ? Panel.Timer : Panel.Shelf);
        UpdateView();
        SetTargets();

        // the expanded timer may not reach as far as the pointer: give it time to get there before closing again
        _collapseTimer.Interval = BubbleLinger;
        _collapseTimer.Start();
    }

    // ───────────────────────── media ─────────────────────────

    void OnMediaChanged()
    {
        string title = _media.HasTrack ? _media.Title : "";
        bool newTrack = title.Length > 0 && title != _lastTitle;
        _lastTitle = title;

        // turned to by the wheel, an app is introduced by name; one that took over by itself is not
        bool asked = _time.Elapsed.TotalSeconds - _sourceAt < SkipMemory;
        string source = _media.Source;
        bool turned = source != _source && asked;
        if (source != _source) _sourceName = asked ? SourceApp.Name(source) : "";
        else if (newTrack && !asked) _sourceName = "";
        _source = source;

        string artist = string.IsNullOrWhiteSpace(_media.Artist) ? "Неизвестный исполнитель" : _media.Artist;
        TitleBig.Text = ToastTitle.Text = title;
        ArtistBig.Text = ToastArtist.Text = _sourceName.Length > 0 ? _sourceName + " · " + artist : artist;

        if (!ReferenceEquals(_cover, _media.Art))
        {
            _cover = _media.Art;
            // the covers cross the way the playlist went: back only when "previous" has just been asked for
            int heading = _time.Elapsed.TotalSeconds - _skipAt < SkipMemory ? _skip : 1;
            ArtSmall.Show(_cover, heading);
            ArtToast.Show(_cover, heading);
            ArtBig.Show(_cover, heading);
            SyncAccent();
        }

        if (_media.IsPlaying != _playShown)
        {
            _playShown = _media.IsPlaying;
            Trade(_playShown ? PlayIcon : PauseIcon, _playShown ? PauseIcon : PlayIcon, MediaBigView.IsVisible);
        }
        // an app that was turned to stays in the pill for a while even when it is paused
        if (_media.IsPlaying || turned) _lastPlaying = DateTime.UtcNow;

        if (turned || (newTrack && _media.IsPlaying)) ShowTransient(View.Toast, 3.2);
        TrackLyrics();
        UpdateView();
        UpdateLyric();
    }

    /// <summary>Two icons share one spot: the one on show shrinks away into a blur as the other grows into focus.</summary>
    static void Trade(FrameworkElement leave, FrameworkElement enter, bool animate)
    {
        Duration quick = Ms(animate ? 150 : 0), slow = Ms(animate ? 360 : 0);

        var shrink = new DoubleAnimation(0.5, quick) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        leave.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        leave.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        leave.BeginAnimation(OpacityProperty, new DoubleAnimation(0, quick));

        // a little past its size and back: the button answers the press
        var grow = new DoubleAnimation(0.5, 1, slow) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        enter.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        enter.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        enter.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(animate ? 200 : 0)));

        leave.Effect = enter.Effect = null;
        if (!animate) return;

        var blurOut = new BlurEffect { Radius = 0 };
        leave.Effect = blurOut;
        var soften = new DoubleAnimation(6, quick);
        soften.Completed += (_, _) =>
        {
            if (ReferenceEquals(leave.Effect, blurOut)) leave.Effect = null;
        };
        blurOut.BeginAnimation(BlurEffect.RadiusProperty, soften);

        var blurIn = new BlurEffect { Radius = 6 };
        enter.Effect = blurIn;
        var sharpen = new DoubleAnimation(0, Ms(240));
        sharpen.Completed += (_, _) =>
        {
            // drop the effect so the icon is rendered crisp again
            if (ReferenceEquals(enter.Effect, blurIn)) enter.Effect = null;
        };
        blurIn.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
    }

    void Skipped(int direction)
    {
        _skip = direction;
        _skipAt = _time.Elapsed.TotalSeconds;
    }

    /// <summary>Turns the island to the next app that has something to play, or to the previous one.</summary>
    void SwitchSource(int direction)
    {
        double now = _time.Elapsed.TotalSeconds;
        if (now - _sourceAt < SourcePause || !_media.Switch(direction)) return;
        _sourceAt = now;
        // the covers cross the way the wheel went
        Skipped(direction);
    }

    // switched off in the menu, the lyrics are not even looked up
    void TrackLyrics() => _lyrics.Track(Settings.Lyrics && _media.HasTrack ? _media.Title : "", _media.Artist);

    // ───────────────────────── lyrics ─────────────────────────

    // the fade is a fixed strip at each end, whatever the current width of the box
    void LyricBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Max(e.NewSize.Width, 2 * LyricEdge);
        LyricMask.EndPoint = new Point(width, 0);
        LyricMaskIn.Offset = LyricEdge / width;
        LyricMaskOut.Offset = 1 - LyricEdge / width;
    }

    /// <param name="snap">The view is just appearing: drop the stale line instead of animating it away.</param>
    void UpdateLyric(bool snap = false)
    {
        if (_current == View.MediaBig) UpdatePlayerLyric(snap);
        if (_current != View.Media) return;

        LyricsService.Line[] lines = _lyrics.For(_media.Duration);
        TimeSpan at = _media.Position + LyricLead;
        int index = lines.Length - 1;
        while (index >= 0 && lines[index].Time > at) index--;

        string title = _media.HasTrack ? _media.Title : "";
        string text = index < 0 ? "" : lines[index].Text;
        TimeSpan end = index + 1 < lines.Length ? lines[index + 1].Time : _media.Duration;
        double seconds = (end - at).TotalSeconds;
        // no lyrics, the intro, or a long break: the track name takes the place of the line
        bool named = text.Length == 0 && (index < 0 || seconds >= LyricGap);
        if (ReferenceEquals(lines, _lyricLines) && index == _lyricIndex && (!named || title == _lyricTitle)) return;

        _lyricLines = lines;
        _lyricIndex = index;
        _lyricTitle = title;
        ShowLyric(named ? title : text, seconds, snap, named);
    }

    /// <summary>Slides the previous line up and out, the new one in from below; a line that does not fit scrolls while it is sung.</summary>
    void ShowLyric(string text, double seconds, bool snap, bool named)
    {
        TextBlock old = _lyric;
        // already on screen: nothing to clear, or the same track name (lyrics arriving mid-intro)
        if (text == old.Text && (text.Length == 0 || (named && _lyricNamed))) return;
        TextBlock next = _lyric = old == LyricA ? LyricB : LyricA;
        _lyricNamed = named;

        var leave = (TranslateTransform)old.RenderTransform;
        var blurOut = new BlurEffect { Radius = 0 };
        old.Effect = blurOut;
        blurOut.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(6, Ms(snap ? 0 : 220)));
        leave.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, Ms(snap ? 0 : 260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        old.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(snap ? 0 : 200)));

        // the name is dimmed so it never reads as a lyric, and is cut with an ellipsis rather than scrolled
        next.Foreground = named ? (Brush)FindResource("Dim") : Brushes.White;
        next.TextTrimming = named ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        next.MaxWidth = named ? MediaNameWidth - LyricInset - 2 * LyricEdge : double.PositiveInfinity;
        next.Text = text;
        next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = next.DesiredSize.Width;
        Canvas.SetTop(next, Math.Round((LyricBox.Height - next.DesiredSize.Height) / 2));

        // the pill stretches to the line; only a line longer than the widest pill has to scroll
        _mediaWidth = text.Length == 0
            ? MediaWidth
            : Math.Clamp(width + 2 * LyricEdge + LyricInset, MediaWidth, MediaMaxWidth);
        double box = _mediaWidth - LyricInset;
        double overflow = width - (box - 2 * LyricEdge);
        if (!snap) _w.Tune(280, 30); // line to line the pill glides, no bounce
        SetTargets();

        var enter = (TranslateTransform)next.RenderTransform;
        DoubleAnimationUsingKeyFrames? scroll = null;
        if (overflow > 0)
        {
            // hold the start for a moment, then reach the end shortly before the next line comes in
            double hold = Math.Min(0.6, seconds * 0.2);
            double run = Math.Min(overflow / LyricSpeed, Math.Max(seconds - hold - 0.5, 0.6));
            // keyframes, not BeginTime: while a delayed animation waits, the block sits at its previous scroll offset
            scroll = new DoubleAnimationUsingKeyFrames { Duration = Ms((hold + run) * 1000) };
            scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(LyricEdge, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(LyricEdge, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
            scroll.KeyFrames.Add(new EasingDoubleKeyFrame(LyricEdge - overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + run)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        }
        enter.X = overflow > 0 ? LyricEdge : (box - width) / 2;
        enter.BeginAnimation(TranslateTransform.XProperty, scroll);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var blurIn = new BlurEffect { Radius = 6 };
        next.Effect = blurIn;
        var sharpen = new DoubleAnimation(0, Ms(300)) { EasingFunction = ease };
        sharpen.Completed += (_, _) =>
        {
            if (ReferenceEquals(next.Effect, blurIn)) next.Effect = null;
        };
        blurIn.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
        enter.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Ms(380)) { EasingFunction = ease });
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
    }

    /// <summary>
    /// The expanded player grows taller for the lyrics: the line being sung in the middle, filling with light as it
    /// goes, its neighbours around it smaller, dimmer and out of focus.
    /// </summary>
    /// <param name="snap">The player is just opening: put the lines in place instead of scrolling to them.</param>
    void UpdatePlayerLyric(bool snap = false)
    {
        LyricsService.Line[] lines = _lyrics.For(_media.Duration);
        if (!ReferenceEquals(lines, _playerLines))
        {
            _playerLines = lines;
            _playerIndex = -1;
            LayPlayerLyric(lines);
            // another song's lines: nothing to scroll from, they fade in where they belong
            if (!snap) PlayerLyricLines.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
            snap = true;
        }

        // the search takes a moment after a track change: hold the room meanwhile rather than close it only to open it again
        bool room = lines.Length > 0 || (_playerRoom && _lyrics.Pending);
        if (room != _playerRoom)
        {
            _playerRoom = room;
            _h.Tune(280, 30); // the pill glides to its new height, no bounce
            SetTargets();
        }
        WaitPlayerLyric(room && lines.Length == 0);
        if (lines.Length == 0) return;

        TimeSpan at = _media.Position + LyricLead;
        int index = lines.Length - 1;
        while (index >= 0 && lines[index].Time > at) index--;
        if (index == _playerIndex && !snap) return;

        Duration fade = Ms(snap ? 0 : 300);
        int was = _playerIndex;
        _playerIndex = index;
        for (int i = 0; i < _playerRows.Length; i++)
        {
            Lyric row = _playerRows[i];
            if (i == index || i == was) Sing(row, i == index, fade);
            // only the lines in sight carry a blur; the one being sung comes into focus
            if (i != index && Math.Abs(i - index) <= 2 && Settings.LyricEffects) Focus(row, PlayerLyricBlur, fade);
            else if (i == index) Focus(row, 0, fade);
            else row.Effect = null;
        }
        SweepPlayerLyric();

        // before the first line is sung, it waits in the middle unlit
        double middle = _playerMiddles[Math.Max(index, 0)];
        PlayerLyricMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(PlayerLyricBox.Height / 2 - middle, Ms(snap ? 0 : 450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    /// <summary>Puts placeholder lines in the room held for the lyrics, or takes them away as the lyrics come (or do not).</summary>
    void WaitPlayerLyric(bool on)
    {
        if (on == _playerWaiting) return;
        _playerWaiting = on;
        if (on) PlayerLyricWait.Run(true);
        var fade = new DoubleAnimation(on ? 1 : 0, Ms(on ? 300 : 200));
        // the sheen stops once it is out of sight
        if (!on) fade.Completed += (_, _) => { if (!_playerWaiting) PlayerLyricWait.Run(false); };
        PlayerLyricWait.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Stacks the whole song in a column; the box shows three lines of it at a time.</summary>
    void LayPlayerLyric(LyricsService.Line[] lines)
    {
        PlayerLyricLines.Children.Clear();
        _playerRows = new Lyric[lines.Length];
        _playerMiddles = new double[lines.Length];

        double width = PlayerLyricBox.Width, top = 0, size = Settings.LyricEffects ? PlayerLyricSmall : 1;
        for (int i = 0; i < lines.Length; i++)
        {
            // a line with no words marks a break in the singing
            var row = new Lyric(lines[i].Text.Length > 0 ? lines[i].Text : "♪")
            {
                Width = width,
                Opacity = PlayerLyricDim,
                // the words start at the left, so that is the side a line shrinks towards
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = new ScaleTransform(size, size),
            };
            PlayerLyricLines.Children.Add(row);
            row.Measure(new Size(width, double.PositiveInfinity));
            Canvas.SetTop(row, top);
            _playerMiddles[i] = top + row.DesiredSize.Height / 2;
            top += row.DesiredSize.Height + PlayerLyricGap;
            _playerRows[i] = row;
        }
    }

    /// <summary>Brings a line forward as it is sung, or sets it back among the others.</summary>
    void Sing(Lyric row, bool sung, Duration time)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(sung ? 1 : PlayerLyricDim, time));
        // with the effects switched off in the menu that is all: the line is lit whole, at its full size
        bool effects = Settings.LyricEffects;
        // set back, the whole line is lit evenly again, however far it had been sung
        row.BeginAnimation(Lyric.UnsungProperty, new DoubleAnimation(sung && effects ? PlayerLyricAhead : 1, time));
        var size = new DoubleAnimation(sung || !effects ? 1 : PlayerLyricSmall, time) { EasingFunction = ease };
        row.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, size);
        row.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, size);
    }

    void Focus(Lyric row, double radius, Duration time)
    {
        if (row.Effect is not BlurEffect blur)
        {
            if (radius == 0) return;
            row.Effect = blur = new BlurEffect { Radius = 0 };
        }
        var turn = new DoubleAnimation(radius, time);
        turn.Completed += (_, _) =>
        {
            // drop the effect so the line is rendered crisp
            if (ReferenceEquals(row.Effect, blur) && blur.Radius < 0.01) row.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, turn);
    }

    /// <summary>Fills the line being sung with light: from its first letter as it starts to its last as the next one comes in.</summary>
    void SweepPlayerLyric()
    {
        int index = _playerIndex;
        if (index < 0 || index >= _playerRows.Length) return;

        TimeSpan start = _playerLines[index].Time;
        TimeSpan end = index + 1 < _playerLines.Length ? _playerLines[index + 1].Time : _media.Duration;
        double seconds = Math.Clamp((end - start).TotalSeconds, 0.3, PlayerLyricLongest);
        _playerRows[index].Progress = Math.Clamp((_media.Position + LyricLead - start).TotalSeconds / seconds, 0, 1);
    }

    // ───────────────────────── seek bar ─────────────────────────

    // the bar runs on its own frames while the expanded player is open: it sweeps in from empty,
    // glides to wherever playback jumps, and swells under the pointer
    void StartSeek()
    {
        _seekX.Value = _seekX.Velocity = 0;
        if (_seekRunning) return;
        _seekRunning = true;
        _seekFrame = _time.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnSeekFrame;
    }

    void OnSeekFrame(object? sender, EventArgs e)
    {
        double now = _time.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _seekFrame, 0.05);
        _seekFrame = now;
        if (_current != View.MediaBig && !_scrubbing)
        {
            CompositionTarget.Rendering -= OnSeekFrame;
            _seekRunning = false;
            return;
        }
        if (dt <= 0) return;

        TimeSpan duration = _media.Duration;
        bool known = duration.TotalSeconds >= 1;
        double played = known ? Math.Clamp(_media.Position / duration, 0, 1) : 0;
        // after a drop the player takes a moment to report the new position: don't flick back meanwhile
        if (!_scrubbing && now < _scrubUntil && Math.Abs(played - _scrub) < 0.02) _scrubUntil = 0;
        double shown = _scrubbing || now < _scrubUntil ? _scrub : played;

        double track = SeekArea.ActualWidth > 0 ? SeekArea.ActualWidth : SeekTrack;
        _seekX.Target = track * shown;
        _seekH.Target = _scrubbing ? SeekDrag : SeekArea.IsMouseOver ? SeekHover : SeekThin;
        _seekX.Advance(dt);
        _seekH.Advance(dt);

        double thick = Math.Max(_seekH.Value, 2);
        SeekBar.Height = thick;
        SeekBack.CornerRadius = SeekFill.CornerRadius = new CornerRadius(thick / 2);
        SeekFill.Width = Math.Clamp(_seekX.Value, 0, track);
        // the line being sung is filled on the same frames
        SweepPlayerLyric();

        // while scrubbing the labels read the spot under the pointer
        TimeSpan at = duration * shown;
        var label = known ? ((int)at.TotalSeconds, (int)duration.TotalSeconds) : (-1, -1);
        if (label == _seekLabel) return;
        _seekLabel = label;
        PosText.Text = known ? Format(at) : "–:––";
        RemText.Text = known ? "-" + Format(duration - at) : "–:––";
    }

    static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    void Play_Click(object sender, RoutedEventArgs e) => _media.TogglePlay();

    void Prev_Click(object sender, RoutedEventArgs e)
    {
        Skipped(-1);
        _media.Previous();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        Skipped(1);
        _media.Next();
    }

    // not handled: the click goes on to the pill and closes the player, out of the way of the app it has just brought up
    void Art_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed) SourceApp.Show(_media.Source, _media.Title);
    }

    double SeekFraction(MouseEventArgs e) => Math.Clamp(e.GetPosition(SeekArea).X / SeekArea.ActualWidth, 0, 1);

    void Seek_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (SeekArea.ActualWidth <= 0 || _media.Duration.TotalSeconds < 1) return;

        _scrubbing = true;
        _scrub = SeekFraction(e);
        _seekX.Tune(900, 60); // stick to the pointer
        PosText.Foreground = RemText.Foreground = Brushes.White;
        SeekArea.CaptureMouse();
    }

    void Seek_MouseMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing) _scrub = SeekFraction(e);
    }

    void Seek_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // swallow the release so it doesn't collapse the island
        e.Handled = true;
        if (!_scrubbing) return;

        EndScrub();
        _scrubUntil = _time.Elapsed.TotalSeconds + 1;
        _media.Seek(_scrub);
        SeekArea.ReleaseMouseCapture();
    }

    // capture taken away mid-drag: let go without seeking
    void Seek_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scrubbing) EndScrub();
    }

    void EndScrub()
    {
        _scrubbing = false;
        _seekX.Tune(170, 26);
        PosText.Foreground = RemText.Foreground = (Brush)FindResource("Dim");
    }

    // ───────────────────────── pointer ─────────────────────────

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hover = true;
        _collapseTimer.Stop();
        SetTargets();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _hover = _pressed = false;
        SetTargets();
        if (_panel == Panel.None) return;
        _collapseTimer.Interval = CollapseDelay;
        _collapseTimer.Start();
    }

    void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        SetTargets();
    }

    void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;

        // a click silences a ringing timer, closes whatever is open, or opens what the pill is showing
        if (_ringing || _panel != Panel.None) Open(Panel.None);
        else Open(MediaActive || !_timer.Active ? Panel.Player : Panel.Timer);
        UpdateView();
        SetTargets();
    }

    void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // from the settings that is a step back, to the menu they belong to
        Open(_panel == Panel.Menu ? Panel.None : Panel.Menu);
        UpdateSwitches(false);
        UpdateView();
    }

    // a middle click gets the island out of the way for a few seconds: whatever was open closes, and it comes back compact
    void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        Open(Panel.None);
        _away = true;
        _awayTimer.Stop();
        _awayTimer.Start();
        UpdateView();
        SetTargets();
    }

    void Open(Panel panel)
    {
        _panel = panel;
        _transient = null;
        _transientTimer.Stop();
        Quiet();
    }

    void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        bool up = e.Delta > 0;
        int step = up ? 1 : -1;
        e.Handled = true;
        // with Ctrl held the wheel leafs through the apps that play: down for the next one
        if (Native.CtrlDown) SwitchSource(-step);
        else if (_current == View.TimerSet) SetMinutes(_minutes + step);
        else if (_current == View.Look && SizeRow.IsMouseOver) SetScale(Step(Scales, Settings.Scale, step, false));
        else if (_current == View.Look && GapRow.IsMouseOver) SetGap(Step(Gaps, Settings.Gap, step, false));
        // more files than fit: down goes on to the later ones
        else if (_current == View.Shelf && ShelfOverflow > 0) ScrollShelf(-step);
        // over the open player it is the music that gets louder, not everything else along with it
        else if (_current == View.MediaBig && Settings.AppVolume && _audio.Nudge(_media.Source, step * 0.02f, out float level))
            ShowPlayerVolume(level, false, true);
        else if (VolumeAtEnd(up)) PushVolume(up);
        else _audio.Nudge(step * 0.02f);
    }

    // ───────────────────────── shelf ─────────────────────────

    /// <summary>Lays the tiles out to match the shelf: new ones grow in at the end, the ones taken off shrink out of the row.</summary>
    void SyncShelf()
    {
        foreach ((Shelf.Item item, ShelfTile tile) in _tiles.ToList())
        {
            if (_shelf.Items.Contains(item)) continue;
            _tiles.Remove(item);
            Leave(tile);
        }
        foreach (Shelf.Item item in _shelf.Items)
        {
            if (_tiles.ContainsKey(item)) continue;
            var tile = new ShelfTile(item) { Margin = new Thickness(0, 0, ShelfStep - ShelfTile.Wide, 0) };
            tile.MouseLeftButtonDown += Tile_MouseLeftButtonDown;
            tile.MouseMove += Tile_MouseMove;
            tile.MouseLeftButtonUp += Tile_MouseLeftButtonUp;
            tile.Removed += t => _shelf.Remove(t.Item);
            _tiles[item] = tile;
            ShelfTiles.Children.Add(tile);
            if (ShelfView.IsVisible) Arrive(tile);
        }

        int count = _shelf.Items.Count;
        // the digits roll the way the number goes
        BubbleShelfText.Down = MenuShelf.Down = count < _shelfShown;
        _shelfShown = count;
        // emptied, the bubble keeps its last number while it tucks away
        if (count > 0) BubbleShelfText.Text = count.ToString();
        MenuShelf.Text = count > 0 ? count.ToString() : "";
        ShelfClear.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncShelfHint();
        ScrollShelf(0);
        SetTargets();
    }

    void Arrive(ShelfTile tile)
    {
        var grow = new DoubleAnimation(0.5, 1, Ms(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(240)));

        var blur = new BlurEffect { Radius = 8 };
        tile.Effect = blur;
        var sharpen = new DoubleAnimation(0, Ms(320));
        sharpen.Completed += (_, _) =>
        {
            // drop the effect so the picture and the name are rendered crisp again
            if (ReferenceEquals(tile.Effect, blur)) tile.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, sharpen);
    }

    // it shrinks into nothing while the gap it leaves closes, the tiles after it sliding over
    void Leave(ShelfTile tile)
    {
        tile.IsHitTestVisible = false;
        bool seen = ShelfView.IsVisible;
        Duration time = Ms(seen ? 300 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var shrink = new DoubleAnimation(0.5, time) { EasingFunction = ease };
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(seen ? 180 : 0)));
        tile.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0), time) { EasingFunction = ease });
        var close = new DoubleAnimation(0, time) { EasingFunction = ease };
        close.Completed += (_, _) => ShelfTiles.Children.Remove(tile);
        tile.BeginAnimation(WidthProperty, close);
    }

    // with nothing on it the shelf says what it is for and is a place to click; a drag over it outlines where to let go,
    // and so, more faintly, does the pointer over the empty shelf
    void SyncShelfHint()
    {
        bool empty = _shelf.Items.Count == 0;
        ShelfHintText.Text = _dropping ? "Отпустите, чтобы положить" : "Перетащите сюда файлы";
        ShelfHintMore.Opacity = _dropping ? 0 : 1;
        ShelfHint.IsHitTestVisible = empty && !_dropping;
        ShelfHint.BeginAnimation(OpacityProperty, new DoubleAnimation(empty ? 1 : 0, Ms(200)));
        double zone = _dropping ? 0.5 : empty && ShelfHint.IsMouseOver ? 0.25 : 0;
        ShelfZone.BeginAnimation(OpacityProperty, new DoubleAnimation(zone, Ms(zone > 0 ? 150 : 300)));
    }

    void ShelfHint_MouseHover(object sender, MouseEventArgs e) => SyncShelfHint();

    /// <summary>Puts files on the shelf from the system's dialog, for when there is nothing at hand to drag.</summary>
    void ShelfAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Положить на полку", Multiselect = true };
        bool picked;
        _picking = true;
        try { picked = dialog.ShowDialog(this) == true; }
        catch (Exception ex)
        {
            App.Log(ex);
            picked = false;
        }
        finally { _picking = false; }
        if (picked) _shelf.Add(dialog.FileNames);

        // the shelf stayed open to show them arrive; then it goes the way it would have gone without the dialog
        Mouse.Synchronize();
        _hover = Island.IsMouseOver;
        SetTargets();
        SyncShelfHint();
        if (_hover || _panel == Panel.None) return;
        _collapseTimer.Interval = BubbleLinger;
        _collapseTimer.Start();
    }

    /// <summary>How far the row of tiles reaches past the strip that shows it.</summary>
    double ShelfOverflow => Math.Max(_shelf.Items.Count * ShelfStep - (ShelfStep - ShelfTile.Wide)
        - (ShelfView.Width - ShelfStrip.Margin.Left - ShelfStrip.Margin.Right), 0);

    /// <summary>The end of the row's mask: see-through as soon as <paramref name="past"/> px of tiles lie beyond it.</summary>
    static Color Edge(double past) => Color.FromArgb((byte)Math.Round(255 * (1 - Math.Clamp(past / ShelfFade, 0, 1))), 0, 0, 0);

    void ScrollShelf(int tiles)
    {
        _shelfScroll.Target = Math.Clamp(_shelfScroll.Target + tiles * ShelfStep, 0, ShelfOverflow);
        Animate();
    }

    void ShelfRow_Click(object sender, RoutedEventArgs e)
    {
        _panel = Panel.Shelf;
        UpdateView();
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

    // a press on a tile is the tile's: no squeeze of the pill, and its release does not close the shelf
    void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _tilePressed = (ShelfTile)sender;
        _tileFrom = e.GetPosition(this);
        _tilePressed.CaptureMouse();
    }

    void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tilePressed != sender || e.LeftButton != MouseButtonState.Pressed) return;
        Vector moved = e.GetPosition(this) - _tileFrom;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        ShelfTile tile = _tilePressed;
        _tilePressed = null;
        tile.ReleaseMouseCapture();
        Carry(tile);
    }

    // a click without a drag opens the file, and the island gets out of its way
    void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_tilePressed != sender) return;
        e.Handled = true;
        ShelfTile tile = _tilePressed;
        _tilePressed = null;
        tile.ReleaseMouseCapture();

        try { Process.Start(new ProcessStartInfo(tile.Item.Path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            App.Log(ex);
            // gone from where it lay: nothing to keep on the shelf
            if (!File.Exists(tile.Item.Path) && !Directory.Exists(tile.Item.Path)) _shelf.Remove(tile.Item);
            return;
        }
        Open(Panel.None);
        UpdateView();
        SetTargets();
    }

    /// <summary>
    /// The file goes where it is dragged. It is offered to be copied or linked there, never moved: it stays where it
    /// lay, whatever the place it is dropped on would do by default.
    /// </summary>
    void Carry(ShelfTile tile)
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { tile.Item.Path });
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, Ms(120)));
        DragDropEffects done;
        _carrying = true;
        try { done = DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Link); }
        catch (Exception ex)
        {
            App.Log(ex);
            done = DragDropEffects.None;
        }
        finally { _carrying = false; }

        // put down somewhere, it has been carried there and is off the shelf; let go anywhere else, it comes back
        if (done != DragDropEffects.None) _shelf.Remove(tile.Item);
        else tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(200)));

        // the drag kept the pointer to itself: whether it is still over the island has to be asked
        Mouse.Synchronize();
        _hover = Island.IsMouseOver;
        SetTargets();
        if (_hover || _panel == Panel.None) return;
        _collapseTimer.Interval = CollapseDelay;
        _collapseTimer.Start();
    }

    /// <summary>What a drag over the island may do with its files: they are only pointed at, never taken from where they lie.</summary>
    static DragDropEffects Keep(DragEventArgs e) =>
        e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : e.AllowedEffects & DragDropEffects.Link;

    // files dragged over the island open the shelf, the place to put them down outlined
    void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_carrying || _hidden || _away || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = Keep(e);
        _dropTimer.Stop();
        _collapseTimer.Stop();
        if (_dropping) return;

        _dropping = true;
        _beforeDrop = _panel;
        Open(Panel.Shelf);
        SyncShelfHint();
        UpdateView();
    }

    void Root_DragLeave(object sender, DragEventArgs e)
    {
        if (!_dropping) return;
        _dropTimer.Stop();
        _dropTimer.Start();
    }

    void Root_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_dropping) return;
        _dropTimer.Stop();
        e.Effects = Keep(e);
        EndDrop();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _shelf.Add(paths);

        // the pointer is over the island, but nothing says so until it moves: a moment to get to it before the shelf closes
        _collapseTimer.Interval = BubbleLinger;
        _collapseTimer.Start();
    }

    void EndDrop()
    {
        _dropping = false;
        SyncShelfHint();
    }

    // ───────────────────────── menu ─────────────────────────

    void SettingsRow_Click(object sender, RoutedEventArgs e)
    {
        _panel = Panel.Settings;
        UpdateView();
    }

    void LookRow_Click(object sender, RoutedEventArgs e)
    {
        _panel = Panel.Look;
        UpdateView();
    }

    // the heading of either page
    void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        _panel = Panel.Menu;
        UpdateView();
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(!Autostart.Enabled); }
        catch (Exception ex) { App.Log(ex); }
        UpdateSwitches(true);
    }

    void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        Settings.Lyrics = !Settings.Lyrics;
        UpdateSwitches(true);
        // drops the lyrics of the track that is playing, or goes looking for them
        TrackLyrics();
    }

    void LyricEffects_Click(object sender, RoutedEventArgs e)
    {
        Settings.LyricEffects = !Settings.LyricEffects;
        UpdateSwitches(true);
        // the player lays its lines out again, the other way, the next time it opens
        _playerLines = [];
    }

    void Rim_Click(object sender, RoutedEventArgs e)
    {
        Settings.Rim = !Settings.Rim;
        UpdateSwitches(true);
        SyncRim();
    }

    void AppVolume_Click(object sender, RoutedEventArgs e)
    {
        Settings.AppVolume = !Settings.AppVolume;
        UpdateSwitches(true);
    }

    void AppSpectrum_Click(object sender, RoutedEventArgs e)
    {
        Settings.AppSpectrum = !Settings.AppSpectrum;
        UpdateSwitches(true);
    }

    void Network_Click(object sender, RoutedEventArgs e)
    {
        Settings.Network = !Settings.Network;
        UpdateSwitches(true);
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        Settings.HideFullscreen = !Settings.HideFullscreen;
        UpdateSwitches(true);
        CheckFullscreen();
    }

    void UpdateSwitches(bool animate)
    {
        LyricsSwitch.Set(Settings.Lyrics, animate);
        LyricEffectsSwitch.Set(Settings.LyricEffects, animate);
        RimSwitch.Set(Settings.Rim, animate);
        AppVolumeSwitch.Set(Settings.AppVolume, animate);
        AppSpectrumSwitch.Set(Settings.AppSpectrum, animate);
        NetworkSwitch.Set(Settings.Network, animate);
        FullscreenSwitch.Set(Settings.HideFullscreen, animate);
        AutostartSwitch.Set(Autostart.Enabled, animate);
    }

    // ───────────────────────── looks ─────────────────────────

    /// <summary>The value next to <paramref name="value"/> among the ones to pick from; past the last it is the first again, or stays.</summary>
    static int Step(int[] among, int value, int by, bool wrap)
    {
        int count = among.Length, at = Array.IndexOf(among, value) + by;
        return among[wrap ? (at % count + count) % count : Math.Clamp(at, 0, count - 1)];
    }

    void Size_Click(object sender, RoutedEventArgs e) => SetScale(Step(Scales, Settings.Scale, 1, true));
    void Gap_Click(object sender, RoutedEventArgs e) => SetGap(Step(Gaps, Settings.Gap, 1, true));

    void SetScale(int percent)
    {
        if (percent == Settings.Scale) return;
        Settings.Scale = percent;
        UpdateLook();
        ApplyLook();
    }

    void SetGap(int px)
    {
        if (px == Settings.Gap) return;
        Settings.Gap = px;
        UpdateLook();
        ApplyLook();
    }

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        // the strip's first dot is a gradient: that one leaves the colour to the cover
        Settings.Accent = ((RadioButton)sender).Background is SolidColorBrush picked ? picked.Color : null;
        UpdateLook();
        SyncAccent();
        SyncRim();
    }

    // what the rows of the page read, and which colour of the strip wears the ring
    void UpdateLook()
    {
        SizeText.Text = Settings.Scale + "%";
        GapText.Text = Settings.Gap + " px";
        foreach (RadioButton dot in AccentStrip.Children)
        {
            Color? colour = dot.Background is SolidColorBrush own ? own.Color : null;
            if (colour != Settings.Accent) continue;
            dot.IsChecked = true;
            AccentText.Text = (string)dot.Tag;
        }
    }

    /// <summary>Sends the island to the size and the distance from the top of the screen picked in the menu.</summary>
    void ApplyLook()
    {
        _size.Target = Settings.Scale / 100.0;
        _gap.Target = Settings.Gap;
        // where "out of sight" is depends on both
        SetTargets();
    }

    void Exit_Click(object sender, RoutedEventArgs e)
    {
        _tick.Stop();
        _alarm.Stop();
        var fade = new DoubleAnimation(0, Ms(220));
        fade.Completed += (_, _) => Application.Current.Shutdown();
        Root.BeginAnimation(OpacityProperty, fade);
        _scale.Target = _bubbleScale.Target = 0.5;
        Animate();
    }
}
