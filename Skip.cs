using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class Skip : FrameworkElement
{
    const double PairWidth = 23, PairHeight = 15;
    const double Spacing = 11;
    const double TailX = 1, TipX = 11;
    const double Vanished = 0.02;
    static readonly Duration StepTime = Ms(420);

    static readonly Geometry Triangle = Frozen(Geometry.Parse("M1,1 L11,7.5 L1,14 Z"));
    static readonly Pen Line = Frozen(new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round });

    public static readonly DependencyProperty TurnProperty = DependencyProperty.Register(
        nameof(Turn), typeof(double), typeof(Skip),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    int _targetTurn;

    public Skip()
    {
        Width = PairWidth;
        Height = PairHeight;
    }

    public bool Back { get; set; }

    public double Turn
    {
        get => (double)GetValue(TurnProperty);
        set => SetValue(TurnProperty, value);
    }

    public void Play()
    {
        if (!IsVisible) return;
        int goal = ++_targetTurn;
        var run = new DoubleAnimation(goal, StepTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        run.Completed += (_, _) =>
        {
            if (goal != _targetTurn) return;
            _targetTurn = 0;
            BeginAnimation(TurnProperty, null);
        };
        BeginAnimation(TurnProperty, run);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double turn = Turn - Math.Floor(Turn);
        if (Back) dc.PushTransform(new ScaleTransform(-1, 1, PairWidth / 2, 0));
        Draw(dc, 0, turn, TailX);
        Draw(dc, Spacing * turn, 1, TailX);
        Draw(dc, Spacing, 1 - turn, TipX);
        if (Back) dc.Pop();
    }

    static void Draw(DrawingContext dc, double x, double size, double pivotX)
    {
        if (size < Vanished) return;
        dc.PushTransform(new TranslateTransform(x, 0));
        dc.PushTransform(new ScaleTransform(size, size, pivotX, PairHeight / 2));
        dc.PushOpacity(Math.Min(size * 2, 1));
        dc.DrawGeometry(Brushes.White, Line, Triangle);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
