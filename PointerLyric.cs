using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using ContainerVisual = Windows.UI.Composition.ContainerVisual;
using Vector = System.Windows.Vector;

namespace DynamicIsland;

sealed class PointerLyric
{
    const double TextSize = 17;
    const double MaxTextWidth = 460;
    const string Ellipsis = "…";
    const char FirstJoinedLetter = '֐', LastJoinedLetter = 'ࣿ';

    const double SideGap = 34, BelowPointer = 8, ScreenMargin = 12, SideRoomToReturn = 160;
    const double FollowStiffness = 60, FollowDamping = 12;
    const double SideStiffness = 45, SideDamping = 12;
    const double ShownStiffness = 110, ShownDamping = 21, HiddenEnough = 0.01;

    const double FocalLength = 620, DimDepth = 820, NearestDepth = -260;
    const double SideYaw = 14, YawPerSpeed = 0.014, MaxSpeedYaw = 26;
    const double PitchPerSpeed = 0.014, MaxPitch = 18;
    const double RollPerSpeed = 0.004, MaxRoll = 5;
    const double DepthPerSpeed = 0.06, MaxSpeedDepth = 140;
    const double TrailSeconds = 0.045, MaxTrail = 60, TrailCurve = 1.6;
    const double SwingDepth = 220, HiddenDepth = 200, ThinnestLetter = 0.2;

    const double DriftRise = 3.5, DriftRiseSeconds = 3.7, DriftDepth = 26, DriftDepthSeconds = 5.3, DriftYaw = 4, DriftYawSeconds = 6.1;
    const double RippleDepth = 6, RippleRise = 1.2, RippleLength = 190, RippleSeconds = 2.6, RippleRiseStretch = 0.7;

    const double EnterDepth = 300, EnterRise = 14, EnterSeconds = 0.62, EnterStepSeconds = 0.024, EnterSpreadSeconds = 0.36;
    const double LeaveDepth = 260, LeaveRise = 18, LeaveSeconds = 0.44;

    const double ColumnTextSize = 19, ColumnMaxWidth = 240, ColumnPrintPitch = 2.4;
    const double ColumnSideGap = 26, ColumnBelowPointer = 14, ColumnHalfHeight = 60;
    const double NearRowPitch = 25, FarRowPitch = 19, FarWordScale = 0.72;
    const double PastWordOpacity = 0.34, NextWordOpacity = 0.5, PastRows = 2, NextRows = 3;
    const double ScrollStiffness = 170, ScrollDamping = 19, WordTrailPerRow = 0.5, ColumnEnterSeconds = 0.3;
    const int GluedLetters = 2;

    const double PrintMargin = 14, PrintDpi = 96;
    const double ShadowBlur = 12, ShadowDrop = 2, ShadowDown = 270, ShadowOpacity = 0.7;
    static readonly TimeSpan HiddenPoll = TimeSpan.FromMilliseconds(60);

    sealed record Letter(SpriteVisual Visual, Vector2 Pivot, double X, double AppearAt);

    sealed record Word(SpriteVisual Visual, Vector2 Pivot, double Width, double Start);

    sealed record Line(string Text, PointerLyricShape Shape, ContainerVisual Visual, CompositionDrawingSurface Surface,
        Letter[] Letters, Word[] Words, double Width, double Scale, double BuiltAt)
    {
        public Spring Scroll { get; } = new(-1, ScrollStiffness, ScrollDamping);

        public double StartedAt { get; set; } = BuiltAt;

        public double Seconds { get; set; }

        public double LeftAt { get; set; } = double.NaN;

        public bool IsLeaving => !double.IsNaN(LeftAt);
    }

    readonly record struct Pose(double Side, double Yaw, double Pitch, double Roll, double Depth, double Rise, Vector Speed, double Shown, double Scale, Point Anchor);

    readonly IntPtr _hwnd;
    readonly Compositor _compositor;
    readonly DesktopWindowTarget _target;
    readonly ContainerVisual _root;
    readonly DrawingSurfaces _surfaces;
    readonly FrameLoop _loop;
    readonly DispatcherTimer _hiddenPoll = new() { Interval = HiddenPoll };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Spring _followX = new(0, FollowStiffness, FollowDamping), _followY = new(0, FollowStiffness, FollowDamping);
    readonly Spring _side = new(1, SideStiffness, SideDamping);
    readonly Spring _shown = new(1, ShownStiffness, ShownDamping);
    readonly Func<bool> _isPointerOverIsland;
    readonly Typeface _typeface;
    Line? _line, _leaving;
    PointerLyricShape _shape = Settings.PointerLyricShape;
    int _sideTarget = 1;
    double _scale = 1;
    Int32Rect _screen;

