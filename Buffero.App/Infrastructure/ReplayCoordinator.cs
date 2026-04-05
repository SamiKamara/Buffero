using System.Diagnostics;
using System.Text;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;
using Buffero.Core.Detection;
using Buffero.Core.State;
using Windows.Graphics.Capture;

namespace Buffero.App.Infrastructure;

public sealed class ReplayCoordinator
{
    private static readonly TimeSpan AutoStartDebounceInterval = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan SegmentMonitorStabilityThreshold = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SaveSnapshotStabilityThreshold = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _segmentCatalogSync = new();
    private readonly FileLogger _logger;
    private readonly FfmpegLocator _locator;
    private readonly BufferoPaths _paths;
    private readonly GameMatchEvaluator _gameMatchEvaluator = new();
    private readonly GameEligibilityDebouncer _autoStartDebouncer = new(AutoStartDebounceInterval);
    private readonly SemaphoreSlim _detectionPassGate = new(1, 1);
    private readonly ReplayStateMachine _stateMachine = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    private AppSettings _settings;
    private SegmentCatalog _segmentCatalog;
    private Task? _detectionLoopTask;
    private Task? _segmentLoopTask;
    private IReplayCaptureSession? _captureSession;
    private string? _currentSessionDirectory;
    private string? _activeMatch;
    private string? _eligibleMatch;
    private string? _lastSavedClipPath;
    private CaptureTargetWindow? _captureTargetWindow;
    private CaptureTargetWindow? _eligibleTargetWindow;
    private string _captureTargetDescription = "Desktop fallback";
    private CaptureBackend _activeCaptureBackend = CaptureBackend.Native;
    private int _captureGeneration;
    private bool _manualCapture;
    private bool _autoCapture;

    public ReplayCoordinator(AppSettings settings, BufferoPaths paths, FileLogger logger, FfmpegLocator locator)
    {
        _settings = settings;
        _paths = paths;
        _logger = logger;
        _locator = locator;
        _segmentCatalog = new SegmentCatalog(_settings.SegmentSeconds);
        _stateMachine.Changed += (_, _) => PublishSnapshot();
    }

    public event Action<ReplayCoordinatorSnapshot>? SnapshotChanged;

    public event Action<ReplaySavingInfo>? ReplaySavingStarted;

    public event Action<ReplaySavedInfo>? ReplaySaved;

    public Task TriggerDetectionAsync()
    {
        if (_lifetimeCts.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => EvaluateDetectionAsync(_lifetimeCts.Token));
    }

    public async Task InitializeAsync()
    {
        var estimateResolution = QualityEstimateResolutionProbe.Resolve(_settings, _activeMatch);
        _settings.Normalize(_locator.FindBestPath(), estimateResolution.Width, estimateResolution.Height);
        _paths.EnsureDirectories(_settings.SaveDirectory);
        PublishSnapshot();

        if (_detectionLoopTask is null)
        {
            _detectionLoopTask = Task.Run(() => RunDetectionLoopAsync(_lifetimeCts.Token));
        }

        await Task.CompletedTask;
    }

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        var estimateResolution = QualityEstimateResolutionProbe.Resolve(settings, _activeMatch);
        settings.Normalize(_locator.FindBestPath(), estimateResolution.Width, estimateResolution.Height);

        var restartManual = settings.ReplayBufferEnabled && _manualCapture;
        var restartAuto = settings.ReplayBufferEnabled
            && settings.BufferActivationMode == BufferActivationMode.Automatic
            && _autoCapture
            && !string.IsNullOrWhiteSpace(_activeMatch);
        var restartMatch = _activeMatch;

        await StopCaptureAsync("Applying updated settings.");

        _settings = settings;
        _paths.EnsureDirectories(_settings.SaveDirectory);
        _segmentCatalog = new SegmentCatalog(_settings.SegmentSeconds);
        _autoStartDebouncer.Reset();

        if (!_settings.ReplayBufferEnabled)
        {
            _eligibleMatch = null;
            _eligibleTargetWindow = null;
            _stateMachine.SetEligibleTarget(null);
            PublishSnapshot();
            return;
        }

