using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DynamicIsland;

static class Motion
{
    public static Duration Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    public static void AnimateScale(this Transform scale, AnimationTimeline? animation)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    public static void AnimateBlur(this UIElement element, double from, double to, Duration time,
        IEasingFunction? ease = null, TimeSpan delay = default, bool keep = false)
    {
        var blur = new BlurEffect { Radius = from };
        element.Effect = blur;
        var change = new DoubleAnimation(to, time) { BeginTime = delay, EasingFunction = ease };
        if (!keep)
        {
            change.Completed += (_, _) =>
            {
                if (ReferenceEquals(element.Effect, blur)) element.Effect = null;
            };
        }
        blur.BeginAnimation(BlurEffect.RadiusProperty, change);
    }

    public static DoubleAnimationUsingKeyFrames Delayed(double from, double to, TimeSpan wait, TimeSpan run, IEasingFunction? ease = null)
    {
        DoubleAnimationUsingKeyFrames animation = Held(from, wait, run);
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(wait + run), ease));
        return animation;
    }

    public static DoubleAnimationUsingKeyFrames Sway(TimeSpan wait, TimeSpan run, params double[] sides)
    {
        DoubleAnimationUsingKeyFrames animation = Held(0, wait, run);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (int i = 0; i < sides.Length; i++)
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(sides[i], KeyTime.FromTimeSpan(wait + run * ((i + 1.0) / sides.Length)), ease));
        return animation;
    }

    static DoubleAnimationUsingKeyFrames Held(double value, TimeSpan wait, TimeSpan run)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = wait + run };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(value, KeyTime.FromTimeSpan(wait)));
        return animation;
    }
}