    public PointerLyric(FontFamily font, Func<bool> isPointerOverIsland)
    {
        _typeface = new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        _isPointerOverIsland = isPointerOverIsland;

        Native.EnsureDispatcherQueue();
        _hwnd = Native.CreateOverlayWindow();
        _compositor = new Compositor();
        _target = DesktopComposition.TargetFor(_compositor, _hwnd);
        _root = _compositor.CreateContainerVisual();
        _target.Root = _root;
        _surfaces = new DrawingSurfaces(_compositor);

        _loop = new FrameLoop(Advance);
        _hiddenPoll.Tick += (_, _) => WakeIfRevealed();
    }

    public IntPtr Handle => _hwnd;

    public string Text => _line?.Text ?? "";

    double Now => _clock.Elapsed.TotalSeconds;

    bool InColumn => _shape == PointerLyricShape.Column;

    double Gap => InColumn ? ColumnSideGap : SideGap;

    double Below => InColumn ? ColumnBelowPointer : BelowPointer;

    double HalfHeight => InColumn ? ColumnHalfHeight : TextSize;

    public void ShowLine(string text, double sung, double seconds)
    {
        if (text == Text)
        {
            if (_line != null) Retime(_line, sung, seconds);
            return;
        }

        _shape = Settings.PointerLyricShape;
        bool wasEmpty = _line == null && _leaving == null;
        if (wasEmpty && text.Length > 0) Settle(Native.CursorPosition, text);
        Replace(text.Length > 0 ? Build(text) : null);
        if (_line != null) Retime(_line, sung, seconds);
    }

    public void Reshape()
    {
        if (_line == null || _line.Shape == Settings.PointerLyricShape) return;

        _shape = Settings.PointerLyricShape;
        Line line = Build(_line.Text);
        Retime(line, Now - _line.StartedAt, _line.Seconds);
        Replace(line);
    }

    void Replace(Line? line)
    {
        if (_leaving != null) Discard(_leaving);
        _leaving = _line;
        if (_leaving != null) _leaving.LeftAt = Now;
        _line = line;

        _hiddenPoll.Stop();
        _loop.Start();
    }

    void Retime(Line line, double sung, double seconds)
    {
        line.StartedAt = Now - sung;
        line.Seconds = seconds;
    }

    void Settle(Point pointer, string text)
    {
        CoverScreen();
        Native.ShowQuietly(_hwnd);
        _scale = Native.ScaleAt(pointer);
        _shown.Snap(_isPointerOverIsland() ? 0 : 1);
        double width = WidthOf(text);
        ChooseSide(pointer, width, true);
        SnapTo(pointer, width);
    }

    void CoverScreen()
    {
        Int32Rect screen = Native.VirtualScreen;
        if (screen == _screen) return;
        _screen = screen;
        Native.MoveAndSize(_hwnd, screen.X, screen.Y, screen.Width, screen.Height);
    }

    void SnapTo(Point pointer, double width)
    {
        Point spot = SpotNear(pointer, width);
        _followX.Snap(spot.X);
        _followY.Snap(spot.Y);
        _side.Snap(_sideTarget);
    }

    double TextWidth => _line?.Width ?? _leaving?.Width ?? 0;

    void ChooseSide(Point pointer, double width, bool snap)
    {
        Rect work = Native.WorkAreaAt(pointer);
        if (work.IsEmpty) return;

        double need = (Gap + width + ScreenMargin) * _scale, toReturn = SideRoomToReturn * _scale;
        double right = work.Right - pointer.X, left = pointer.X - work.Left;
        if (snap) _sideTarget = right >= need || right >= left ? 1 : -1;
        else if (_sideTarget > 0 && right < need && left > right) _sideTarget = -1;
        else if (_sideTarget < 0 && (right >= need + toReturn || (left < need && right > left))) _sideTarget = 1;
        _side.Target = _sideTarget;
    }

