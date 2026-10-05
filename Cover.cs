using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class Cover : Grid
{
    const double FirstScale = 0.88;
    static readonly Duration SlideTime = Ms(460);

    Border? _shown;

    public Cover() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

    public double Radius { get; set; }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Clip = Squircle.Of(new Rect(info.NewSize), Radius);
    }

    public void Show(ImageSource? art, int direction)
    {
        Border? old = _shown;
        Border? next = _shown = art != null ? CreateLayer(art) : null;
        if (!IsVisible || ActualWidth <= 0)
        {
            Children.Clear();
            if (next != null) Children.Add(next);
            return;
        }

        double shift = ActualWidth * (direction < 0 ? -1 : 1);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        if (old != null)
        {
            var leave = new DoubleAnimation(-shift, SlideTime) { EasingFunction = ease };
            leave.Completed += (_, _) => Children.Remove(old);
            Offset(old).BeginAnimation(TranslateTransform.XProperty, leave);
            if (next == null) old.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(240)));
        }
        if (next == null) return;

        Children.Add(next);
        if (old != null)
        {
            Offset(next).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shift, 0, SlideTime) { EasingFunction = ease });
            return;
        }

        Scale(next).AnimateScale(new DoubleAnimation(FirstScale, 1, SlideTime) { EasingFunction = ease });
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
    }

    static Border CreateLayer(ImageSource art) => new()
    {
        Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
    };

    static Transform Scale(Border layer) => ((TransformGroup)layer.RenderTransform).Children[0];

    static Transform Offset(Border layer) => ((TransformGroup)layer.RenderTransform).Children[1];
}
