using System.Collections.ObjectModel;
using Buffero.App.Infrastructure;
using Buffero.Core.Configuration;

namespace Buffero.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ReplayCoordinator _coordinator;
    private readonly SettingsStore _settingsStore;
    private readonly BufferoPaths _paths;
    private readonly FileLogger _logger;
    private AppSettings _appliedSettings;

    private string _stateName = "Idle";
    private string _statusMessage = "Waiting for an eligible game.";
    private string _statusDetails = "No replay buffer is running.";
    private string _saveDirectory = string.Empty;
    private string _ffmpegPath = string.Empty;
    private int _bufferSeconds = 30;
    private int _segmentSeconds = 2;
    private int _fps = 30;
    private int _qualityCrf = 23;
    private int _maxTempStorageGb = 4;
    private CaptureMode _captureMode = CaptureMode.Window;
    private OutputResolutionMode _outputResolution = OutputResolutionMode.Native;
    private string _clipFilePattern = "Buffero-{timestamp}-{game}";
    private bool _notificationsEnabled = true;
    private bool _replayBufferEnabled = true;
    private bool _startWithWindows;
    private bool _autoStartEnabled = true;
    private bool _requireForegroundWindow = true;
    private string _allowedExecutablesText = string.Empty;
    private bool _hotkeyCtrl = true;
    private bool _hotkeyAlt;
    private bool _hotkeyShift = true;
    private string _selectedHotkeyKey = "F8";
    private string _diagnosticsText = string.Empty;
    private bool _isCapturing;
    private string _hotkeyStatus = "Save hotkey is not registered.";
    private bool _hotkeyAvailable;

    public MainViewModel(
        AppSettings initialSettings,
        SettingsStore settingsStore,
        BufferoPaths paths,
        FileLogger logger,
        ReplayCoordinator coordinator)
    {
        _settingsStore = settingsStore;
        _paths = paths;
        _logger = logger;
        _coordinator = coordinator;
        _appliedSettings = CloneSettings(initialSettings);

        PopulateFromSettings(initialSettings);

        _coordinator.SnapshotChanged += snapshot =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplySnapshot(snapshot));
        };

        _logger.LineLogged += line =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RecentLogLines.Insert(0, line);
                while (RecentLogLines.Count > 200)
                {
                    RecentLogLines.RemoveAt(RecentLogLines.Count - 1);
                }
            });
        };
    }

    public ObservableCollection<string> RecentLogLines { get; } = [];

    public string[] SupportedHotkeyKeys => HotkeyBinding.SupportedKeys;

    public ResolutionModeOption[] SupportedResolutionModes { get; } =
    [
        new(OutputResolutionMode.Native, "Native"),
        new(OutputResolutionMode.Max1080p, "Max 1080p"),
        new(OutputResolutionMode.Max720p, "Max 720p")
    ];

    public CaptureModeOption[] SupportedCaptureModes { get; } =
    [
        new(CaptureMode.Window, "Window"),
        new(CaptureMode.Display, "Display")
    ];

    public string StateName
    {
        get => _stateName;
        private set => SetProperty(ref _stateName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string StatusDetails
    {
        get => _statusDetails;
        private set => SetProperty(ref _statusDetails, value);
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetProperty(ref _saveDirectory, value);
    }

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set => SetProperty(ref _ffmpegPath, value);
    }

    public int BufferSeconds
    {
        get => _bufferSeconds;
        set => SetProperty(ref _bufferSeconds, value);
    }

    public int SegmentSeconds
    {
        get => _segmentSeconds;
        set => SetProperty(ref _segmentSeconds, value);
    }

    public int Fps
    {
        get => _fps;
        set => SetProperty(ref _fps, value);
    }

    public int QualityCrf
    {
        get => _qualityCrf;
        set => SetProperty(ref _qualityCrf, value);
    }

    public int MaxTempStorageGb
    {
        get => _maxTempStorageGb;
        set => SetProperty(ref _maxTempStorageGb, value);
    }

    public CaptureMode CaptureMode
    {
        get => _captureMode;
        set => SetProperty(ref _captureMode, value);
    }

    public OutputResolutionMode OutputResolution
    {
        get => _outputResolution;
        set => SetProperty(ref _outputResolution, value);
    }

    public string ClipFilePattern
    {
        get => _clipFilePattern;
        set => SetProperty(ref _clipFilePattern, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set => SetProperty(ref _autoStartEnabled, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool ReplayBufferEnabled
    {
        get => _replayBufferEnabled;
        set => SetProperty(ref _replayBufferEnabled, value);
    }

    public bool RequireForegroundWindow
    {
        get => _requireForegroundWindow;
        set => SetProperty(ref _requireForegroundWindow, value);
    }

    public string AllowedExecutablesText
    {
        get => _allowedExecutablesText;
        set => SetProperty(ref _allowedExecutablesText, value);
    }

    public bool HotkeyCtrl
    {
        get => _hotkeyCtrl;
        set
        {
            if (SetProperty(ref _hotkeyCtrl, value))
            {
                RaisePropertyChanged(nameof(HotkeyPreview));
            }
        }
    }

    public bool HotkeyAlt
    {
        get => _hotkeyAlt;
        set
        {
            if (SetProperty(ref _hotkeyAlt, value))
            {
                RaisePropertyChanged(nameof(HotkeyPreview));
            }
        }
    }

    public bool HotkeyShift
    {
        get => _hotkeyShift;
        set
        {
            if (SetProperty(ref _hotkeyShift, value))
            {
                RaisePropertyChanged(nameof(HotkeyPreview));
            }
        }
    }

    public string SelectedHotkeyKey
    {
        get => _selectedHotkeyKey;
        set
        {
            if (SetProperty(ref _selectedHotkeyKey, value))
            {
                RaisePropertyChanged(nameof(HotkeyPreview));
            }
        }
    }

    public string HotkeyPreview => BuildHotkeyBinding().ToDisplayString();

    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set => SetProperty(ref _diagnosticsText, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        private set => SetProperty(ref _isCapturing, value);
    }

    public string HotkeyStatus
    {
        get => _hotkeyStatus;
        private set => SetProperty(ref _hotkeyStatus, value);
    }

    public bool HotkeyAvailable
    {
        get => _hotkeyAvailable;
        private set => SetProperty(ref _hotkeyAvailable, value);
    }

    public async Task ApplySettingsAsync()
    {
        var settings = BuildSettings();
        await ApplySettingsCoreAsync(settings, "Settings saved.");
    }

    public async Task SetReplayBufferEnabledAsync(bool isEnabled)
    {
        if (_appliedSettings.ReplayBufferEnabled == isEnabled)
        {
            ReplayBufferEnabled = isEnabled;
            return;
        }

        var settings = CloneSettings(_appliedSettings);
        settings.ReplayBufferEnabled = isEnabled;
        await ApplySettingsCoreAsync(
            settings,
            isEnabled ? "Replay buffer enabled." : "Replay buffer disabled.");
    }

    public HotkeyBinding BuildHotkeyBinding()
    {
        var binding = new HotkeyBinding
        {
            Ctrl = HotkeyCtrl,
            Alt = HotkeyAlt,
            Shift = HotkeyShift,
            Key = SelectedHotkeyKey
        };

        binding.Normalize();
        return binding;
    }

    public void SetHotkeyStatus(bool isAvailable, string message)
    {
        HotkeyAvailable = isAvailable;
        HotkeyStatus = message;
        if (!IsCapturing)
        {
            StatusDetails = message;
        }

        UpdateDiagnostics(null);
    }

    private AppSettings BuildSettings()
    {
        var settings = new AppSettings
        {
            ReplayBufferEnabled = ReplayBufferEnabled,
            StartWithWindows = StartWithWindows,
            AutoStartEnabled = AutoStartEnabled,
            RequireForegroundWindow = RequireForegroundWindow,
            SaveDirectory = SaveDirectory,
            BufferSeconds = BufferSeconds,
            SegmentSeconds = SegmentSeconds,
            Fps = Fps,
            QualityCrf = QualityCrf,
            MaxTempStorageGb = MaxTempStorageGb,
            CaptureMode = CaptureMode,
            OutputResolution = OutputResolution,
            NotificationsEnabled = NotificationsEnabled,
            SaveReplayHotkey = BuildHotkeyBinding(),
            FfmpegPath = FfmpegPath,
            ClipFilePattern = ClipFilePattern,
            AllowedExecutables = AllowedExecutablesText
                .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
        };

        settings.Normalize(settings.FfmpegPath);
        return settings;
    }

    private void PopulateFromSettings(AppSettings settings)
    {
        SaveDirectory = settings.SaveDirectory;
        FfmpegPath = settings.FfmpegPath;
        BufferSeconds = settings.BufferSeconds;
        SegmentSeconds = settings.SegmentSeconds;
        Fps = settings.Fps;
        QualityCrf = settings.QualityCrf;
        MaxTempStorageGb = settings.MaxTempStorageGb;
        CaptureMode = settings.CaptureMode;
        OutputResolution = settings.OutputResolution;
        ClipFilePattern = settings.ClipFilePattern;
        NotificationsEnabled = settings.NotificationsEnabled;
        ReplayBufferEnabled = settings.ReplayBufferEnabled;
        StartWithWindows = settings.StartWithWindows;
        AutoStartEnabled = settings.AutoStartEnabled;
        RequireForegroundWindow = settings.RequireForegroundWindow;
        AllowedExecutablesText = string.Join(Environment.NewLine, settings.AllowedExecutables);
        HotkeyCtrl = settings.SaveReplayHotkey.Ctrl;
        HotkeyAlt = settings.SaveReplayHotkey.Alt;
        HotkeyShift = settings.SaveReplayHotkey.Shift;
        SelectedHotkeyKey = settings.SaveReplayHotkey.Key;
        RaisePropertyChanged(nameof(HotkeyPreview));
        UpdateDiagnostics(null);
    }

    private void ApplySnapshot(ReplayCoordinatorSnapshot snapshot)
    {
        StateName = snapshot.IsReplayBufferEnabled ? snapshot.State.ToString() : "Disabled";
        StatusMessage = snapshot.IsReplayBufferEnabled
            ? snapshot.StatusMessage
            : "Replay buffer disabled.";
        StatusDetails = snapshot.IsReplayBufferEnabled
            ? snapshot.IsCapturing
                ? $"Buffered segments: {snapshot.BufferedSegmentCount}. Target: {snapshot.CaptureTargetDescription}."
                : HotkeyStatus
            : "Enable the replay buffer to resume auto-capture and replay saves.";
        IsCapturing = snapshot.IsCapturing;
        UpdateDiagnostics(snapshot);
    }

    private void UpdateDiagnostics(ReplayCoordinatorSnapshot? snapshot)
    {
        var saveDriveFreeSpace = TryFormatDriveFreeSpace(snapshot?.LastSavedClipPath ?? SaveDirectory);
        var tempDriveFreeSpace = TryFormatDriveFreeSpace(_paths.TempSessionsDirectory);

        DiagnosticsText = string.Join(Environment.NewLine,
        [
            $"Settings File: {_paths.SettingsFilePath}",
            $"Temp Session Root: {_paths.TempSessionsDirectory}",
            $"Log File: {_logger.LogFilePath}",
            $"ffmpeg: {(snapshot?.FfmpegPath ?? FfmpegPath)}",
            $"Current Session: {snapshot?.SessionDirectory ?? "(none)"}",
            $"Last Saved Clip: {snapshot?.LastSavedClipPath ?? "(none)"}",
            $"Capture Target: {snapshot?.CaptureTargetDescription ?? "(none)"}",
            $"Replay Buffer Enabled: {ReplayBufferEnabled}",
            $"Capture Mode: {FormatCaptureMode(CaptureMode)}",
            $"Capture Resolution: {FormatResolutionMode(OutputResolution)}",
            $"Replay Saved Notifications: {NotificationsEnabled}",
            $"Save Drive Free Space: {saveDriveFreeSpace}",
            $"Temp Drive Free Space: {tempDriveFreeSpace}",
            $"Hotkey: {HotkeyStatus}",
            $"Start With Windows: {StartWithWindows}",
            "Audio: intentionally disabled in this MVP; video export only."
        ]);
    }

    private static string TryFormatDriveFreeSpace(string path)
    {
        if (!StorageSpaceProbe.TryGetAvailableFreeBytes(path, out var availableFreeBytes, out _))
        {
            return "(unavailable)";
        }

        return FormatBytes(availableFreeBytes);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var scaledValue = Math.Max(0, (double)bytes);
        var unitIndex = 0;

        while (scaledValue >= 1024 && unitIndex < units.Length - 1)
        {
            scaledValue /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{scaledValue:0} {units[unitIndex]}"
            : $"{scaledValue:0.#} {units[unitIndex]}";
    }

    private static string FormatResolutionMode(OutputResolutionMode outputResolution)
    {
        return outputResolution switch
        {
            OutputResolutionMode.Max1080p => "Max 1080p",
            OutputResolutionMode.Max720p => "Max 720p",
            _ => "Native"
        };
    }

    private static string FormatCaptureMode(CaptureMode captureMode)
    {
        return captureMode switch
        {
            CaptureMode.Display => "Display",
            _ => "Window"
        };
    }

    private async Task ApplySettingsCoreAsync(AppSettings settings, string logMessage)
    {
        settings.Normalize(settings.FfmpegPath);
        _paths.EnsureDirectories(settings.SaveDirectory);
        _settingsStore.Save(_paths.SettingsFilePath, settings);
        await _coordinator.ApplySettingsAsync(settings);
        _appliedSettings = CloneSettings(settings);
        PopulateFromSettings(settings);
        _logger.Info(logMessage);
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            ReplayBufferEnabled = settings.ReplayBufferEnabled,
            StartWithWindows = settings.StartWithWindows,
            AutoStartEnabled = settings.AutoStartEnabled,
            RequireForegroundWindow = settings.RequireForegroundWindow,
            SaveDirectory = settings.SaveDirectory,
            BufferSeconds = settings.BufferSeconds,
            SegmentSeconds = settings.SegmentSeconds,
            Fps = settings.Fps,
            QualityCrf = settings.QualityCrf,
            MaxTempStorageGb = settings.MaxTempStorageGb,
            SaveReplayHotkey = new HotkeyBinding
            {
                Ctrl = settings.SaveReplayHotkey.Ctrl,
                Alt = settings.SaveReplayHotkey.Alt,
                Shift = settings.SaveReplayHotkey.Shift,
                Key = settings.SaveReplayHotkey.Key
            },
            FfmpegPath = settings.FfmpegPath,
            IncludeSystemAudio = settings.IncludeSystemAudio,
            NotificationsEnabled = settings.NotificationsEnabled,
            CaptureMode = settings.CaptureMode,
            OutputResolution = settings.OutputResolution,
            ClipFilePattern = settings.ClipFilePattern,
            AllowedExecutables = [.. settings.AllowedExecutables]
        };
    }
}

public sealed record ResolutionModeOption(OutputResolutionMode Value, string Label);

public sealed record CaptureModeOption(CaptureMode Value, string Label);