    Point SpotNear(Point pointer, double width)
    {
        double halfHeight = HalfHeight * _scale;
        double x = pointer.X + _sideTarget * (Gap + width / 2) * _scale, y = pointer.Y + Below * _scale;

        Rect work = Native.WorkAreaAt(pointer);
        if (work.IsEmpty) return new Point(x, y);

        double margin = ScreenMargin * _scale, reach = width / 2 * _scale + margin;
        x = work.Width > 2 * reach ? Math.Clamp(x, work.Left + reach, work.Right - reach) : work.Left + work.Width / 2;
        y = Math.Clamp(y, work.Top + halfHeight + margin, Math.Max(work.Top + halfHeight + margin, work.Bottom - halfHeight - margin));
        return new Point(x, y);
    }

    bool Advance(double dt)
    {
        double now = Now, width = TextWidth;
        CoverScreen();
        Point pointer = Native.CursorPosition;
        _scale = Native.ScaleAt(pointer);
        bool hide = _isPointerOverIsland();
        _shown.Target = hide ? 0 : 1;
        bool busy = _shown.Advance(dt);

        ChooseSide(pointer, width, false);
        Point spot = SpotNear(pointer, width);
        _followX.Target = spot.X;
        _followY.Target = spot.Y;
        busy |= _followX.Advance(dt) | _followY.Advance(dt) | _side.Advance(dt);
        if (_line != null) busy |= Scroll(_line, now, dt);
        if (_leaving != null) Scroll(_leaving, now, dt);

        if (_leaving != null && now - _leaving.LeftAt >= LeaveSeconds)
        {
            Discard(_leaving);
            _leaving = null;
        }
        if (_line == null && _leaving == null)
        {
            Native.Hide(_hwnd);
            return false;
        }
        busy |= _leaving != null || (_line != null && IsAppearing(_line, now));

        Pose pose = PoseAt(now, new Vector(_followX.Velocity, _followY.Velocity) / _scale);
        if (_line != null) Arrange(_line, pose, now);
        if (_leaving != null) Arrange(_leaving, pose, now);

        if (!hide || _shown.Value >= HiddenEnough || busy) return true;
        _hiddenPoll.Start();
        return false;
    }

    void WakeIfRevealed()
    {
        if (_isPointerOverIsland()) return;
        _hiddenPoll.Stop();
        Point pointer = Native.CursorPosition;
        _scale = Native.ScaleAt(pointer);
        SnapTo(pointer, TextWidth);
        _loop.Start();
    }

    Pose PoseAt(double now, Vector speed)
    {
        double side = _side.Value, shown = _shown.Value;
        double yaw = side * SideYaw + Math.Clamp(speed.X * YawPerSpeed, -MaxSpeedYaw, MaxSpeedYaw) + DriftYaw * Wave(now, DriftYawSeconds);
        double pitch = Math.Clamp(-speed.Y * PitchPerSpeed, -MaxPitch, MaxPitch);
        double roll = Math.Clamp(-speed.X * RollPerSpeed, -MaxRoll, MaxRoll);
        double depth = Math.Min(speed.Length * DepthPerSpeed, MaxSpeedDepth) + SwingDepth * (1 - side * side)
            + HiddenDepth * (1 - shown) + DriftDepth * Wave(now, DriftDepthSeconds);
        var anchor = new Point(_followX.Value - _screen.X, _followY.Value - _screen.Y);
        return new Pose(side, Radians(yaw), Radians(pitch), Radians(roll), depth, DriftRise * Wave(now, DriftRiseSeconds), speed, shown, _scale, anchor);
    }

    static bool Scroll(Line line, double now, double dt)
    {
        if (line.Words.Length == 0) return false;

        double sung = line.Seconds > 0 ? (now - line.StartedAt) / line.Seconds : 0;
        if (!line.IsLeaving) line.Scroll.Target = WordAt(line.Words, sung);
        return line.Scroll.Advance(dt);
    }

    static int WordAt(Word[] words, double sung)
    {
        int index = words.Length - 1;
        while (index > 0 && words[index].Start > sung) index--;
        return index;
    }

    static void Arrange(Line line, Pose pose, double now)
    {
        if (line.Shape == PointerLyricShape.Column) ArrangeWords(line, pose, now);
        else ArrangeLetters(line, pose, now);
    }

