using System.Runtime.InteropServices;
using Buffero.Core.Configuration;
using Buffero.Core.Capture;
using Windows.Graphics.Capture;

namespace Buffero.App.Infrastructure;

public sealed class NativeCaptureSession : IReplayCaptureSession
{
    private static readonly TimeSpan MinimumSegmentCompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumSegmentCompletionTimeout = TimeSpan.FromSeconds(30);

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
        _bitrate = (uint)CaptureQualityEstimator.ResolveTargetBitrateBitsPerSecond(
            settings.QualityInputMode,
            settings.QualityBitrateMbps,
            settings.QualityCrf,
            (int)_outputWidth,
            (int)_outputHeight,
            settings.Fps);
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
        var segmentCompletionTimeout = GetSegmentCompletionTimeout(_settings.SegmentSeconds);

        try
        {
            while (!_stopRequested)
            {
                var segmentPath = Path.Combine(_sessionDirectory, $"segment-{sequence:000000}.mp4");
                var result = await EncodeSegmentAsync(segmentPath, segmentCompletionTimeout);

                if (_stopRequested)
                {
                    break;
                }

                if (result.CaptureEnded)
                {
                    throw new InvalidOperationException("The native capture target closed or became unavailable.");
                }

                if (!result.SegmentCompleted)
                {
                    throw new InvalidOperationException("The native encoder stopped before finalizing a replay segment.");
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

    private async Task<SegmentEncodeResult> EncodeSegmentAsync(string segmentPath, TimeSpan segmentCompletionTimeout)
    {
        var hardwareAccelerationEnabled = true;

        while (true)
        {
            using var encoder = new NativeSegmentEncoder(
                _frameSource!,
                _inputWidth,
                _inputHeight,
                _outputWidth,
                _outputHeight,
                _bitrate,
                (uint)_settings.Fps,
                hardwareAccelerationEnabled);

            try
            {
                var encodeTask = encoder.EncodeAsync(
                    segmentPath,
                    TimeSpan.FromSeconds(_settings.SegmentSeconds),
                    CancellationToken.None);

                try
                {
                    return await encodeTask.WaitAsync(segmentCompletionTimeout);
                }
                catch (TimeoutException exception) when (!_stopRequested)
                {
                    _frameSource?.Dispose();

                    try
                    {
                        await encodeTask;
                    }
                    catch
                    {
                        // The session is being failed and cleaned up below.
                    }

                    throw new InvalidOperationException(
                        $"The native encoder did not finalize a replay segment within {segmentCompletionTimeout.TotalSeconds:0.#} seconds.",
                        exception);
                }
            }
            catch (COMException exception) when (hardwareAccelerationEnabled && !_stopRequested)
            {
                hardwareAccelerationEnabled = false;
                _logger.Warn($"Native hardware transcoder failed and will retry in software. {exception.Message}");
            }
        }
    }

    internal static TimeSpan GetSegmentCompletionTimeout(int segmentSeconds)
    {
        var scaledTimeout = TimeSpan.FromSeconds(Math.Max(1, segmentSeconds) * 4);
        if (scaledTimeout < MinimumSegmentCompletionTimeout)
        {
            return MinimumSegmentCompletionTimeout;
        }

        if (scaledTimeout > MaximumSegmentCompletionTimeout)
        {
            return MaximumSegmentCompletionTimeout;
        }

        return scaledTimeout;
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
