using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public sealed class CompactLyric : FrameworkElement
{
    const double FontSize = 12;
    const string Ellipsis = "…";
    const char FirstJoinedLetter = '֐', LastJoinedLetter = 'ࣿ';

    const double LineBlur = 6, LineRise = 10;
    const double LineLeaveMs = 260, LineLeaveFadeMs = 200, LineLeaveBlurMs = 220;
    const double LineEnterMs = 380, LineEnterFadeMs = 300;

    const double LetterBlur = 4, LetterRise = 8, LetterDrop = 9, LetterShrink = 0.9;
    const double LetterLeaveMs = 150, LetterLeaveStepMs = 8, LetterLeaveSpreadMs = 100;
    const double LetterEnterMs = 340, LetterEnterWaitMs = 130, LetterEnterStepMs = 14, LetterEnterSpreadMs = 220;

    const double WordTurnMs = 300, WordStepMs = 35;

    readonly record struct Pose(double Opacity, double Y, double ScaleX, double ScaleY, double Blur);

    sealed class TurnEase(bool rising) : EasingFunctionBase
    {
        protected override double EaseInCore(double time)
        {
            double angle = time * time * (3 - 2 * time) * Math.PI / 2;
            return rising ? Math.Sin(angle) : 1 - Math.Cos(angle);
        }

        protected override Freezable CreateInstanceCore() => new TurnEase(rising);
    }

    sealed class Piece(DrawingVisual visual, Brush fill, ScaleTransform size, TranslateTransform shift)
    {
        public void Glide(Pose from, Pose to, TimeSpan wait, TimeSpan run, IEasingFunction travel, IEasingFunction swell, IEasingFunction fade)
        {
            fill.BeginAnimation(Brush.OpacityProperty, Delayed(from.Opacity, to.Opacity, wait, run, fade));
            shift.BeginAnimation(TranslateTransform.YProperty, Delayed(from.Y, to.Y, wait, run, travel));
            size.BeginAnimation(ScaleTransform.ScaleXProperty, Delayed(from.ScaleX, to.ScaleX, wait, run, swell));
            size.BeginAnimation(ScaleTransform.ScaleYProperty, Delayed(from.ScaleY, to.ScaleY, wait, run, swell));
            if (from.Blur == to.Blur) return;

            var blur = new BlurEffect { Radius = from.Blur };
            visual.Effect = blur;
            DoubleAnimationUsingKeyFrames change = Delayed(from.Blur, to.Blur, wait, run, fade);
            if (to.Blur == 0)
            {
                change.Completed += (_, _) =>
                {
                    if (ReferenceEquals(visual.Effect, blur)) visual.Effect = null;
                };
            }
            blur.BeginAnimation(BlurEffect.RadiusProperty, change);
        }
    }

    static readonly Pose Rest = new(1, 0, 1, 1, 0);
    static readonly Pose LetterBelow = new(0, LetterDrop, LetterShrink, LetterShrink, LetterBlur);
    static readonly Pose LetterAbove = new(0, -LetterRise, LetterShrink, LetterShrink, LetterBlur);
    static readonly IEasingFunction Sink = new CubicEase { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction Settle = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction Launch = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction Land = new QuinticEase { EasingMode = EasingMode.EaseOut };
    static readonly IEasingFunction TurnSine = new TurnEase(true) { EasingMode = EasingMode.EaseIn };
    static readonly IEasingFunction TurnCosine = new TurnEase(false) { EasingMode = EasingMode.EaseIn };

    readonly VisualCollection _visuals;
    Piece[] _pieces = [];
    Size _size;
    LyricChange _change;

    public CompactLyric()
    {
        _visuals = new VisualCollection(this);
        RenderTransform = Offset;
    }

    public string Text { get; private set; } = "";

    public TranslateTransform Offset { get; } = new();

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size available) => _size;

    internal void Show(string text, Brush brush, double maxWidth, LyricChange change)
    {
        Text = text;
        string shown = Fit(text, maxWidth);
        FormattedText whole = Format(shown, brush);
        _size = new Size(whole.WidthIncludingTrailingWhitespace, whole.Height);
        _change = shown.Any(letter => letter is >= FirstJoinedLetter and <= LastJoinedLetter) ? LyricChange.Smooth : change;

        _visuals.Clear();
        _pieces = [.. Split(shown, _change).Select(part => CreatePiece(whole, shown, part.Start, part.Length, brush))];
        InvalidateMeasure();
    }

    internal void Enter()
    {
        BeginAnimation(OpacityProperty, null);
        Offset.BeginAnimation(TranslateTransform.YProperty, null);
        Effect = null;
        Opacity = 1;

        switch (_change)
        {
            case LyricChange.Wave:
                Stagger(LetterBelow, Rest, LetterEnterWaitMs, StepWithin(LetterEnterStepMs, LetterEnterSpreadMs), LetterEnterMs, Land, Land, Settle);
                break;
            case LyricChange.Drum:
                Stagger(WordEdgeOn(1), Rest, 0, WordStepMs, WordTurnMs, TurnCosine, TurnSine, TurnSine);
                break;
            default:
                this.AnimateBlur(LineBlur, 0, Ms(LineEnterFadeMs), Settle);
                Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(LineRise, 0, Ms(LineEnterMs)) { EasingFunction = Settle });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(LineEnterFadeMs)));
                break;
        }
    }

    internal void Leave(bool snap)
    {
        if (snap)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            return;
        }

        switch (_change)
        {
            case LyricChange.Wave:
                Stagger(Rest, LetterAbove, 0, StepWithin(LetterLeaveStepMs, LetterLeaveSpreadMs), LetterLeaveMs, Launch, Launch, Launch);
                break;
            case LyricChange.Drum:
                Stagger(Rest, WordEdgeOn(-1), 0, WordStepMs, WordTurnMs, TurnSine, TurnCosine, TurnCosine);
                break;
            default:
                this.AnimateBlur(0, LineBlur, Ms(LineLeaveBlurMs), keep: true);
                Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-LineRise, Ms(LineLeaveMs)) { EasingFunction = Sink });
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(LineLeaveFadeMs)));
                break;
        }
    }

    Pose WordEdgeOn(int side) => new(0, side * _size.Height / 2, 1, 0, 0);

    void Stagger(Pose from, Pose to, double waitMs, double stepMs, double runMs,
        IEasingFunction travel, IEasingFunction swell, IEasingFunction fade)
    {
        TimeSpan run = TimeSpan.FromMilliseconds(runMs);
        for (int i = 0; i < _pieces.Length; i++)
            _pieces[i].Glide(from, to, TimeSpan.FromMilliseconds(waitMs + i * stepMs), run, travel, swell, fade);
    }

    double StepWithin(double longestMs, double spreadMs) => Math.Min(longestMs, spreadMs / Math.Max(_pieces.Length, 1));

    static IEnumerable<(int Start, int Length)> Split(string text, LyricChange change)
    {
        if (change == LyricChange.Drum) return Regex.Matches(text, @"\S+").Select(word => (word.Index, word.Length));
        if (change != LyricChange.Wave) return text.Length > 0 ? [(0, text.Length)] : [];

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return starts
            .Select((start, i) => (start, (i + 1 < starts.Length ? starts[i + 1] : text.Length) - start))
            .Where(letter => !char.IsWhiteSpace(text[letter.start]));
    }

    Piece CreatePiece(FormattedText whole, string text, int start, int length, Brush brush)
    {
        Rect box = whole.BuildHighlightGeometry(new Point(), start, length)?.Bounds ?? new Rect();
        Brush fill = brush.Clone();
        var size = new ScaleTransform(1, 1, box.Left + box.Width / 2, _size.Height / 2);
        var shift = new TranslateTransform();
        var visual = new DrawingVisual { Transform = new TransformGroup { Children = { size, shift } } };
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Animated);
        using (DrawingContext dc = visual.RenderOpen()) dc.DrawText(Format(text.Substring(start, length), fill), new Point(box.Left, 0));
        _visuals.Add(visual);
        return new Piece(visual, fill, size, shift);
    }

    string Fit(string text, double maxWidth)
    {
        if (double.IsInfinity(maxWidth) || Format(text, Brushes.White).WidthIncludingTrailingWhitespace <= maxWidth) return text;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int kept = starts.Length - 1;
        string cut;
        do cut = text[..starts[kept]].TrimEnd() + Ellipsis;
        while (Format(cut, Brushes.White).WidthIncludingTrailingWhitespace > maxWidth && kept-- > 0);
        return cut;
    }

    FormattedText Format(string text, Brush fill) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
        FontSize, fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