    static void ArrangeWords(Line line, Pose pose, double now)
    {
        double cosRoll = Math.Cos(pose.Roll), sinRoll = Math.Sin(pose.Roll);
        double gone = line.IsLeaving ? Math.Min((now - line.LeftAt) / LeaveSeconds, 1) : 0;
        double appeared = 1 - Math.Pow(1 - Math.Clamp((now - line.BuiltAt) / ColumnEnterSeconds, 0, 1), 3);
        double resize = pose.Scale / line.Scale;
        double align = (1 - pose.Side) / 2, nearEdge = -pose.Side * line.Width / 2;

        for (int i = 0; i < line.Words.Length; i++)
        {
            Word word = line.Words[i];
            double row = i - line.Scroll.Value, size = 1 - (1 - FarWordScale) * Math.Min(Math.Abs(row), 1);
            Vector trail = -pose.Speed * TrailSeconds * Math.Min(Math.Abs(row) * WordTrailPerRow, 1);
            if (trail.Length > MaxTrail) trail *= MaxTrail / trail.Length;

            double x = nearEdge - align * word.Width * size + trail.X;
            double y = RowOffset(row) + pose.Rise - NearRowPitch * gone + trail.Y;
            word.Visual.TransformMatrix = Placed(word.Pivot, size * resize, size * resize, cosRoll, sinRoll, pose.Anchor, x * pose.Scale, y * pose.Scale);
            word.Visual.Opacity = (float)(RowOpacity(row) * appeared * (1 - gone) * pose.Shown);
        }
    }

    static double RowOffset(double row)
    {
        double far = Math.Abs(row);
        return Math.Sign(row) * (Math.Min(far, 1) * NearRowPitch + Math.Max(far - 1, 0) * FarRowPitch);
    }

    static double RowOpacity(double row)
    {
        double far = Math.Abs(row), near = row < 0 ? PastWordOpacity : NextWordOpacity, rows = row < 0 ? PastRows : NextRows;
        return far <= 1 ? 1 + (near - 1) * far : Math.Max(near * (1 - (far - 1) / (rows - 1)), 0);
    }

    static Matrix4x4 Placed(Vector2 pivot, double scaleX, double scaleY, double cosRoll, double sinRoll, Point anchor, double flatX, double flatY) =>
        Matrix4x4.CreateTranslation(-pivot.X, -pivot.Y, 0) * new Matrix4x4(
            (float)(scaleX * cosRoll), (float)(scaleX * sinRoll), 0, 0,
            (float)(-scaleY * sinRoll), (float)(scaleY * cosRoll), 0, 0,
            0, 0, 1, 0,
            (float)(anchor.X + flatX * cosRoll - flatY * sinRoll), (float)(anchor.Y + flatX * sinRoll + flatY * cosRoll), 0, 1);

    static void ArrangeLetters(Line line, Pose pose, double now)
    {
        double cosYaw = Math.Cos(pose.Yaw), sinYaw = Math.Sin(pose.Yaw);
        double cosPitch = Math.Cos(pose.Pitch), sinPitch = Math.Sin(pose.Pitch);
        double cosRoll = Math.Cos(pose.Roll), sinRoll = Math.Sin(pose.Roll);
        double gone = line.IsLeaving ? Math.Min((now - line.LeftAt) / LeaveSeconds, 1) : 0;
        double ripplePhase = 2 * Math.PI * now / RippleSeconds;
        double resize = pose.Scale / line.Scale;

        foreach (Letter letter in line.Letters)
        {
            double appeared = 1 - Math.Pow(1 - Math.Clamp((now - letter.AppearAt) / EnterSeconds, 0, 1), 3);
            double fromPointer = Math.Clamp((pose.Side * letter.X + line.Width / 2) / Math.Max(line.Width, 1), 0, 1);
            double ripple = 2 * Math.PI * letter.X / RippleLength - ripplePhase;

            double y = pose.Rise + RippleRise * Math.Sin(ripple * RippleRiseStretch);
            double turnedX = letter.X * cosYaw, turnedZ = letter.X * sinYaw;
            double tiltedY = y * cosPitch - turnedZ * sinPitch, tiltedZ = y * sinPitch + turnedZ * cosPitch;
            double depth = Math.Max(tiltedZ + RippleDepth * Math.Sin(ripple) + pose.Depth + EnterDepth * (1 - appeared) + LeaveDepth * gone * gone, NearestDepth);
            tiltedY += EnterRise * (1 - appeared) - LeaveRise * gone;

            double near = FocalLength / (FocalLength + depth);
            Vector trail = -pose.Speed * TrailSeconds * Math.Pow(fromPointer, TrailCurve);
            if (trail.Length > MaxTrail) trail *= MaxTrail / trail.Length;
            double flatX = (turnedX * near + trail.X) * pose.Scale, flatY = (tiltedY * near + trail.Y) * pose.Scale;

            double scaleX = near * Math.Max(cosYaw, ThinnestLetter) * resize, scaleY = near * cosPitch * resize;
            letter.Visual.TransformMatrix = Placed(letter.Pivot, scaleX, scaleY, cosRoll, sinRoll, pose.Anchor, flatX, flatY);
            letter.Visual.Opacity = (float)(appeared * (1 - gone) * pose.Shown * Math.Clamp(1 - depth / DimDepth, 0, 1));
        }
    }

