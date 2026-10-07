using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class Puddle : Grid
{
    public static readonly DependencyProperty TintProperty = DependencyProperty.RegisterAttached(
        "Tint", typeof(Color), typeof(Puddle), new PropertyMetadata(Colors.White));

    readonly Liquid _liquid = new();
    readonly FrameLoop _loop;
    ButtonBase[] _buttons = [];

    public Puddle()
    {
        _loop = new FrameLoop(Advance);
        AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler((_, _) => Aim()), true);
        AddHandler(Mouse.MouseDownEvent, new MouseButtonEventHandler((_, _) => Aim()), true);
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, _) => Aim()), true);
        AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) => Aim()), true);
        MouseEnter += (_, _) => Aim();
        MouseLeave += (_, _) => Aim();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            _liquid.Rest();
            InvalidateVisual();
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _liquid.Draw(dc);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    void Aim()
    {
        if (_buttons.Length == 0) _buttons = [.. ButtonsIn(this)];
        Rect[] targets = [.. _buttons.Select(button => button.TransformToAncestor(this).TransformBounds(new Rect(button.RenderSize)))];
        int over = Array.FindIndex(_buttons, button => button.IsMouseOver);
        _liquid.Style = Settings.Hover;
        Color[] tints = [.. _buttons.Select(GetTint)];
        _liquid.Aim(targets, tints, over, Mouse.GetPosition(this), over >= 0 && _buttons[over].IsPressed);
        _loop.Start();
    }

    public static Color GetTint(DependencyObject element) => (Color)element.GetValue(TintProperty);

    public static void SetTint(DependencyObject element, Color tint) => element.SetValue(TintProperty, tint);

    bool Advance(double dt)
    {
        bool moving = _liquid.Advance(dt);
        InvalidateVisual();
        return moving;
    }

    static IEnumerable<ButtonBase> ButtonsIn(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is ButtonBase button) yield return button;
            else foreach (ButtonBase inner in ButtonsIn(child)) yield return inner;
        }
    }
}
