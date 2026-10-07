using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

public sealed class Segments : FrameworkElement
{
    const double FontSize = 12;
    const double CellPadding = 7, KnobInset = 2;
    const double TrackRadius = 9, KnobRadius = 7;
    const double TrackOpacity = 0.1, KnobOpacity = 0.2;
    const double RestingText = 0.6;
    const double StretchSpeed = 9, StretchWider = 0.18, StretchFlatter = 0.08;

    readonly Spring _at = new(0, 520, 30);
    readonly FrameLoop _loop;
    string[] _labels = [];
    int _picked;

    public Segments()
    {
        _loop = new FrameLoop(Advance);
    }

    public string Labels
    {
        get => string.Join('|', _labels);
        set
        {
            _labels = value.Split('|');
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public void Set(int picked, bool animate)
    {
        _picked = picked;
        if (animate)
        {
            _at.Target = picked;
            _loop.Start();
        }
        else
        {
            _at.Snap(picked);
            InvalidateVisual();
        }
    }

    public int PickUnderPointer()
    {
        Point pointer = Mouse.GetPosition(this);
        bool inside = pointer.X >= 0 && pointer.X < ActualWidth && pointer.Y >= 0 && pointer.Y < ActualHeight;
        if (!inside) return (_picked + 1) % _labels.Length;
        return Math.Clamp((int)((pointer.X - KnobInset) / CellWidth(ActualWidth)), 0, _labels.Length - 1);
    }

    protected override Size MeasureOverride(Size available)
    {
        double widest = _labels.Length == 0 ? 0 : _labels.Max(label => Format(label, Brushes.White).Width);
        return new Size(Math.Ceiling(widest + CellPadding * 2) * _labels.Length + KnobInset * 2, 0);
    }

    double CellWidth(double width) => (width - KnobInset * 2) / Math.Max(_labels.Length, 1);

    bool Advance(double dt)
    {
        bool moving = _at.Advance(dt);
        InvalidateVisual();
        return moving;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _labels.Length == 0) return;

        double cell = CellWidth(w), stretch = Math.Min(1, Math.Abs(_at.Velocity) / StretchSpeed);
        double knobWidth = cell * (1 + StretchWider * stretch), knobHeight = (h - KnobInset * 2) * (1 - StretchFlatter * stretch);
        double cx = KnobInset + cell * (_at.Value + 0.5);

        dc.DrawRoundedRectangle(Shades.White(TrackOpacity), null, new Rect(0, 0, w, h), TrackRadius, TrackRadius);
        dc.DrawRoundedRectangle(Shades.White(KnobOpacity), null,
            new Rect(cx - knobWidth / 2, (h - knobHeight) / 2, knobWidth, knobHeight), KnobRadius, KnobRadius);

        for (int i = 0; i < _labels.Length; i++)
        {
            double near = 1 - Math.Clamp(Math.Abs(_at.Value - i), 0, 1);
            FormattedText label = Format(_labels[i], Shades.White(RestingText + (1 - RestingText) * near));
            dc.DrawText(label, new Point(KnobInset + cell * (i + 0.5) - label.Width / 2, (h - label.Height) / 2));
        }
    }

    FormattedText Format(string text, Brush fill) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
        FontSize, fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
