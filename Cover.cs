using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>
/// The cover of the track. A new one does not replace the old one on the spot: it pushes it out, the way the
/// playlist moves, the two passing through the frame side by side.
/// </summary>
public sealed class Cover : Grid
{
    const double Small = 0.88; // size the first cover grows from: it has no other to push out

    Border? _shown;

    public Cover() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

    /// <summary>Of the corners.</summary>
    public double Radius { get; set; }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        // the pictures slide inside the cover's own rounded square
        Clip = Squircle.Of(new Rect(info.NewSize), Radius);
    }

    /// <param name="art">Null leaves the cover empty: whatever lies under it shows.</param>
    /// <param name="direction">1: the next track, the pictures move left; -1: the previous one, they move right.</param>
    public void Show(ImageSource? art, int direction)
    {
        Border? old = _shown;
        Border? next = _shown = art != null ? Layer(art) : null;
        // off screen there is nobody to animate for
        if (!IsVisible || ActualWidth <= 0)
        {
            Children.Clear();
            if (next != null) Children.Add(next);
            return;
        }

        // both move as one strip, so what lies under the cover never shows between them
        double far = ActualWidth * (direction < 0 ? -1 : 1);
        Duration time = Ms(460);
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        if (old != null)
        {
            var leave = new DoubleAnimation(-far, time) { EasingFunction = ease };
            leave.Completed += (_, _) => Children.Remove(old);
            Slide(old).BeginAnimation(TranslateTransform.XProperty, leave);
            // with nothing coming in after it, it also fades: sliding off an empty frame alone would look cut
            if (next == null) old.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(240)));
        }
        if (next == null) return;

        Children.Add(next);
        if (old != null)
        {
            Slide(next).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(far, 0, time) { EasingFunction = ease });
            return;
        }

        var grow = new DoubleAnimation(Small, 1, time) { EasingFunction = ease };
        Transform size = ((TransformGroup)next.RenderTransform).Children[0];
        size.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        size.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(300)));
    }

    static Border Layer(ImageSource art) => new()
    {
        Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
    };

    static Transform Slide(Border layer) => ((TransformGroup)layer.RenderTransform).Children[1];

    static Duration Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}
