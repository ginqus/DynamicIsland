using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using static DynamicIsland.Motion;

namespace DynamicIsland;

public partial class MainWindow
{
    const double TileGap = 4;
    const double TilePitch = ShelfTile.TileWidth + TileGap;
    const double ShelfEdgeFade = 16;
    const double TileBlur = 8;
    const double CarriedOpacity = 0.35;
    const double DropZoneOpacity = 0.5, HoverZoneOpacity = 0.25;
    static readonly TimeSpan DragLeaveDelay = TimeSpan.FromMilliseconds(150);

    readonly Shelf _shelf;
    readonly Dictionary<Shelf.Item, ShelfTile> _tiles = [];
    readonly Spring _shelfScroll = new(0, 260, 30);
    readonly DelayedAction _dragLeaveTimeout;
    int _shownShelfCount;
    bool _dragOver, _draggingOut, _pickingFiles;
    Panel _panelBeforeDrag;
    ShelfTile? _pressedTile;
    Point _tilePressPoint;

    double ShelfOverflow => Math.Max(_shelf.Items.Count * TilePitch - TileGap
        - (ShelfView.Width - ShelfStrip.Margin.Left - ShelfStrip.Margin.Right), 0);

    void SyncShelf()
    {
        foreach ((Shelf.Item item, ShelfTile tile) in _tiles.ToList())
        {
            if (_shelf.Items.Contains(item)) continue;
            _tiles.Remove(item);
            AnimateTileOut(tile);
        }
        foreach (Shelf.Item item in _shelf.Items)
        {
            if (!_tiles.ContainsKey(item)) AddTile(item);
        }

        int count = _shelf.Items.Count;
        BubbleShelfText.RollDown = MenuShelf.RollDown = count < _shownShelfCount;
        _shownShelfCount = count;
        if (count > 0) BubbleShelfText.Text = count.ToString();
        MenuShelf.Text = count > 0 ? count.ToString() : "";
        ShelfClear.SetVisible(count > 0);
        SyncShelfHint();
        ScrollShelf(0);
        UpdateTargets();
    }

    void AddTile(Shelf.Item item)
    {
        var tile = new ShelfTile(item) { Margin = new Thickness(0, 0, TileGap, 0) };
        tile.MouseLeftButtonDown += Tile_MouseLeftButtonDown;
        tile.MouseMove += Tile_MouseMove;
        tile.MouseLeftButtonUp += Tile_MouseLeftButtonUp;
        tile.Removed += removed => _shelf.Remove(removed.Item);
        _tiles[item] = tile;
        ShelfTiles.Children.Add(tile);
        if (ShelfView.IsVisible) AnimateTileIn(tile);
    }

    void OnShelfPictureLoaded(Shelf.Item item)
    {
        if (_tiles.TryGetValue(item, out ShelfTile? tile)) tile.ShowPicture();
    }

