using System.Diagnostics;
using System.Text;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;
using Buffero.Core.Detection;
using Buffero.Core.State;

namespace Buffero.App.Infrastructure;

public sealed class ReplayCoordinator
{
    private static readonly TimeSpan AutoStartDebounceInterval = TimeSpan.FromSeconds(2.5);

    private readonly SemaphoreSlim _gate = new(1, 1);
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
    private FfmpegCaptureSession? _captureSession;
    private string? _currentSessionDirectory;
    private string? _activeMatch;
    private string? _lastSavedClipPath;
    private CaptureTargetWindow? _captureTargetWindow;
    private string _captureTargetDescription = "Desktop fallback";
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
        _settings.Normalize(_locator.FindBestPath());
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
        settings.Normalize(_locator.FindBestPath());

        var restartManual = _manualCapture;
        var restartAuto = _autoCapture && !string.IsNullOrWhiteSpace(_activeMatch);
        var restartMatch = _activeMatch;

        await StopCaptureAsync("Applying updated settings.");

        _settings = settings;
        _paths.EnsureDirectories(_settings.SaveDirectory);
        _segmentCatalog = new SegmentCatalog(_settings.SegmentSeconds);
        _autoStartDebouncer.Reset();

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
        _manualCapture = true;
        _autoCapture = false;
        await StartCaptureInternalAsync(_activeMatch, manualCapture: true);
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
                _currentSessionDirectory = null;
                _activeMatch = null;
                _captureTargetWindow = null;
                _captureTargetDescription = "Not capturing";
                _segmentCatalog.ReplaceSegments([]);
                _stateMachine.MarkCaptureStopped();
                PublishSnapshot();
                return;
            }

            _logger.Info(reason);
            await _captureSession.StopAsync();
            _captureSession = null;
            _currentSessionDirectory = null;
            _activeMatch = null;
            _captureTargetWindow = null;
            _captureTargetDescription = "Not capturing";
            _segmentCatalog.ReplaceSegments([]);
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
        string concatPath;
        string sessionDirectory;
        ReplaySavedInfo? replaySavedInfo = null;

        await _gate.WaitAsync();

        try
        {
            snapshot = _segmentCatalog.GetReplaySnapshot(_settings.BufferSeconds).ToList();
            if (snapshot.Count == 0)
            {
                throw new InvalidOperationException("No finalized replay segments are buffered yet.");
            }

            sessionDirectory = _currentSessionDirectory ?? Path.Combine(_paths.TempSessionsDirectory, "exports");
            Directory.CreateDirectory(sessionDirectory);

            outputPath = Path.Combine(
                _settings.SaveDirectory,
                ClipNameBuilder.Build(_settings.ClipFilePattern, DateTimeOffset.Now, _activeMatch));
            EnsureSufficientExportSpace(outputPath, snapshot);
            concatPath = Path.Combine(sessionDirectory, $"concat-{Guid.NewGuid():N}.txt");
            WriteConcatFile(concatPath, snapshot);
            _stateMachine.MarkExportQueued();
            PublishSnapshot();
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var args = FfmpegCommandBuilder.BuildExportArguments(_settings, concatPath, outputPath);
            await RunFfmpegOnceAsync(args, _lifetimeCts.Token);
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
                if (File.Exists(concatPath))
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

    private async Task StartCaptureInternalAsync(string? activeMatch, bool manualCapture)
    {
        await _gate.WaitAsync();

        try
        {
            if (_captureSession?.IsRunning == true)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.FfmpegPath) || !File.Exists(_settings.FfmpegPath))
            {
                var discovered = _locator.FindBestPath();
                if (!string.IsNullOrWhiteSpace(discovered))
                {
                    _settings.FfmpegPath = discovered;
                }
            }

            if (string.IsNullOrWhiteSpace(_settings.FfmpegPath) || !File.Exists(_settings.FfmpegPath))
            {
                _stateMachine.MarkFault("ffmpeg was not found. Set the ffmpeg path in settings.");
                PublishSnapshot();
                return;
            }

            var targetWindow = ForegroundProcessProbe.TryResolveTargetWindow(activeMatch);
            if (!manualCapture && targetWindow is null)
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

            var outputPattern = Path.Combine(_currentSessionDirectory, "segment-%06d.mp4");
            _captureTargetWindow = targetWindow;
            var captureRegion = _captureTargetWindow?.ToCaptureRegion();
            _captureTargetDescription = _captureTargetWindow?.Description ?? "Desktop fallback";
            _logger.Info($"Capture target: {_captureTargetDescription}");
            var arguments = FfmpegCommandBuilder.BuildCaptureArguments(_settings, outputPattern, captureRegion);

            _captureSession = new FfmpegCaptureSession(_logger);
            _captureSession.Exited += OnCaptureExited;
            await _captureSession.StartAsync(_settings.FfmpegPath, arguments);

            _segmentCatalog = new SegmentCatalog(_settings.SegmentSeconds);
            _segmentLoopTask = Task.Run(() => RunSegmentLoopAsync(_currentSessionDirectory, _lifetimeCts.Token), _lifetimeCts.Token);
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

    private async Task RunSegmentLoopAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _captureSession?.IsRunning == true)
        {
            try
            {
                var threshold = DateTimeOffset.UtcNow.AddSeconds(-1);
                var segments = Directory.EnumerateFiles(sessionDirectory, "segment-*.mp4", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists && file.Length > 0 && file.LastWriteTimeUtc <= threshold.UtcDateTime)
                    .Select(file => new SegmentInfo(
                        file.FullName,
                        ParseSegmentSequence(file.Name),
                        file.Length,
                        new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
                    .OrderBy(segment => segment.Sequence)
                    .ToArray();

                _segmentCatalog.ReplaceSegments(segments);

                foreach (var segment in _segmentCatalog.GetOverflowSegments(_settings.BufferSeconds, _settings.MaxTempStorageBytes))
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
        SnapshotChanged?.Invoke(new ReplayCoordinatorSnapshot(
            _stateMachine.CurrentState,
            _stateMachine.StatusMessage,
            _activeMatch,
            _captureSession?.IsRunning == true,
            _segmentCatalog.Segments.Count,
            _lastSavedClipPath,
            _settings.FfmpegPath,
            _currentSessionDirectory,
            _captureTargetDescription));
    }

    private void OnCaptureExited(int exitCode)
    {
        if (exitCode == 0)
        {
            return;
        }

        _stateMachine.MarkRecovering($"Capture process exited with code {exitCode}.");
        PublishSnapshot();
    }

    private async Task RunFfmpegOnceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
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

    private static string? ResolveEligibleAutoCaptureMatch(GameMatchResult result)
    {
        if (!result.IsMatch)
        {
            return null;
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
            if (_settings.AutoStartEnabled)
            {
                var runningProcessNames = ForegroundProcessProbe.GetRunningProcessNames();
                var foregroundProcessName = _settings.RequireForegroundWindow
                    ? ForegroundProcessProbe.GetForegroundProcessName()
                    : null;
                var result = _gameMatchEvaluator.Evaluate(runningProcessNames, foregroundProcessName, _settings);
                var eligibleMatch = ResolveEligibleAutoCaptureMatch(result);
                _stateMachine.SetEligibleTarget(eligibleMatch);
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
}
