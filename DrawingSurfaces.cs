using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;
using WinRT;

namespace DynamicIsland;

sealed class DrawingSurfaces : IDisposable
{
    const int D3D_DRIVER_TYPE_HARDWARE = 1, D3D_DRIVER_TYPE_WARP = 5;
    const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20, D3D11_SDK_VERSION = 7;
    const int UpdateSubresourceSlot = 48, SetMultithreadProtectedSlot = 5;
    const int BytesPerPixel = 4;
    static readonly Guid Texture2DId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    static readonly Guid MultithreadId = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");

    [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorInterop
    {
        void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr surface);
        void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr surface);
        void CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr graphicsDevice);
    }

    [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositionDrawingSurfaceInterop
    {
        void BeginDraw(IntPtr updateRect, ref Guid iid, out IntPtr updateObject, out Offset updateOffset);
        void EndDraw();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Offset { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct Box { public uint Left, Top, Front, Right, Bottom, Back; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void UpdateSubresourceCall(IntPtr context, IntPtr resource, uint subresource, ref Box box, IntPtr data, uint rowPitch, uint depthPitch);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetMultithreadProtectedCall(IntPtr multithread, int on);

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount,
        uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);

    readonly IntPtr _device, _context;
    readonly UpdateSubresourceCall _updateSubresource;
    readonly CompositionGraphicsDevice _graphics;

    public DrawingSurfaces(Compositor compositor)
    {
        if (D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out _device, out _, out _context) < 0)
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
                D3D11_SDK_VERSION, out _device, out _, out _context));

        ProtectFromOtherThreads();
        _updateSubresource = Marshal.GetDelegateForFunctionPointer<UpdateSubresourceCall>(Slot(_context, UpdateSubresourceSlot));
        compositor.As<ICompositorInterop>().CreateGraphicsDevice(_device, out IntPtr graphics);
        _graphics = CompositionGraphicsDevice.FromAbi(graphics);
        Marshal.Release(graphics);
    }

    static IntPtr Slot(IntPtr instance, int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size);

    void ProtectFromOtherThreads()
    {
        Guid id = MultithreadId;
        if (Marshal.QueryInterface(_device, ref id, out IntPtr multithread) < 0) return;
        Marshal.GetDelegateForFunctionPointer<SetMultithreadProtectedCall>(Slot(multithread, SetMultithreadProtectedSlot))(multithread, 1);
        Marshal.Release(multithread);
    }

    public CompositionDrawingSurface Create(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * BytesPerPixel;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        CompositionDrawingSurface surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(width, height),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        try
        {
            Fill(surface, pixels, width, height, stride);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        return surface;
    }

    void Fill(CompositionDrawingSurface surface, byte[] pixels, int width, int height, int stride)
    {
        var drawing = surface.As<ICompositionDrawingSurfaceInterop>();
        Guid id = Texture2DId;
        drawing.BeginDraw(IntPtr.Zero, ref id, out IntPtr texture, out Offset offset);
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var box = new Box
            {
                Left = (uint)offset.X, Top = (uint)offset.Y, Front = 0,
                Right = (uint)(offset.X + width), Bottom = (uint)(offset.Y + height), Back = 1,
            };
            _updateSubresource(_context, texture, 0, ref box, pinned.AddrOfPinnedObject(), (uint)stride, 0);
        }
        finally
        {
            pinned.Free();
            Marshal.Release(texture);
            drawing.EndDraw();
        }
    }

    public void Dispose()
    {
        _graphics.Dispose();
        Marshal.Release(_context);
        Marshal.Release(_device);
    }
}
