using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DynamicIsland;

sealed class AreaPicker : Window
{
    const double ShadeAlpha = 0.5;
    const double FrameRadius = 6;
    const double SmallestSide = 16;
    const int RowLeftUncovered = 1;
    const uint KeepOrder = 0x0004;

    readonly Int32Rect _desktop;
    readonly RectangleGeometry _everything = new(), _hole = new() { RadiusX = FrameRadius, RadiusY = FrameRadius };
    readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, RadiusX = FrameRadius, RadiusY = FrameRadius, Visibility = Visibility.Collapsed };
    Point _pressedAt;
    bool _dragging;

    public AreaPicker(Int32Rect desktop)
    {
        _desktop = desktop;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;

        var shade = new Path
        {
            Fill = new SolidColorBrush(Colors.Black.WithAlpha(ShadeAlpha)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, _everything, _hole),
        };
        Content = new Canvas { Children = { shade, _frame } };
        SizeChanged += (_, _) => _everything.Rect = new Rect(RenderSize);
    }

    public Int32Rect? Picked { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _desktop.X, _desktop.Y, _desktop.Width, _desktop.Height - RowLeftUncovered, KeepOrder);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Activate();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        _dragging = CaptureMouse();
        Outline(new Rect(_pressedAt, _pressedAt));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) Outline(new Rect(_pressedAt, e.GetPosition(this)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        var area = new Rect(_pressedAt, e.GetPosition(this));
        if (area.Width < SmallestSide || area.Height < SmallestSide)
        {
            Outline(Rect.Empty);
            return;
        }

        Point from = PointToScreen(area.TopLeft), to = PointToScreen(area.BottomRight);
        Picked = new Int32Rect((int)from.X, (int)from.Y, (int)(to.X - from.X), (int)(to.Y - from.Y));
        Close();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    void Outline(Rect area)
    {
        _hole.Rect = area;
        _frame.SetVisible(!area.IsEmpty);
        if (area.IsEmpty) return;

        Canvas.SetLeft(_frame, area.X);
        Canvas.SetTop(_frame, area.Y);
        _frame.Width = area.Width;
        _frame.Height = area.Height;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
