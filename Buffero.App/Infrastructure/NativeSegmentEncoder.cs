using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Buffero.App.Infrastructure;

internal sealed record SegmentEncodeResult(bool SegmentCompleted, bool CaptureEnded);

internal sealed class NativeSegmentEncoder : IDisposable
{
    private static readonly TimeSpan MinimumStartupFrameTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaximumStartupFrameTimeout = TimeSpan.FromSeconds(20);

    private readonly NativeCaptureFrameSource _frameSource;
    private readonly uint _inputWidth;
    private readonly uint _inputHeight;
    private readonly uint _outputWidth;
    private readonly uint _outputHeight;
    private readonly uint _bitrate;
    private readonly uint _frameRate;
    private readonly TimeSpan _frameDuration;
    private readonly bool _hardwareAccelerationEnabled;
    private MediaStreamSource? _mediaStreamSource;
    private MediaTranscoder? _transcoder;
    private CapturedSurface? _currentFrame;
    private TimeSpan _segmentDuration;
    private TimeSpan _startupFrameTimeout;
    private int _emittedFrameCount;
    private int _targetFrameCount;
    private bool _stopRequested;
    private bool _captureEnded;
    private bool _segmentCompleted;
    private bool _timedOutWaitingForFirstFrame;

    public NativeSegmentEncoder(
        NativeCaptureFrameSource frameSource,
        uint inputWidth,
        uint inputHeight,
        uint outputWidth,
        uint outputHeight,
        uint bitrate,
        uint frameRate,
        bool hardwareAccelerationEnabled = true)
    {
        _frameSource = frameSource;
        _inputWidth = inputWidth;
        _inputHeight = inputHeight;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _bitrate = bitrate;
        _frameRate = frameRate;
        _frameDuration = TimeSpan.FromSeconds(1d / Math.Max(1, frameRate));
        _hardwareAccelerationEnabled = hardwareAccelerationEnabled;
    }

    public async Task<SegmentEncodeResult> EncodeAsync(string outputPath, TimeSpan segmentDuration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        _segmentDuration = segmentDuration;
        _currentFrame?.Dispose();
        _currentFrame = null;
        _emittedFrameCount = 0;
        _targetFrameCount = GetTargetFrameCount(segmentDuration, _frameRate);
        _stopRequested = false;
        _captureEnded = false;
        _segmentCompleted = false;
        _timedOutWaitingForFirstFrame = false;
        _startupFrameTimeout = GetStartupFrameTimeout(segmentDuration);

        CreateMediaObjects();
        using var stream = await OpenOutputStreamAsync(outputPath);
        using var registration = cancellationToken.Register(() => _stopRequested = true);

        var profile = new MediaEncodingProfile();
        profile.Container.Subtype = "MPEG4";
        profile.Video.Subtype = "H264";
        profile.Video.Width = _outputWidth;
        profile.Video.Height = _outputHeight;
        profile.Video.Bitrate = _bitrate;
        profile.Video.FrameRate.Numerator = _frameRate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        var prepared = await _transcoder!.PrepareMediaStreamSourceTranscodeAsync(_mediaStreamSource!, stream, profile);

        try
        {
            await prepared.TranscodeAsync().AsTask();
        }
        catch (COMException exception) when (_timedOutWaitingForFirstFrame)
        {
            throw new InvalidOperationException("Timed out waiting for the first native capture frame.", exception);
        }

        if (_timedOutWaitingForFirstFrame)
        {
            throw new InvalidOperationException("Timed out waiting for the first native capture frame.");
        }

        return new SegmentEncodeResult(_segmentCompleted, _captureEnded);
    }

    public void Dispose()
    {
        if (_mediaStreamSource is not null)
        {
            _mediaStreamSource.Starting -= OnStarting;
            _mediaStreamSource.SampleRequested -= OnSampleRequested;
            _mediaStreamSource = null;
        }

        _currentFrame?.Dispose();
        _currentFrame = null;
        _transcoder = null;
    }

    private void CreateMediaObjects()
    {
        var videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, _inputWidth, _inputHeight);
        var descriptor = new VideoStreamDescriptor(videoProperties);
        _mediaStreamSource = new MediaStreamSource(descriptor)
        {
            BufferTime = TimeSpan.Zero
        };
        _mediaStreamSource.Starting += OnStarting;
        _mediaStreamSource.SampleRequested += OnSampleRequested;

        _transcoder = new MediaTranscoder
        {
            HardwareAccelerationEnabled = _hardwareAccelerationEnabled
        };
    }

    private void OnStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        _currentFrame?.Dispose();
        _currentFrame = _frameSource.WaitForNewFrame(_startupFrameTimeout, out var timedOut);
        if (timedOut)
        {
            _timedOutWaitingForFirstFrame = true;
            _stopRequested = true;
            args.Request.SetActualStartPosition(TimeSpan.Zero);
            return;
        }

        if (_currentFrame is null)
        {
            _captureEnded = true;
            args.Request.SetActualStartPosition(TimeSpan.Zero);
            return;
        }

        args.Request.SetActualStartPosition(TimeSpan.Zero);
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (_stopRequested)
        {
            args.Request.Sample = null;
            return;
        }

        if (_emittedFrameCount >= _targetFrameCount)
        {
            _segmentCompleted = true;
            _stopRequested = true;
            args.Request.Sample = null;
            return;
        }

        try
        {
            if (_emittedFrameCount > 0)
            {
                var nextFrame = _frameSource.WaitForNewFrame(_frameDuration, out var timedOut);
                if (nextFrame is not null)
                {
                    _currentFrame?.Dispose();
                    _currentFrame = nextFrame;
                }
                else if (!timedOut)
                {
                    _captureEnded = true;
                    args.Request.Sample = null;
                    return;
                }
            }

            if (_currentFrame is null)
            {
                _captureEnded = true;
                args.Request.Sample = null;
                return;
            }

            var sampleTime = GetSampleTime(_emittedFrameCount, _frameRate);
            var sample = MediaStreamSample.CreateFromDirect3D11Surface(_currentFrame.Surface, sampleTime);
            sample.Duration = _frameDuration;
            args.Request.Sample = sample;
            _emittedFrameCount++;
            if (_emittedFrameCount >= _targetFrameCount)
            {
                _segmentCompleted = true;
                _stopRequested = true;
            }
        }
        finally
        {
        }
    }

    private static async Task<IRandomAccessStream> OpenOutputStreamAsync(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);
        return await file.OpenAsync(FileAccessMode.ReadWrite);
    }

    private static TimeSpan GetStartupFrameTimeout(TimeSpan segmentDuration)
    {
        var scaledTimeout = TimeSpan.FromSeconds(segmentDuration.TotalSeconds * 3);
        if (scaledTimeout < MinimumStartupFrameTimeout)
        {
            return MinimumStartupFrameTimeout;
        }

        if (scaledTimeout > MaximumStartupFrameTimeout)
        {
            return MaximumStartupFrameTimeout;
        }

        return scaledTimeout;
    }

    internal static int GetTargetFrameCount(TimeSpan segmentDuration, uint frameRate)
    {
        return Math.Max(
            1,
            (int)Math.Round(segmentDuration.TotalSeconds * Math.Max(1, frameRate), MidpointRounding.AwayFromZero));
    }

    internal static TimeSpan GetSampleTime(int sampleIndex, uint frameRate)
    {
        return TimeSpan.FromSeconds(sampleIndex / (double)Math.Max(1, frameRate));
    }
}