    static bool IsAppearing(Line line, double now) => line.Letters.Length > 0 && now - line.Letters.Max(letter => letter.AppearAt) < EnterSeconds;

    static double Wave(double now, double seconds) => Math.Sin(2 * Math.PI * now / seconds);

    static double Radians(double degrees) => degrees * Math.PI / 180;

    void Discard(Line line)
    {
        _root.Children.Remove(line.Visual);
        foreach (Letter letter in line.Letters) letter.Visual.Dispose();
        foreach (Word word in line.Words) word.Visual.Dispose();
        line.Visual.Dispose();
        line.Surface.Dispose();
    }

    Line Build(string text) => InColumn ? BuildColumn(text) : BuildLine(text);

    Line BuildColumn(string text)
    {
        double scale = _scale;
        FormattedText[] words = [.. Words(text).Select(word => Format(Fit(word, ColumnTextSize, ColumnMaxWidth, scale), ColumnTextSize, scale))];
        double pitch = ColumnTextSize * ColumnPrintPitch, wordHeight = words.Max(word => word.Height);
        double width = words.Max(word => word.WidthIncludingTrailingWhitespace), height = pitch * (words.Length - 1) + wordHeight;

        BitmapSource print = Print(words.Select((word, i) => (word, new Point(PrintMargin, PrintMargin + i * pitch))), width, height, scale);
        CompositionDrawingSurface surface = _surfaces.Create(print);
        ContainerVisual visual = _compositor.CreateContainerVisual();
        _root.Children.InsertAtTop(visual);

        int[] edges = [0, .. Enumerable.Range(1, words.Length - 1).Select(i => PrintPixel(i * pitch - (pitch - wordHeight) / 2, scale)), print.PixelHeight];
        double[] weights = [.. words.Select(word => word.Text.Count(char.IsLetterOrDigit) + 1.0)];
        double total = weights.Sum(), start = Now;

        Word[] placed = [.. words.Select((word, i) =>
        {
            int right = Math.Min(PrintPixel(word.WidthIncludingTrailingWhitespace + PrintMargin, scale), print.PixelWidth);
            SpriteVisual sprite = CreateSprite(surface, 0, edges[i], right, edges[i + 1]);
            visual.Children.InsertAtTop(sprite);
            var pivot = new Vector2(PrintPixel(0, scale), PrintPixel(i * pitch + wordHeight / 2, scale) - edges[i]);
            return new Word(sprite, pivot, word.WidthIncludingTrailingWhitespace, weights.Take(i).Sum() / total);
        })];
        return new Line(text, PointerLyricShape.Column, visual, surface, [], placed, width, scale, start);
    }

