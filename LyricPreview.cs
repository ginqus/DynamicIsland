using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

sealed class LyricPreview : Grid
{
    static readonly string[] Lines = ["Одна строка", "за другой", "и ещё одна"];
    static readonly TimeSpan LineTime = TimeSpan.FromSeconds(1.7);

    readonly CompactLyric[] _blocks = [Block(), Block()];
    readonly DispatcherTimer _turns = new() { Interval = LineTime };
    int _line;

    public LyricPreview()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        foreach (CompactLyric block in _blocks) Children.Add(block);
        _turns.Tick += (_, _) => Turn(false);
        IsVisibleChanged += (_, _) =>
        {
            _turns.IsEnabled = IsVisible;
            if (IsVisible) Turn(true);
        };
    }

    public LyricChange Change { get; set; }

    static CompactLyric Block() => new()
    {
        Opacity = 0,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    void Turn(bool snap)
    {
        CompactLyric old = _blocks[_line % _blocks.Length];
        _line++;
        CompactLyric next = _blocks[_line % _blocks.Length];

        old.Leave(snap);
        next.Show(Lines[_line % Lines.Length], Brushes.White, double.PositiveInfinity, Change);
        next.Enter();
    }
}
