using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Buffero.App.Infrastructure;

internal sealed record SegmentEncodeResult(bool SegmentCompleted, bool CaptureEnded);

internal sealed class NativeSegmentEncoder : IDisposable
{
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
    private TimeSpan _segmentDuration;
    private TimeSpan? _segmentStartTime;
    private CapturedSurface? _pendingFrame;
    private bool _stopRequested;
    private bool _captureEnded;
    private bool _segmentCompleted;

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
        _segmentStartTime = null;
        _pendingFrame?.Dispose();
        _pendingFrame = null;
        _stopRequested = false;
        _captureEnded = false;
        _segmentCompleted = false;

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
        await prepared.TranscodeAsync().AsTask();
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

        _pendingFrame?.Dispose();
        _pendingFrame = null;
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
        _pendingFrame?.Dispose();
        _pendingFrame = _frameSource.WaitForNewFrame();
        if (_pendingFrame is null)
        {
            _captureEnded = true;
            _segmentStartTime = TimeSpan.Zero;
            args.Request.SetActualStartPosition(TimeSpan.Zero);
            return;
        }

        _segmentStartTime = _pendingFrame.SystemRelativeTime;
        args.Request.SetActualStartPosition(_segmentStartTime.Value);
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        if (_stopRequested)
        {
            args.Request.Sample = null;
            return;
        }

        using var frame = _pendingFrame ?? _frameSource.WaitForNewFrame();
        _pendingFrame = null;
        if (frame is null)
        {
            _captureEnded = true;
            args.Request.Sample = null;
            return;
        }

        _segmentStartTime ??= frame.SystemRelativeTime;
        var sampleTime = frame.SystemRelativeTime;
        var elapsed = sampleTime - _segmentStartTime.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (sampleTime < TimeSpan.Zero)
        {
            sampleTime = TimeSpan.Zero;
        }

        if (elapsed >= _segmentDuration)
        {
            _segmentCompleted = true;
            _stopRequested = true;
            args.Request.Sample = null;
            return;
        }

        var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, sampleTime);
        sample.Duration = _frameDuration;
        args.Request.Sample = sample;
    }

    private static async Task<IRandomAccessStream> OpenOutputStreamAsync(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);
        return await file.OpenAsync(FileAccessMode.ReadWrite);
    }
}
