using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DynamicIsland;

public partial class MainWindow
{
    const double MinPillSize = 24;
    const double MinScale = 0.01;
    const double ShadowFadeHeight = 50;
    const double OffscreenMargin = 30;
    const double PressedScale = 0.93, PressedOpenScale = 0.975, HoverScale = 1.07;
    const double BubbleGap = 7;
    const double BubbleTuckFactor = 1.6;
    const double BubbleVisibleSplit = 0.01, BubbleClickableSplit = 0.75;
    const double TimerBubbleWidth = 78, ShelfBubbleWidth = 54;
    const double BubbleOverlap = 11;
    const double ShelfBubblePadding = 13, ShelfBubblePaddingWithTimer = 10;
    const double MinViewScale = 0.5, MaxViewScale = 1.15;
    const double MotionBlurSpeed = 650;
    const double MaxMotionBlur = 4, MinMotionBlur = 0.1;
    const double CacheScaleTolerance = 0.001;

    readonly Spring _width = new(34), _height = new(34), _radius = new(17);
    readonly Spring _scale = new(1, 320, 20), _offsetY = new(0, 260, 26), _leanX = new(0);
    readonly Spring _userScale = new(Settings.Scale / 100.0, 240, 26), _topGap = new(Settings.Gap, 240, 26);
    readonly Spring _bubbleSplit = new(0), _bubbleScale = new(1, 320, 20);
    readonly Spring _bubbleTimer = new(0, 260, 24), _bubbleShelf = new(0, 260, 24), _shelfBubbleWidth = new(ShelfBubbleWidth, 260, 24);
    readonly BlurEffect _motionBlur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    readonly FrameLoop _shapeLoop;
    readonly Spring[] _springs;
    Rect _clipRect = Rect.Empty;
    double _clipRadius;
    bool _morphing;

    void UpdateTargets()
    {
        PillShape shape = ShapeOf(_view);
        bool compact = shape.Height < CompactMaxHeight;
        double pull = RubberBand(_pull, MaxPull), lean = Math.Sign(_leanDrag) * RubberBand(Math.Abs(_leanDrag), MaxLean);
        bool dragging = _grab is Grab.Pull or Grab.Lean;
        _width.Target = shape.Width + pull * PullWidening + Math.Abs(lean) * LeanStretch;
        _height.Target = shape.Height + pull;
        _radius.Target = shape.Radius + pull * PullRounding;
        _leanX.Target = lean;
        if (MediaSpots.TryGetValue(_view, out MediaSpot spot))
        {
            _coverTop.Target = spot.CoverTop + pull / 2;
            _barsCenterY.Target = spot.BarsCenterY + pull / 2;
        }
        if (dragging) TuneDragSprings(true);
        UpdateBubbleTargets(compact);
        _offsetY.Target = (_hiddenByFullscreen || _sentAway) && !_ringing ? -(shape.Height + OffscreenMargin + Settings.Gap * 100.0 / Settings.Scale) : 0;
        _scale.Target = dragging ? 1 : _pressed ? (compact ? PressedScale : PressedOpenScale) : _hovered && compact ? HoverScale : 1;
        _bubbleScale.Target = _bubblePressed ? PressedScale : _bubbleHovered ? HoverScale : 1;
        StartShapeLoop();
    }

    void UpdateBubbleTargets(bool compact)
    {
        bool timer = _countdown.IsActive && _view != View.Timer, shelf = _shelf.Items.Count > 0;
        bool split = compact && (timer || shelf);
        _bubbleSplit.Target = split ? 1 : 0;
        if (compact) _bubbleSplit.Tune(140, 17);
        else _bubbleSplit.Tune(300, 30);
        if (!split) return;

        _bubbleTimer.Target = timer ? 1 : 0;
        _bubbleShelf.Target = shelf ? 1 : 0;
        if (shelf) _shelfBubbleWidth.Target = MeasureShelfBubble();
        if (_bubbleSplit.Value >= BarelyVisible) return;

        _bubbleTimer.Snap(_bubbleTimer.Target);
        _bubbleShelf.Snap(_bubbleShelf.Target);
        _shelfBubbleWidth.Snap(_shelfBubbleWidth.Target);
    }

    static double RubberBand(double distance, double limit) => limit * (1 - 1 / (distance * DragFollow / limit + 1));

    void TuneDragSprings(bool tight)
    {
        (double stiffness, double damping) = tight ? (900.0, 60.0) : (300.0, 21.0);
        _width.Tune(stiffness, damping);
        _height.Tune(stiffness, damping);
        foreach (Spring spring in _spotSprings) spring.Tune(stiffness, damping);
        _radius.Tune(tight ? 900 : 300, tight ? 60 : 30);
        _leanX.Tune(tight ? 900 : 230, tight ? 60 : 15);
    }

    double MeasureShelfBubble()
    {
        BubbleShelf.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(ShelfBubbleWidth, BubbleShelf.DesiredSize.Width - BubbleShelf.Margin.Right + 2 * ShelfBubblePadding);
    }

