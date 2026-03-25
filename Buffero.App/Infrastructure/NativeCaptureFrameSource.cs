using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Buffero.App.Infrastructure;

internal sealed class CapturedSurface : IDisposable
{
    public required IDirect3DSurface Surface { get; init; }

    public required TimeSpan SystemRelativeTime { get; init; }

    public void Dispose()
    {
        Surface.Dispose();
    }
}

internal sealed class MultithreadLock : IDisposable
{
    private ID3D11Multithread? _multithread;

    public MultithreadLock(ID3D11Multithread? multithread)
    {
        _multithread = multithread;
        _multithread?.Enter();
    }

    public void Dispose()
    {
        _multithread?.Leave();
        _multithread = null;
    }
}

internal sealed class NativeCaptureFrameSource : IDisposable
{
    private readonly NativeDirect3DContext _graphics;
    private readonly GraphicsCaptureItem _item;
    private readonly object _frameGate = new();
    private readonly ManualResetEvent _frameEvent = new(initialState: false);
    private readonly ManualResetEvent _closedEvent = new(initialState: false);
    private readonly WaitHandle[] _events;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Direct3D11CaptureFrame? _currentFrame;
    private ID3D11Texture2D? _blankTexture;
    private SizeInt32 _captureSize;
    private bool _disposed;

    public NativeCaptureFrameSource(NativeDirect3DContext graphics, GraphicsCaptureItem item)
    {
        _graphics = graphics;
        _item = item;
        _captureSize = item.Size;
        _events = [_closedEvent, _frameEvent];

        InitializeBlankTexture(_captureSize);
        InitializeCapture(_captureSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closedEvent.Set();
        Cleanup();
        _frameEvent.Dispose();
        _closedEvent.Dispose();
    }

    public CapturedSurface? WaitForNewFrame()
    {
        Direct3D11CaptureFrame? frame;

        while (true)
        {
            if (_disposed)
            {
                return null;
            }

            lock (_frameGate)
            {
                if (_currentFrame is not null)
                {
                    frame = _currentFrame;
                    _currentFrame = null;
                    break;
                }

                _frameEvent.Reset();
            }

            var signaled = _events[WaitHandle.WaitAny(_events)];
            if (signaled == _closedEvent || _disposed)
            {
                return null;
            }
        }

        if (frame is null)
        {
            return null;
        }

        using var capturedFrame = frame;
        using var multithreadLock = new MultithreadLock(_graphics.Multithread);
        using var sourceTexture = Direct3D11Interop.CreateTexture2D(capturedFrame.Surface);
        var contentSize = capturedFrame.ContentSize;
        if (contentSize.Width > _captureSize.Width || contentSize.Height > _captureSize.Height)
        {
            throw new InvalidOperationException("The capture target size changed while native capture was running.");
        }

        var description = sourceTexture.Description;
        description.Usage = ResourceUsage.Default;
        description.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
        description.CPUAccessFlags = CpuAccessFlags.None;
        description.MiscFlags = ResourceOptionFlags.None;

        using var copyTexture = _graphics.Device.CreateTexture2D(description);
        _graphics.DeviceContext.CopyResource(copyTexture, _blankTexture!);

        var width = Math.Clamp(contentSize.Width, 0, capturedFrame.Surface.Description.Width);
        var height = Math.Clamp(contentSize.Height, 0, capturedFrame.Surface.Description.Height);
        if (width > 0 && height > 0)
        {
            var region = new Box(0, 0, 0, width, height, 1);
            _graphics.DeviceContext.CopySubresourceRegion(copyTexture, 0, 0, 0, 0, sourceTexture, 0, region);
        }

        return new CapturedSurface
        {
            Surface = Direct3D11Interop.CreateSurface(copyTexture),
            SystemRelativeTime = capturedFrame.SystemRelativeTime
        };
    }

    private void InitializeCapture(SizeInt32 size)
    {
        _item.Closed += OnItemClosed;
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _graphics.WinrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            size);
        _framePool.FrameArrived += OnFrameArrived;
        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = true;
        _session.StartCapture();
    }

    private void InitializeBlankTexture(SizeInt32 size)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _blankTexture = _graphics.Device.CreateTexture2D(description);
        using var renderTargetView = _graphics.Device.CreateRenderTargetView(_blankTexture);
        _graphics.DeviceContext.ClearRenderTargetView(renderTargetView, new Color4(0f, 0f, 0f, 1f));
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var nextFrame = sender.TryGetNextFrame();
        if (nextFrame is null)
        {
            return;
        }

        lock (_frameGate)
        {
            _currentFrame?.Dispose();
            _currentFrame = nextFrame;
        }

        _frameEvent.Set();
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _closedEvent.Set();
    }

    private void Cleanup()
    {
        _framePool?.Dispose();
        _framePool = null;
        _session?.Dispose();
        _session = null;
        lock (_frameGate)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        _blankTexture?.Dispose();
        _blankTexture = null;
        _item.Closed -= OnItemClosed;
    }
}
