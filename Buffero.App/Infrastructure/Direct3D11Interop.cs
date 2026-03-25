using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static Vortice.Direct3D11.D3D11;

namespace Buffero.App.Infrastructure;

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}

internal sealed class NativeDirect3DContext : IDisposable
{
    public NativeDirect3DContext(
        ID3D11Device device,
        ID3D11DeviceContext deviceContext,
        IDirect3DDevice winrtDevice,
        ID3D11Multithread? multithread)
    {
        Device = device;
        DeviceContext = deviceContext;
        WinrtDevice = winrtDevice;
        Multithread = multithread;
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext DeviceContext { get; }

    public IDirect3DDevice WinrtDevice { get; }

    public ID3D11Multithread? Multithread { get; }

    public void Dispose()
    {
        Multithread?.Dispose();
        WinrtDevice.Dispose();
        DeviceContext.Dispose();
        Device.Dispose();
    }
}

internal static class Direct3D11Interop
{
    private static readonly Guid Id3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    public static NativeDirect3DContext Create(bool useWarp = false)
    {
        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };

        var creationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        ID3D11Device device;
        ID3D11DeviceContext deviceContext;

        try
        {
            var driverType = useWarp ? DriverType.Warp : DriverType.Hardware;
            D3D11CreateDevice(
                null,
                driverType,
                creationFlags,
                featureLevels,
                out device,
                out _,
                out deviceContext).ThrowIfFailed();
        }
        catch when (!useWarp)
        {
            return Create(useWarp: true);
        }

        var multithread = device.QueryInterfaceOrNull<ID3D11Multithread>();
        multithread?.SetMultithreadProtected(true);

        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevicePointer).ThrowIfFailed();

        try
        {
            var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePointer);
            return new NativeDirect3DContext(device, deviceContext, winrtDevice, multithread);
        }
        finally
        {
            Marshal.Release(graphicsDevicePointer);
        }
    }

    public static IDirect3DSurface CreateSurface(ID3D11Texture2D texture)
    {
        using var dxgiSurface = texture.QueryInterface<IDXGISurface>();
        CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var graphicsSurfacePointer).ThrowIfFailed();

        try
        {
            return MarshalInterface<IDirect3DSurface>.FromAbi(graphicsSurfacePointer);
        }
        finally
        {
            Marshal.Release(graphicsSurfacePointer);
        }
    }

    public static ID3D11Texture2D CreateTexture2D(IDirect3DSurface surface)
    {
        var access = (IDirect3DDxgiInterfaceAccess)surface;
        var iid = Id3D11Texture2DGuid;
        var texturePointer = access.GetInterface(ref iid);
        return new ID3D11Texture2D(texturePointer);
    }
}