    static IEnumerable<string> Words(string text)
    {
        string glued = "";
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            glued = glued.Length > 0 ? glued + " " + word : word;
            if (glued.Count(char.IsLetterOrDigit) <= GluedLetters) continue;
            yield return glued;
            glued = "";
        }
        if (glued.Length > 0) yield return glued;
    }

    double WidthOf(string text) => InColumn
        ? Words(text).Select(word => Math.Min(Measure(word, ColumnTextSize, _scale), ColumnMaxWidth)).DefaultIfEmpty(0).Max()
        : Math.Min(Measure(text, TextSize, _scale), MaxTextWidth);

    Line BuildLine(string text)
    {
        double scale = _scale;
        string shown = Fit(text, TextSize, MaxTextWidth, scale);
        FormattedText whole = Format(shown, TextSize, scale);
        double width = whole.WidthIncludingTrailingWhitespace, height = whole.Height;

        BitmapSource print = Print([(whole, new Point(PrintMargin, PrintMargin))], width, height, scale);
        CompositionDrawingSurface surface = _surfaces.Create(print);
        ContainerVisual visual = _compositor.CreateContainerVisual();
        _root.Children.InsertAtTop(visual);

        Rect[] boxes = [.. Split(shown)
            .Select(piece => whole.BuildHighlightGeometry(new Point(), piece.Start, piece.Length)?.Bounds ?? Rect.Empty)
            .Where(box => !box.IsEmpty)];
        int[] edges = [0, .. boxes.Skip(1).Select((box, i) => PrintPixel((boxes[i].Right + box.Left) / 2, scale)), print.PixelWidth];
        double step = Math.Min(EnterStepSeconds, EnterSpreadSeconds / Math.Max(boxes.Length, 1)), start = Now;
        float pivotY = (float)((PrintMargin + height / 2) * scale);

        Letter[] letters = [.. boxes.Select((box, i) =>
        {
            double middle = box.Left + box.Width / 2;
            int fromPointer = _sideTarget > 0 ? i : boxes.Length - 1 - i;
            SpriteVisual sprite = CreateSprite(surface, edges[i], 0, edges[i + 1], print.PixelHeight);
            visual.Children.InsertAtTop(sprite);
            var pivot = new Vector2(PrintPixel(middle, scale) - edges[i], pivotY);
            return new Letter(sprite, pivot, middle - width / 2, start + fromPointer * step);
        })];
        return new Line(text, PointerLyricShape.Line, visual, surface, letters, [], width, scale, start);
    }

    static int PrintPixel(double x, double scale) => (int)Math.Round((x + PrintMargin) * scale);

    SpriteVisual CreateSprite(CompositionDrawingSurface surface, int left, int top, int right, int bottom)
    {
        CompositionSurfaceBrush brush = _compositor.CreateSurfaceBrush(surface);
        brush.Stretch = CompositionStretch.None;
        brush.HorizontalAlignmentRatio = 0;
        brush.VerticalAlignmentRatio = 0;
        brush.Offset = new Vector2(-left, -top);
        SpriteVisual sprite = _compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        sprite.Size = new Vector2(right - left, bottom - top);
        sprite.Opacity = 0;
        return sprite;
    }

    static BitmapSource Print(IEnumerable<(FormattedText Text, Point At)> texts, double width, double height, double scale)
    {
        var words = new DrawingVisual
        {
            Effect = new DropShadowEffect
            {
                BlurRadius = ShadowBlur, ShadowDepth = ShadowDrop, Direction = ShadowDown, Opacity = ShadowOpacity, Color = Colors.Black,
            },
        };
        TextOptions.SetTextRenderingMode(words, TextRenderingMode.Grayscale);
        using (DrawingContext dc = words.RenderOpen())
        {
            foreach ((FormattedText text, Point at) in texts) dc.DrawText(text, at);
        }
        var holder = new System.Windows.Media.ContainerVisual();
        holder.Children.Add(words);

        var print = new RenderTargetBitmap((int)Math.Ceiling((width + 2 * PrintMargin) * scale), (int)Math.Ceiling((height + 2 * PrintMargin) * scale),
            PrintDpi * scale, PrintDpi * scale, PixelFormats.Pbgra32);
        print.Render(holder);
        return print;
    }

    static IEnumerable<(int Start, int Length)> Split(string text)
    {
        if (text.Any(letter => letter is >= FirstJoinedLetter and <= LastJoinedLetter)) return [(0, text.Length)];

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return starts
            .Select((start, i) => (start, (i + 1 < starts.Length ? starts[i + 1] : text.Length) - start))
            .Where(letter => !char.IsWhiteSpace(text[letter.start]));
    }

    double Measure(string text, double size, double pixelsPerDip) => Format(text, size, pixelsPerDip).WidthIncludingTrailingWhitespace;

    string Fit(string text, double size, double maxWidth, double pixelsPerDip)
    {
        if (Measure(text, size, pixelsPerDip) <= maxWidth) return text;

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int kept = starts.Length - 1;
        string cut;
        do cut = text[..starts[kept]].TrimEnd() + Ellipsis;
        while (Measure(cut, size, pixelsPerDip) > maxWidth && kept-- > 0);
        return cut;
    }

    FormattedText Format(string text, double size, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, size, Brushes.White, pixelsPerDip);
}
