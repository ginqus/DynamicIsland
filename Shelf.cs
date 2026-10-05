using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland;

sealed class Shelf
{
    const int ThumbnailSize = 112, IconSize = 96;
    const int ThumbnailOnly = 0x8, IconOnly = 0x4;
    const int BitsPerPixel = 32, BytesPerPixel = 4;
    const double Dpi = 96;

    public sealed class Item(string path)
    {
        public string Path { get; } = path;
        public string Name { get; } = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
        public ImageSource? Picture { get; internal set; }
        public bool IsPhoto { get; internal set; }
    }

    readonly Dispatcher _ui;
    readonly List<Item> _items = [];

    public Shelf(Dispatcher ui)
    {
        _ui = ui;
        AddNew(Settings.Shelf.Where(Exists));
    }

    public IReadOnlyList<Item> Items => _items;

    public event Action? Changed;

    public event Action<Item>? PictureLoaded;

    public bool Add(IEnumerable<string> paths)
    {
        if (!AddNew(paths.Where(Exists))) return false;
        Save();
        return true;
    }

    public void Remove(Item item)
    {
        if (_items.Remove(item)) Save();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Save();
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    bool AddNew(IEnumerable<string> paths)
    {
        var added = new List<Item>();
        foreach (string path in paths)
        {
            if (_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var fresh = new Item(path);
            _items.Add(fresh);
            added.Add(fresh);
        }
        if (added.Count == 0) return false;

        LoadPictures(added);
        return true;
    }

    void Save()
    {
        Settings.Shelf = _items.Select(item => item.Path).ToArray();
        Changed?.Invoke();
    }

    void LoadPictures(List<Item> items)
    {
        var thread = new Thread(() =>
        {
            foreach (Item item in items)
            {
                BitmapSource? picture = ShellImage(item.Path, ThumbnailSize, ThumbnailOnly);
                bool photo = picture != null && IsOpaque(picture);
                if (!photo) picture = ShellImage(item.Path, IconSize, IconOnly);
                if (picture == null) continue;
                _ui.InvokeAsync(() =>
                {
                    item.Picture = picture;
                    item.IsPhoto = photo;
                    PictureLoaded?.Invoke(item);
                });
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    static bool IsOpaque(BitmapSource picture)
    {
        int w = picture.PixelWidth, h = picture.PixelHeight;
        var pixel = new byte[BytesPerPixel];
        foreach (var (x, y) in new[] { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1) })
        {
            picture.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, BytesPerPixel, 0);
            if (pixel[3] < 0xFF) return false;
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr context, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    static BitmapSource? ShellImage(string path, int size, int flags)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory) != 0) return null;
            int result = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out bitmap);
            Marshal.ReleaseComObject(factory);
            if (result != 0 || bitmap == IntPtr.Zero) return null;

            if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out BITMAP info) == 0 || info.bmBitsPixel != BitsPerPixel) return null;
            int width = info.bmWidth, height = Math.Abs(info.bmHeight);
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = BitsPerPixel,
            };
            var pixels = new byte[width * BytesPerPixel * height];
            IntPtr dc = GetDC(IntPtr.Zero);
            int rows = GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0);
            ReleaseDC(IntPtr.Zero, dc);
            if (rows != height) return null;
            var copy = BitmapSource.Create(width, height, Dpi, Dpi, PixelFormats.Pbgra32, null, pixels, width * BytesPerPixel);
            copy.Freeze();
            return copy;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }
}