    void StartShapeLoop() => _shapeLoop.Start();

    bool AdvanceShape(double dt)
    {
        bool moving = false;
        foreach (Spring spring in _springs) moving |= spring.Advance(dt);
        ApplyShape();
        if (!moving) EndMorph();
        return moving;
    }

    void ApplyShape()
    {
        double w = Math.Max(_width.Value, MinPillSize), h = Math.Max(_height.Value, MinPillSize);
        double r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        var pill = new Rect((HostWidth - w) / 2, 0, w, h);
        double scale = Math.Max(_scale.Value, MinScale), size = Math.Max(_userScale.Value, MinScale);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;

        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(r);
        Shadow.Opacity = Math.Clamp((h - CompactMaxHeight) / ShadowFadeHeight, 0, 1);
        if (pill != _clipRect || r != _clipRadius)
        {
            _clipRect = pill;
            _clipRadius = r;
            Host.Clip = Shared.Clip = Shadow.Data = Squircle.Of(pill, r);
        }

        FitMediaViews(w, h);
        if (_morphing) FitViews(w, h);
        ApplyMotionBlur();
        ApplyShelfScroll();
        StretchVolumeBar();

        IslandScale.ScaleX = IslandScale.ScaleY = scale;
        RootSize.ScaleX = RootSize.ScaleY = size;
        RootMove.Y = _offsetY.Value + _topGap.Value / size;
        RootLean.X = Math.Round(_leanX.Value * size * dpi) / (size * dpi);
        double sharp = size * scale * dpi;
        if (Math.Abs(_lyricCache.RenderAtScale - sharp) > CacheScaleTolerance) _lyricCache.RenderAtScale = sharp;

        PlaceCoverAndBars(pill, dpi);
        PlaceBubble(pill, r, scale);
    }

    void ApplyMotionBlur()
    {
        double speed = Math.Sqrt(_width.Velocity * _width.Velocity + _height.Velocity * _height.Velocity);
        double smear = _morphing && _grab == Grab.None ? Math.Min(speed / MotionBlurSpeed, MaxMotionBlur) : 0;
        _motionBlur.Radius = smear;
        Host.Effect = smear > MinMotionBlur ? _motionBlur : null;
    }

    void PlaceBubble(Rect pill, double r, double scale)
    {
        double split = _bubbleSplit.Value, bubble = Math.Max(_bubbleScale.Value, MinScale);
        double timer = Math.Max(_bubbleTimer.Value, 0), shelf = Math.Max(_bubbleShelf.Value, 0);
        double wide = Math.Max(TimerBubbleWidth * timer + _shelfBubbleWidth.Value * shelf - BubbleOverlap * timer * shelf, Bubble.Height);
        bool apart = split > BubbleVisibleSplit;
        Bubble.SetVisible(apart);
        Bubble.Width = wide;
        BubbleTimer.Opacity = Math.Clamp(timer * 2 - 1, 0, 1);
        BubbleShelf.Opacity = Math.Clamp(shelf * 2 - 1, 0, 1);
        BubbleShelf.Margin = new Thickness(0, 0, ShelfBubblePadding + (ShelfBubblePaddingWithTimer - ShelfBubblePadding) * Math.Clamp(timer, 0, 1), 0);
        double tuck = Math.Max(r - Bubble.Height / 2, 0) * BubbleTuckFactor * (1 - Math.Clamp(split, 0, 1)) * scale;
        BubbleMove.X = (pill.Width * scale - wide) / 2 + (BubbleGap + wide) * split - tuck;
        BubbleScale.ScaleX = BubbleScale.ScaleY = bubble;
        BubbleBody.Opacity = Math.Clamp(split * 4 - 3, 0, 1);
        Bubble.IsHitTestVisible = split > BubbleClickableSplit;

        double past = ((BubbleGap + wide) * split - wide - tuck) / scale;
        Body.SetShape(pill, r, apart
            ? new Rect(pill.Right + past, 0, wide * bubble / scale, Bubble.Height * bubble / scale)
            : Rect.Empty);
    }

    void FitViews(double w, double h)
    {
        foreach (FrameworkElement view in _views.Values)
        {
            if (view.Visibility != Visibility.Visible) continue;
            FitView(view, Math.Clamp(Math.Min(w / view.Width, h / view.Height), MinViewScale, MaxViewScale), (h - view.Height) / 2);
        }
    }

    static void FitView(FrameworkElement view, double scale, double offsetY)
    {
        ScaleTransform fit = FitScaleOf(view);
        fit.ScaleX = fit.ScaleY = scale;
        OffsetOf(view).Y = offsetY;
    }

    void EndMorph()
    {
        if (!_morphing || _grab == Grab.Pull) return;
        _morphing = false;
        foreach (FrameworkElement view in _views.Values) FitView(view, 1, 0);
        Host.Effect = null;
    }
}