    static void AnimateTileIn(ShelfTile tile)
    {
        tile.RenderTransform.AnimateScale(new DoubleAnimation(0.5, 1, Ms(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } });
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(240)));
        tile.AnimateBlur(TileBlur, 0, Ms(320));
    }

    void AnimateTileOut(ShelfTile tile)
    {
        tile.IsHitTestVisible = false;
        bool seen = ShelfView.IsVisible;
        Duration time = Ms(seen ? 300 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        tile.RenderTransform.AnimateScale(new DoubleAnimation(0.5, time) { EasingFunction = ease });
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ms(seen ? 180 : 0)));
        tile.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0), time) { EasingFunction = ease });
        var close = new DoubleAnimation(0, time) { EasingFunction = ease };
        close.Completed += (_, _) => ShelfTiles.Children.Remove(tile);
        tile.BeginAnimation(WidthProperty, close);
    }

    void SyncShelfHint()
    {
        bool empty = _shelf.Items.Count == 0;
        ShelfHintText.Text = _dragOver ? "Отпустите, чтобы положить" : "Перетащите сюда файлы";
        ShelfHintMore.Opacity = _dragOver ? 0 : 1;
        ShelfHint.IsHitTestVisible = empty && !_dragOver;
        ShelfHint.BeginAnimation(OpacityProperty, new DoubleAnimation(empty ? 1 : 0, Ms(200)));
        double zone = _dragOver ? DropZoneOpacity : empty && ShelfHint.IsMouseOver ? HoverZoneOpacity : 0;
        ShelfZone.BeginAnimation(OpacityProperty, new DoubleAnimation(zone, Ms(zone > 0 ? 150 : 300)));
    }

    void ShelfHint_MouseHover(object sender, MouseEventArgs e) => SyncShelfHint();

    void ShelfAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Положить на полку", Multiselect = true };
        bool picked;
        _pickingFiles = true;
        try { picked = dialog.ShowDialog(this) == true; }
        catch (Exception ex)
        {
            App.Log(ex);
            picked = false;
        }
        finally { _pickingFiles = false; }
        if (picked) _shelf.Add(dialog.FileNames);

        ResyncPointer(LongCollapseDelay);
        SyncShelfHint();
    }

    void ResyncPointer(TimeSpan collapseAfter)
    {
        Mouse.Synchronize();
        _hovered = Island.IsMouseOver;
        UpdateTargets();
        if (!_hovered && _panel != Panel.None) _collapseTimeout.Start(collapseAfter);
    }

    static Color EdgeFade(double past) => Color.FromArgb((byte)Math.Round(255 * (1 - Math.Clamp(past / ShelfEdgeFade, 0, 1))), 0, 0, 0);

    void ScrollShelf(int tiles)
    {
        _shelfScroll.Target = Math.Clamp(_shelfScroll.Target + tiles * TilePitch, 0, ShelfOverflow);
        StartShapeLoop();
    }

    void ApplyShelfScroll()
    {
        double scrolled = _shelfScroll.Value;
        ShelfMove.X = -scrolled;
        ShelfEdgeLeft.Color = EdgeFade(scrolled);
        ShelfEdgeRight.Color = EdgeFade(ShelfOverflow - scrolled);
    }

    void ShelfRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Shelf);

    void ShelfClear_Click(object sender, RoutedEventArgs e) => _shelf.Clear();

    void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _pressedTile = (ShelfTile)sender;
        _tilePressPoint = e.GetPosition(this);
        _pressedTile.CaptureMouse();
    }

    void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTile != sender || e.LeftButton != MouseButtonState.Pressed) return;
        Vector moved = e.GetPosition(this) - _tilePressPoint;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        ShelfTile tile = _pressedTile;
        _pressedTile = null;
        tile.ReleaseMouseCapture();
        DragOut(tile);
    }

    void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedTile != sender) return;
        e.Handled = true;
        ShelfTile tile = _pressedTile;
        _pressedTile = null;
        tile.ReleaseMouseCapture();

        try { Process.Start(new ProcessStartInfo(tile.Item.Path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!Shelf.Exists(tile.Item.Path)) _shelf.Remove(tile.Item);
            return;
        }
        SelectPanel(Panel.None);
        UpdateView();
        UpdateTargets();
    }

    void DragOut(ShelfTile tile)
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { tile.Item.Path });
        tile.BeginAnimation(OpacityProperty, new DoubleAnimation(CarriedOpacity, Ms(120)));
        DragDropEffects done;
        _draggingOut = true;
        try { done = DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Link); }
        catch (Exception ex)
        {
            App.Log(ex);
            done = DragDropEffects.None;
        }
        finally { _draggingOut = false; }

        if (done != DragDropEffects.None) _shelf.Remove(tile.Item);
        else tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ms(200)));
        ResyncPointer(CollapseDelay);
    }

    static DragDropEffects DropEffect(DragEventArgs e) =>
        e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : e.AllowedEffects & DragDropEffects.Link;

    void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggingOut || _hiddenByFullscreen || _sentAway || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DropEffect(e);
        _dragLeaveTimeout.Cancel();
        _collapseTimeout.Cancel();
        if (_dragOver) return;

        _dragOver = true;
        _panelBeforeDrag = _panel;
        SelectPanel(Panel.Shelf);
        SyncShelfHint();
        UpdateView();
    }

    void Root_DragLeave(object sender, DragEventArgs e)
    {
        if (_dragOver) _dragLeaveTimeout.Start(DragLeaveDelay);
    }

    void Root_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_dragOver) return;
        _dragLeaveTimeout.Cancel();
        e.Effects = DropEffect(e);
        EndDragOver();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _shelf.Add(paths);
        _collapseTimeout.Start(LongCollapseDelay);
    }

    void EndDragOver()
    {
        _dragOver = false;
        SyncShelfHint();
    }

    void OnDragLeft()
    {
        EndDragOver();
        ShowPanel(_panelBeforeDrag);
    }
}
