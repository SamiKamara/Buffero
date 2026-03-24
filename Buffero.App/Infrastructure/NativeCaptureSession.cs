using Buffero.Core.Configuration;
using Windows.Graphics.Capture;

namespace Buffero.App.Infrastructure;

public sealed class NativeCaptureSession : IReplayCaptureSession
{
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;
    private readonly GraphicsCaptureItem _item;
    private readonly string _sessionDirectory;
    private readonly uint _inputWidth;
    private readonly uint _inputHeight;
    private readonly uint _outputWidth;
    private readonly uint _outputHeight;
    private readonly uint _bitrate;
    private NativeDirect3DContext? _graphics;
    private NativeCaptureFrameSource? _frameSource;
    private Task? _recordTask;
    private bool _stopRequested;
    private bool _cleanupCompleted;

    public NativeCaptureSession(AppSettings settings, FileLogger logger, GraphicsCaptureItem item, string sessionDirectory)
    {
        _settings = settings;
        _logger = logger;
        _item = item;
        _sessionDirectory = sessionDirectory;
        _inputWidth = (uint)item.Size.Width;
        _inputHeight = (uint)item.Size.Height;
        (_outputWidth, _outputHeight) = OutputResolutionCalculator.GetOutputSize(item.Size.Width, item.Size.Height, settings.OutputResolution);
        _bitrate = OutputResolutionCalculator.EstimateBitrate((int)_outputWidth, (int)_outputHeight, settings.Fps, settings.QualityCrf);
    }

    public bool IsRunning { get; private set; }

    public CaptureBackend Backend => CaptureBackend.Native;

    public event Action<int>? Exited;

    public Task StartAsync()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows.Graphics.Capture is not supported on this Windows installation.");
        }

        Directory.CreateDirectory(_sessionDirectory);
        _graphics = Direct3D11Interop.Create();
        _frameSource = new NativeCaptureFrameSource(_graphics, _item);
        _stopRequested = false;
        IsRunning = true;
        _recordTask = Task.Run(RunSegmentLoopAsync);
        _logger.Info("Native capture session started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning && _recordTask is null)
        {
            return;
        }

        _stopRequested = true;
        _frameSource?.Dispose();

        if (_recordTask is not null)
        {
            try
            {
                await _recordTask.WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                _logger.Warn("Timed out while waiting for the native capture loop to stop.");
            }
        }

        Cleanup();
        _logger.Info("Native capture session stopped.");
    }

    private async Task RunSegmentLoopAsync()
    {
        var sequence = 1;
        var exitCode = 0;

        try
        {
            while (!_stopRequested)
            {
                var segmentPath = Path.Combine(_sessionDirectory, $"segment-{sequence:000000}.mp4");
                using var encoder = new NativeSegmentEncoder(
                    _frameSource!,
                    _inputWidth,
                    _inputHeight,
                    _outputWidth,
                    _outputHeight,
                    _bitrate,
                    (uint)_settings.Fps);
                var result = await encoder.EncodeAsync(
                    segmentPath,
                    TimeSpan.FromSeconds(_settings.SegmentSeconds),
                    CancellationToken.None);

                if (_stopRequested)
                {
                    break;
                }

                if (result.CaptureEnded)
                {
                    throw new InvalidOperationException("The native capture target closed or became unavailable.");
                }

                if (!File.Exists(segmentPath) || new FileInfo(segmentPath).Length <= 0)
                {
                    throw new InvalidOperationException("The native encoder did not produce a valid replay segment.");
                }

                sequence++;
            }
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _logger.Error("Native capture failed.", exception);
        }
        finally
        {
            IsRunning = false;
            Cleanup();
            Exited?.Invoke(_stopRequested ? 0 : exitCode);
        }
    }

    private void Cleanup()
    {
        if (_cleanupCompleted)
        {
            return;
        }

        _cleanupCompleted = true;
        _frameSource?.Dispose();
        _frameSource = null;
        _graphics?.Dispose();
        _graphics = null;
    }
}