        if (restartManual)
        {
            await StartCaptureInternalAsync(restartMatch, manualCapture: true);
        }
        else if (restartAuto)
        {
            await StartCaptureInternalAsync(restartMatch, manualCapture: false);
        }
        else
        {
            PublishSnapshot();
        }
    }

    public async Task StartManualCaptureAsync()
    {
        if (!_settings.ReplayBufferEnabled)
        {
            _logger.Warn("Manual start ignored because the replay buffer is disabled.");
            PublishSnapshot();
            return;
        }

        _manualCapture = true;
        _autoCapture = false;
        await StartCaptureInternalAsync(ResolveManualCaptureMatch(), manualCapture: true);
    }

    public async Task StopCaptureAsync(string reason)
    {
        await _gate.WaitAsync();

        try
        {
            _manualCapture = false;
            _autoCapture = false;

            if (_captureSession is null)
            {
                _captureGeneration++;
                _currentSessionDirectory = null;
                _activeMatch = null;
                _captureTargetWindow = null;
                _captureTargetDescription = "Not capturing";
                _activeCaptureBackend = _settings.CaptureBackend;
                lock (_segmentCatalogSync)
                {
                    _segmentCatalog.ReplaceSegments([]);
                }

                _stateMachine.MarkCaptureStopped();
                PublishSnapshot();
                return;
            }

            _logger.Info(reason);
            await _captureSession.StopAsync();
            _captureGeneration++;
            _captureSession = null;
            _currentSessionDirectory = null;
            _activeMatch = null;
            _captureTargetWindow = null;
            _captureTargetDescription = "Not capturing";
            _activeCaptureBackend = _settings.CaptureBackend;
            lock (_segmentCatalogSync)
            {
                _segmentCatalog.ReplaceSegments([]);
            }

            _stateMachine.MarkCaptureStopped();
            PublishSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveReplayAsync()
    {
        List<SegmentInfo> snapshot;
        string outputPath;
        string? concatPath = null;
        string sessionDirectory;
        ReplaySavingInfo? replaySavingInfo = null;
        ReplaySavedInfo? replaySavedInfo = null;

        await _gate.WaitAsync();

        try
        {
            if (!_settings.ReplayBufferEnabled)
            {
                throw new InvalidOperationException("Replay buffer is disabled. Enable it before saving a replay.");
            }

            sessionDirectory = _currentSessionDirectory ?? Path.Combine(_paths.TempSessionsDirectory, "exports");
            snapshot = GetLatestReplaySnapshot(sessionDirectory, SaveSnapshotStabilityThreshold).ToList();

            if (snapshot.Count == 0)
            {
                throw new InvalidOperationException("No finalized replay segments are buffered yet.");
            }

            Directory.CreateDirectory(sessionDirectory);

            outputPath = Path.Combine(
                _settings.SaveDirectory,
                ClipNameBuilder.Build(_settings.ClipFilePattern, DateTimeOffset.Now, _activeMatch));
            EnsureSufficientExportSpace(outputPath, snapshot);
            _stateMachine.MarkExportQueued();
            replaySavingInfo = new ReplaySavingInfo(outputPath, _activeMatch, _captureTargetWindow);
            PublishSnapshot();
        }
        finally
        {
            _gate.Release();
        }

        if (replaySavingInfo is not null)
        {
            try
            {
                ReplaySavingStarted?.Invoke(replaySavingInfo);
            }
            catch (Exception exception)
            {
                _logger.Error("Replay saving notification failed.", exception);
            }
        }

        try
        {
            if (_activeCaptureBackend == CaptureBackend.Native)
            {
                try
                {
                    await NativeReplayExporter.ExportAsync(snapshot, _settings, outputPath, _lifetimeCts.Token);
                }
                catch (Exception exception) when (CanUseFfmpeg())
                {
                    _logger.Warn($"Native replay export failed and will fall back to ffmpeg. {exception.Message}");
                    concatPath = Path.Combine(sessionDirectory, $"concat-{Guid.NewGuid():N}.txt");
                    WriteConcatFile(concatPath, snapshot);
                    var fallbackArguments = FfmpegCommandBuilder.BuildExportArguments(_settings, concatPath, outputPath);
                    await RunFfmpegOnceAsync(fallbackArguments, _lifetimeCts.Token);
                }
            }
            else
            {
                concatPath = Path.Combine(sessionDirectory, $"concat-{Guid.NewGuid():N}.txt");
                WriteConcatFile(concatPath, snapshot);
                var args = FfmpegCommandBuilder.BuildExportArguments(_settings, concatPath, outputPath);
                await RunFfmpegOnceAsync(args, _lifetimeCts.Token);
            }

            _lastSavedClipPath = outputPath;
            replaySavedInfo = new ReplaySavedInfo(
                outputPath,
                _activeMatch,
                ForegroundProcessProbe.TryResolveTargetWindow(_activeMatch) ?? _captureTargetWindow);
            _logger.Info($"Saved replay to {outputPath}");
        }
        finally
        {
            _stateMachine.MarkExportCompleted();

            try
            {
                if (concatPath is not null && File.Exists(concatPath))
                {
                    File.Delete(concatPath);
                }
            }
            catch
            {
                // Ignore temp cleanup issues.
            }

            PublishSnapshot();
        }

        if (replaySavedInfo is not null)
        {
            try
            {
                ReplaySaved?.Invoke(replaySavedInfo);
            }
            catch (Exception exception)
            {
                _logger.Error("Replay saved notification failed.", exception);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _lifetimeCts.Cancel();
        await StopCaptureAsync("Application shutdown.");

        if (_detectionLoopTask is not null)
        {
            try
            {
                await _detectionLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task StartCaptureInternalAsync(
        string? activeMatch,
        bool manualCapture,
        CaptureBackend? backendOverride = null)
    {
        await _gate.WaitAsync();

        try
        {
            if (_captureSession?.IsRunning == true)
            {
                return;
            }

            var targetWindow = ResolveCaptureTargetWindow(activeMatch);
            if (!manualCapture
                && _settings.CaptureMode == CaptureMode.Window
                && targetWindow is null)
            {
                _captureTargetWindow = null;
                _captureTargetDescription = "Not capturing";
                PublishSnapshot();
                return;
            }

            _activeMatch = activeMatch;
            _manualCapture = manualCapture;
            _autoCapture = !manualCapture;
            _currentSessionDirectory = Path.Combine(_paths.TempSessionsDirectory, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(_currentSessionDirectory);

            _captureTargetWindow = targetWindow;
            var effectiveCaptureMode = ResolveEffectiveCaptureMode(targetWindow);
            _captureTargetDescription = DescribeCaptureTarget(_captureTargetWindow, _settings.CaptureMode, effectiveCaptureMode);
            _logger.Info($"Capture target: {_captureTargetDescription}");
            (_captureSession, _activeCaptureBackend) = await StartPreferredCaptureSessionAsync(
                targetWindow,
                _currentSessionDirectory,
                effectiveCaptureMode,
                backendOverride);
            _captureSession.Exited += OnCaptureExited;

            lock (_segmentCatalogSync)
            {
                _segmentCatalog = new SegmentCatalog(_settings.SegmentSeconds);
            }

            var captureGeneration = ++_captureGeneration;
            var captureSession = _captureSession;
            var sessionDirectory = _currentSessionDirectory;
            _segmentLoopTask = Task.Run(
                () => RunSegmentLoopAsync(captureSession, sessionDirectory, captureGeneration, _lifetimeCts.Token),
                _lifetimeCts.Token);
            _stateMachine.MarkCaptureStarted(_activeMatch);
            PublishSnapshot();
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to start buffering.", exception);
            _stateMachine.MarkFault(exception.Message);
            PublishSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunDetectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await EvaluateDetectionAsync(cancellationToken);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunSegmentLoopAsync(
        IReplayCaptureSession session,
        string sessionDirectory,
        int captureGeneration,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsCurrentCaptureSession(session, sessionDirectory, captureGeneration))
            {
                break;
            }

            try
            {
                var segments = ReadStableSegments(
                    sessionDirectory,
                    DateTimeOffset.UtcNow - SegmentMonitorStabilityThreshold);

                if (!IsCurrentCaptureSession(session, sessionDirectory, captureGeneration))
                {
                    break;
                }

                IReadOnlyList<SegmentInfo> overflowSegments;
                lock (_segmentCatalogSync)
                {
                    _segmentCatalog.ReplaceSegments(segments);
                    overflowSegments = _segmentCatalog
                        .GetOverflowSegments(_settings.BufferSeconds, _settings.MaxTempStorageBytes)
                        .ToArray();
                }

                foreach (var segment in overflowSegments)
                {
                    try
                    {
                        if (File.Exists(segment.Path))
                        {
                            File.Delete(segment.Path);
                        }
                    }
                    catch
                    {
                        // A segment can still be locked for a moment; it will be retried later.
                    }
                }

                PublishSnapshot();
            }
            catch (Exception exception)
            {
                if (!IsCurrentCaptureSession(session, sessionDirectory, captureGeneration))
                {
                    break;
                }

                _logger.Error("Segment monitor failed.", exception);
                _stateMachine.MarkRecovering(exception.Message);
                PublishSnapshot();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PublishSnapshot()
    {
        int segmentCount;
        lock (_segmentCatalogSync)
        {
            segmentCount = _segmentCatalog.Segments.Count;
        }

        SnapshotChanged?.Invoke(new ReplayCoordinatorSnapshot(
            _settings.ReplayBufferEnabled,
            _settings.BufferActivationMode,
            _stateMachine.CurrentState,
            _stateMachine.StatusMessage,
            _activeMatch,
            _eligibleMatch,
            _captureSession?.IsRunning == true,
            segmentCount,
            _lastSavedClipPath,
            _settings.CaptureBackend,
            _activeCaptureBackend,
            _settings.FfmpegPath,
            _currentSessionDirectory,
            _captureTargetDescription,
            _captureTargetWindow,
            _eligibleTargetWindow));
    }

    private void OnCaptureExited(int exitCode)
    {
        _ = HandleCaptureExitedAsync(exitCode);
    }

    internal static IReadOnlyList<SegmentInfo> GetFinalizedSegments(IEnumerable<SegmentInfo> segments)
    {
        return segments
            .OrderBy(segment => segment.Sequence)
            .ToArray();
    }

    internal static IReadOnlyList<SegmentInfo> ReadStableSegments(
        string sessionDirectory,
        DateTimeOffset stableBeforeUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
        {
            return [];
        }

        return GetFinalizedSegments(Directory.EnumerateFiles(sessionDirectory, "segment-*.mp4", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => IsStableSegmentFile(file, stableBeforeUtc))
            .Select(file => new SegmentInfo(
                file.FullName,
                ParseSegmentSequence(file.Name),
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))));
    }

    private IReadOnlyList<SegmentInfo> GetLatestReplaySnapshot(
        string sessionDirectory,
        TimeSpan stabilityThreshold)
    {
        var stableSegments = ReadStableSegments(
            sessionDirectory,
            DateTimeOffset.UtcNow - stabilityThreshold);
        if (stableSegments.Count > 0)
        {
            var liveCatalog = new SegmentCatalog(_settings.SegmentSeconds);
            liveCatalog.ReplaceSegments(stableSegments);
            return liveCatalog.GetReplaySnapshot(_settings.BufferSeconds);
        }

        lock (_segmentCatalogSync)
        {
            return _segmentCatalog.GetReplaySnapshot(_settings.BufferSeconds).ToArray();
        }
    }

    private static bool IsStableSegmentFile(FileInfo file, DateTimeOffset stableBeforeUtc)
    {
        if (!file.Exists || file.Length <= 0 || file.LastWriteTimeUtc > stableBeforeUtc.UtcDateTime)
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task HandleCaptureExitedAsync(int exitCode)
    {
        if (exitCode == 0)
        {
            return;
        }

        string? restartMatch = null;
        CaptureBackend? backendOverride = null;
        var manualCapture = false;

        await _gate.WaitAsync();

        try
        {
            var failedBackend = _activeCaptureBackend;
            if (_captureSession is not null)
            {
                _captureSession.Exited -= OnCaptureExited;
            }

            _captureGeneration++;
            _captureSession = null;
            _currentSessionDirectory = null;
            lock (_segmentCatalogSync)
            {
                _segmentCatalog.ReplaceSegments([]);
            }

            if (failedBackend == CaptureBackend.Native
                && _settings.CaptureBackend == CaptureBackend.Native
                && CanUseFfmpeg()
                && (_manualCapture || _autoCapture)
                && !_lifetimeCts.IsCancellationRequested)
            {
                restartMatch = _activeMatch;
                manualCapture = _manualCapture;
                backendOverride = CaptureBackend.Ffmpeg;
                _stateMachine.MarkRecovering("Native capture stopped producing frames. Restarting with ffmpeg fallback.");
            }
            else
            {
                _stateMachine.MarkRecovering($"Capture process exited with code {exitCode}.");
            }

            PublishSnapshot();
        }
        finally
        {
            _gate.Release();
        }

        if (backendOverride is not null)
        {
            _logger.Warn("Native capture exited after startup and will restart with ffmpeg fallback.");
            await StartCaptureInternalAsync(restartMatch, manualCapture, backendOverride);
        }
    }

    private async Task<(IReplayCaptureSession Session, CaptureBackend Backend)> StartPreferredCaptureSessionAsync(
        CaptureTargetWindow? targetWindow,
        string sessionDirectory,
        CaptureMode effectiveCaptureMode,
        CaptureBackend? backendOverride = null)
    {
        var requestedBackend = backendOverride ?? _settings.CaptureBackend;
        if (ShouldPreferFfmpegCapture(requestedBackend, effectiveCaptureMode) && CanUseFfmpeg())
        {
            _logger.Info("Using ffmpeg capture because native display fallback drops replay time between segment rotations.");
            requestedBackend = CaptureBackend.Ffmpeg;
        }

        Exception? nativeFailure = null;

        if (requestedBackend == CaptureBackend.Native)
        {
            try
            {
                var nativeSession = new NativeCaptureSession(
                    _settings,
                    _logger,
                    CreateCaptureItem(targetWindow, effectiveCaptureMode),
                    sessionDirectory);
                await nativeSession.StartAsync();
                return (nativeSession, CaptureBackend.Native);
            }
            catch (Exception exception)
            {
                nativeFailure = exception;
                _logger.Warn($"Native capture could not start and will {(CanUseFfmpeg() ? "fall back to ffmpeg" : "stop")}. {exception.Message}");
            }
        }

        if (requestedBackend == CaptureBackend.Ffmpeg || CanUseFfmpeg())
        {
            var ffmpegSession = new FfmpegCaptureSession(_logger);
            var outputPattern = Path.Combine(sessionDirectory, "segment-%06d.mp4");
            var captureRegion = ResolveCaptureRegion(targetWindow, effectiveCaptureMode);
            var captureSource = FfmpegCaptureSourceResolver.TryResolve(
                _settings.FfmpegPath,
                _settings,
                _settings.CaptureMode,
                effectiveCaptureMode,
                targetWindow);
            if (captureSource is not null)
            {
                _logger.Info($"ffmpeg capture source: {captureSource.Name}");
            }

            var arguments = FfmpegCommandBuilder.BuildCaptureArguments(_settings, outputPattern, captureRegion, captureSource);
            await ffmpegSession.StartAsync(_settings.FfmpegPath, arguments);
            return (ffmpegSession, CaptureBackend.Ffmpeg);
        }

        if (nativeFailure is not null)
        {
            throw new InvalidOperationException(
                "Native capture failed and ffmpeg fallback is not available. Configure ffmpeg or switch to the ffmpeg backend.",
                nativeFailure);
        }

        throw new InvalidOperationException("ffmpeg was not found. Set the ffmpeg path in settings or switch back to the native backend.");
    }

    internal bool ShouldPreferFfmpegCapture(CaptureBackend requestedBackend, CaptureMode effectiveCaptureMode)
    {
        return ShouldPreferFfmpegCapture(requestedBackend, effectiveCaptureMode, _settings.CaptureMode);
    }

    internal static bool ShouldPreferFfmpegCapture(
        CaptureBackend requestedBackend,
        CaptureMode effectiveCaptureMode,
        CaptureMode requestedCaptureMode)
    {
        return requestedBackend == CaptureBackend.Native
            && requestedCaptureMode == CaptureMode.Window
            && effectiveCaptureMode == CaptureMode.Display;
    }

    private async Task RunFfmpegOnceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        EnsureFfmpegAvailable();

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stderrTask = PumpErrorsAsync(process, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}.");
        }
    }

    private GraphicsCaptureItem CreateCaptureItem(CaptureTargetWindow? targetWindow, CaptureMode effectiveCaptureMode)
    {
        if (effectiveCaptureMode == CaptureMode.Window)
        {
            if (targetWindow is null || targetWindow.Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("A valid game window is required for native window capture.");
            }

            return GraphicsCaptureItemFactory.CreateForWindow(targetWindow.Handle);
        }

        var monitor = MonitorLocator.GetCaptureMonitor(targetWindow);
        if (monitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("A display target could not be resolved for native capture.");
        }

        return GraphicsCaptureItemFactory.CreateForMonitor(monitor);
    }

    private bool CanUseFfmpeg()
    {
        try
        {
            EnsureFfmpegAvailable();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureFfmpegAvailable()
    {
        if (!string.IsNullOrWhiteSpace(_settings.FfmpegPath) && File.Exists(_settings.FfmpegPath))
        {
            return;
        }

        var discovered = _locator.FindBestPath();
        if (!string.IsNullOrWhiteSpace(discovered))
        {
            _settings.FfmpegPath = discovered;
        }

        if (string.IsNullOrWhiteSpace(_settings.FfmpegPath) || !File.Exists(_settings.FfmpegPath))
        {
            throw new InvalidOperationException("ffmpeg was not found. Set the ffmpeg path in settings.");
        }
    }

    private async Task PumpErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                _logger.Info($"ffmpeg: {line}");
            }
        }
    }

    private static void WriteConcatFile(string concatPath, IEnumerable<SegmentInfo> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            builder.Append("file '")
                .Append(segment.Path.Replace("'", "'\\''", StringComparison.Ordinal))
                .AppendLine("'");
        }

        File.WriteAllText(concatPath, builder.ToString());
    }

    private static int ParseSegmentSequence(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var token = name.Split('-').LastOrDefault();
        return int.TryParse(token, out var sequence) ? sequence : 0;
    }

    private bool IsCurrentCaptureSession(IReplayCaptureSession session, string sessionDirectory, int captureGeneration)
    {
        return captureGeneration == Volatile.Read(ref _captureGeneration)
            && ReferenceEquals(_captureSession, session)
            && string.Equals(_currentSessionDirectory, sessionDirectory, StringComparison.Ordinal)
            && session.IsRunning;
    }

    private static string? ResolveEligibleAutoCaptureMatch(GameMatchResult result, AppSettings settings)
    {
        if (!result.IsMatch)
        {
            return null;
        }

        if (settings.CaptureMode == CaptureMode.Display)
        {
            return result.MatchedExecutable;
        }

        var targetWindow = ForegroundProcessProbe.TryResolveTargetWindow(result.MatchedExecutable);
        return targetWindow?.ProcessName;
    }

    private async Task EvaluateDetectionAsync(CancellationToken cancellationToken)
    {
        bool entered;

        try
        {
            entered = await _detectionPassGate.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!entered)
        {
            return;
        }

        try
        {
            if (_settings.ReplayBufferEnabled)
            {
                string? eligibleMatch = null;
                if (_settings.AutoStartEnabled || _settings.BufferActivationMode == BufferActivationMode.HotkeyToggle)
                {
                    var runningProcessNames = ForegroundProcessProbe.GetRunningProcessNames();
                    var foregroundProcessName = _settings.RequireForegroundWindow
                        ? ForegroundProcessProbe.GetForegroundProcessName()
                        : null;
                    var result = _gameMatchEvaluator.Evaluate(runningProcessNames, foregroundProcessName, _settings);
                    eligibleMatch = ResolveEligibleAutoCaptureMatch(result, _settings);
                }

                _eligibleMatch = eligibleMatch;
                _eligibleTargetWindow = ResolveCaptureTargetWindow(_eligibleMatch);
                _stateMachine.SetEligibleTarget(_eligibleMatch);

                if (_settings.BufferActivationMode == BufferActivationMode.Automatic && _settings.AutoStartEnabled)
                {
                    var stableEligibleMatch = _autoStartDebouncer
                        .Observe(eligibleMatch, DateTimeOffset.UtcNow)
                        .StableExecutable;

                    if (!_manualCapture)
                    {
                        if (!string.IsNullOrWhiteSpace(stableEligibleMatch) && _captureSession?.IsRunning != true)
                        {
                            await StartCaptureInternalAsync(stableEligibleMatch, manualCapture: false);
                        }
                        else if (!string.IsNullOrWhiteSpace(stableEligibleMatch)
                            && _autoCapture
                            && _captureSession?.IsRunning == true
                            && !string.Equals(_activeMatch, stableEligibleMatch, StringComparison.OrdinalIgnoreCase))
                        {
                            await StopCaptureAsync($"Auto-switch because {stableEligibleMatch} became the active eligible game.");
                            await StartCaptureInternalAsync(stableEligibleMatch, manualCapture: false);
                        }
                        else if (string.IsNullOrWhiteSpace(stableEligibleMatch) && _autoCapture && _captureSession?.IsRunning == true)
                        {
                            await StopCaptureAsync("Auto-stop because no configured game window stayed eligible.");
                        }
                    }
                }
                else
                {
                    _autoStartDebouncer.Reset();
                }
            }
            else
            {
                _eligibleMatch = null;
                _eligibleTargetWindow = null;
                _autoStartDebouncer.Reset();
                _stateMachine.SetEligibleTarget(null);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Detection loop failed.", exception);
            _stateMachine.MarkRecovering(exception.Message);
        }
        finally
        {
            PublishSnapshot();
            _detectionPassGate.Release();
        }
    }

    private void EnsureSufficientExportSpace(string outputPath, IReadOnlyCollection<SegmentInfo> snapshot)
    {
        if (!StorageSpaceProbe.TryGetAvailableFreeBytes(outputPath, out var availableFreeBytes, out var failureReason))
        {
            var probeFailureMessage = $"Replay export blocked because the save drive could not be checked. {failureReason}";
            _logger.Warn(probeFailureMessage);
            throw new InvalidOperationException(probeFailureMessage);
        }

        var check = ExportDiskSpaceGuard.Evaluate(snapshot, availableFreeBytes);
        if (check.CanExport)
        {
            return;
        }

        var insufficientSpaceMessage = $"Replay export blocked because the save drive is critically low on space. Available: {FormatBytes(check.AvailableFreeBytes)}. Required free space: {FormatBytes(check.RequiredFreeBytes)}.";
        _logger.Warn(insufficientSpaceMessage);
        throw new InvalidOperationException(insufficientSpaceMessage);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{value} {units[unitIndex]}";
        }

        var scaledValue = Math.Max(0, bytes) / Math.Pow(1024, unitIndex);
        return $"{scaledValue:0.#} {units[unitIndex]}";
    }

    private CaptureTargetWindow? ResolveCaptureTargetWindow(string? activeMatch)
    {
        return ForegroundProcessProbe.TryResolveTargetWindow(activeMatch);
    }

    private static CaptureMode ResolveEffectiveCaptureMode(CaptureTargetWindow? targetWindow, CaptureMode requestedCaptureMode)
    {
        if (requestedCaptureMode == CaptureMode.Window && MonitorLocator.ShouldPreferMonitorCapture(targetWindow))
        {
            return CaptureMode.Display;
        }

        return requestedCaptureMode;
    }

    private CaptureMode ResolveEffectiveCaptureMode(CaptureTargetWindow? targetWindow)
    {
        return ResolveEffectiveCaptureMode(targetWindow, _settings.CaptureMode);
    }

    private static CaptureRegion? ResolveCaptureRegion(CaptureTargetWindow? targetWindow, CaptureMode effectiveCaptureMode)
    {
        if (effectiveCaptureMode == CaptureMode.Window)
        {
            return targetWindow?.ToCaptureRegion();
        }

        return MonitorLocator.TryGetMonitorBounds(targetWindow, out var monitorBounds)
            ? monitorBounds.ToCaptureRegion()
            : null;
    }

    private static string DescribeCaptureTarget(
        CaptureTargetWindow? targetWindow,
        CaptureMode requestedCaptureMode,
        CaptureMode effectiveCaptureMode)
    {
        if (effectiveCaptureMode == CaptureMode.Display)
        {
            return requestedCaptureMode == CaptureMode.Window && targetWindow is not null
                ? $"Fullscreen display capture fallback for {targetWindow.Description}"
                : "Full display capture";
        }

        return targetWindow?.Description ?? "Desktop fallback";
    }

    private string? ResolveManualCaptureMatch()
    {
        if (!string.IsNullOrWhiteSpace(_eligibleMatch))
        {
            return _eligibleMatch;
        }

        if (_settings.BufferActivationMode != BufferActivationMode.HotkeyToggle)
        {
            return _activeMatch;
        }

        var runningProcessNames = ForegroundProcessProbe.GetRunningProcessNames();
        var foregroundProcessName = _settings.RequireForegroundWindow
            ? ForegroundProcessProbe.GetForegroundProcessName()
            : null;
        var result = _gameMatchEvaluator.Evaluate(runningProcessNames, foregroundProcessName, _settings);
        return ResolveEligibleAutoCaptureMatch(result, _settings);
    }
}
