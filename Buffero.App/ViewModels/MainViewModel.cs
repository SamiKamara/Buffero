using System.Collections.ObjectModel;
using System.Windows;
using Buffero.App.Infrastructure;
using Buffero.Core.Capture;
using Buffero.Core.Configuration;

namespace Buffero.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ReplayCoordinator _coordinator;
    private readonly SettingsStore _settingsStore;
    private readonly BufferoPaths _paths;
    private readonly FileLogger _logger;
    private AppSettings _appliedSettings;

    private UiMode _uiMode = global::Buffero.Core.Configuration.UiMode.Default;
    private double _defaultModeWindowWidth = AppSettings.DefaultModeDefaultWindowWidth;
    private double _defaultModeWindowHeight = AppSettings.DefaultModeDefaultWindowHeight;
    private double _advancedModeWindowWidth = AppSettings.AdvancedModeDefaultWindowWidth;
    private double _advancedModeWindowHeight = AppSettings.AdvancedModeDefaultWindowHeight;
    private string _stateName = "Idle";
    private string _statusMessage = "Waiting for an eligible game.";
    private string _statusDetails = "No replay buffer is running.";
    private string _saveDirectory = string.Empty;
    private string _ffmpegPath = string.Empty;
    private int _bufferSeconds = 30;
    private int _segmentSeconds = 2;
    private int _fps = 30;
    private int _qualityCrf = CaptureQualityEstimator.EstimateCrf(OutputResolutionMode.Native, 30, 6);
    private int _qualityBitrateMbps = 6;
    private QualityInputMode _qualityInputMode = QualityInputMode.Bitrate;
    private int _maxTempStorageGb = 4;
    private CaptureBackend _captureBackend = CaptureBackend.Native;
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
    private string? _qualityEstimatePreferredProcessName;

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

    public UiMode UiMode
    {
        get => _uiMode;
        set
        {
            if (SetProperty(ref _uiMode, value))
            {
                RaisePropertyChanged(nameof(IsAdvancedMode));
                RaisePropertyChanged(nameof(UiModeToggleButtonText));
                RaisePropertyChanged(nameof(MainContentColumnWidth));
                RaisePropertyChanged(nameof(SidePanelColumnWidth));
                RaisePropertyChanged(nameof(SidePanelMaxWidth));
            }
        }
    }

    public bool IsAdvancedMode => UiMode == global::Buffero.Core.Configuration.UiMode.Advanced;

    public GridLength MainContentColumnWidth => IsAdvancedMode
        ? new GridLength(2, GridUnitType.Star)
        : new GridLength(1, GridUnitType.Star);

    public GridLength SidePanelColumnWidth => IsAdvancedMode
        ? new GridLength(1.2, GridUnitType.Star)
        : GridLength.Auto;

    public double SidePanelMaxWidth => IsAdvancedMode
        ? double.PositiveInfinity
        : 188d;

    public string UiModeToggleButtonText => IsAdvancedMode
        ? "Switch To Default Mode"
        : "Switch To Advanced Mode";

    public double DefaultModeWindowWidth
    {
        get => _defaultModeWindowWidth;
        set => SetProperty(ref _defaultModeWindowWidth, value);
    }

    public double DefaultModeWindowHeight
    {
        get => _defaultModeWindowHeight;
        set => SetProperty(ref _defaultModeWindowHeight, value);
    }

    public double AdvancedModeWindowWidth
    {
        get => _advancedModeWindowWidth;
        set => SetProperty(ref _advancedModeWindowWidth, value);
    }

    public double AdvancedModeWindowHeight
    {
        get => _advancedModeWindowHeight;
        set => SetProperty(ref _advancedModeWindowHeight, value);
    }

    public ResolutionModeOption[] SupportedResolutionModes { get; } =
    [
        new(OutputResolutionMode.Native, "Native"),
        new(OutputResolutionMode.Max1080p, "Max 1080p"),
        new(OutputResolutionMode.Max720p, "Max 720p")
    ];

    public CaptureBackendOption[] SupportedCaptureBackends { get; } =
    [
        new(CaptureBackend.Native, "Native (Default)"),
        new(CaptureBackend.Ffmpeg, "ffmpeg Fallback")
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
        set
        {
            var clampedValue = Math.Min(value, AppSettings.MaxBufferSeconds);
            if (SetProperty(ref _bufferSeconds, clampedValue))
            {
                RaisePropertyChanged(nameof(ReplaySecondsHeader));
            }
        }
    }

    public string ReplaySecondsHeader => $"Replay Seconds {FormatReplayDuration(BufferSeconds)}";

    public int SegmentSeconds
    {
        get => _segmentSeconds;
        set => SetProperty(ref _segmentSeconds, value);
    }

    public int Fps
    {
        get => _fps;
        set
        {
            if (SetProperty(ref _fps, value))
            {
                RefreshDerivedQualityValues();
            }
        }
    }

    public int QualityCrf
    {
        get => _qualityCrf;
        set
        {
            if (SetProperty(ref _qualityCrf, value))
            {
                SetQualityInputMode(QualityInputMode.Crf);
                RefreshDerivedQualityValues();
                PersistQualitySettingsDraft();
            }
        }
    }

    public int QualityBitrateMbps
    {
        get => _qualityBitrateMbps;
        set
        {
            if (SetProperty(ref _qualityBitrateMbps, value))
            {
                SetQualityInputMode(QualityInputMode.Bitrate);
                RefreshDerivedQualityValues();
                PersistQualitySettingsDraft();
            }
        }
    }

    public bool IsCrfQualityActive => _qualityInputMode == QualityInputMode.Crf;

    public bool IsBitrateQualityActive => _qualityInputMode == QualityInputMode.Bitrate;

    public string CrfFieldStatus => IsCrfQualityActive ? "Active input" : "Estimated from Mb/s";

    public string BitrateFieldStatus => IsBitrateQualityActive ? "Active input" : "Estimated from CRF";

    public string QualityEstimateSummary
    {
        get
        {
            var activeControl = IsCrfQualityActive ? "CRF" : "Mb/s";
            var estimateResolution = GetQualityEstimateResolution();
            var outputDescription = estimateResolution.Source == QualityEstimateSource.ConfiguredGameWindow
                ? $"{estimateResolution.Width}x{estimateResolution.Height} based on the running configured game"
                : estimateResolution.Source == QualityEstimateSource.PrimaryScreen
                ? $"{estimateResolution.Width}x{estimateResolution.Height} based on the primary screen"
                : $"{estimateResolution.Width}x{estimateResolution.Height} reference output";

            return $"Last edited: {activeControl}. The inactive control is estimated using {outputDescription} at {CaptureQualityEstimator.ClampFps(Fps)} FPS; actual results still vary by content.";
        }
    }

    public int MaxTempStorageGb
    {
        get => _maxTempStorageGb;
        set => SetProperty(ref _maxTempStorageGb, value);
    }

    public CaptureBackend CaptureBackend
    {
        get => _captureBackend;
        set => SetProperty(ref _captureBackend, value);
    }

    public CaptureMode CaptureMode
    {
        get => _captureMode;
        set => SetProperty(ref _captureMode, value);
    }

    public OutputResolutionMode OutputResolution
    {
        get => _outputResolution;
        set
        {
            if (SetProperty(ref _outputResolution, value))
            {
                RefreshDerivedQualityValues();
            }
        }
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
        set
        {
            if (SetProperty(ref _allowedExecutablesText, value))
            {
                RefreshDerivedQualityValues();
            }
        }
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

    public void ToggleUiMode()
    {
        var nextMode = IsAdvancedMode
            ? global::Buffero.Core.Configuration.UiMode.Default
            : global::Buffero.Core.Configuration.UiMode.Advanced;
        UiMode = nextMode;

        try
        {
            var settings = CloneSettings(_appliedSettings);
            settings.UiMode = nextMode;
            var estimateResolution = GetQualityEstimateResolution();
            _settingsStore.Save(_paths.SettingsFilePath, settings, estimateResolution.Width, estimateResolution.Height);
            _appliedSettings = settings;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to persist UI mode. {exception.Message}");
        }
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

    public (double Width, double Height) GetWindowSize(UiMode uiMode)
    {
        return uiMode switch
        {
            global::Buffero.Core.Configuration.UiMode.Advanced => (AdvancedModeWindowWidth, AdvancedModeWindowHeight),
            _ => (DefaultModeWindowWidth, DefaultModeWindowHeight)
        };
    }

    public (double Width, double Height) GetAppliedWindowSize(UiMode uiMode)
    {
        return _appliedSettings.GetWindowSize(uiMode);
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
            UiMode = UiMode,
            DefaultModeWindowWidth = DefaultModeWindowWidth,
            DefaultModeWindowHeight = DefaultModeWindowHeight,
            AdvancedModeWindowWidth = AdvancedModeWindowWidth,
            AdvancedModeWindowHeight = AdvancedModeWindowHeight,
            ReplayBufferEnabled = ReplayBufferEnabled,
            StartWithWindows = StartWithWindows,
            AutoStartEnabled = AutoStartEnabled,
            RequireForegroundWindow = RequireForegroundWindow,
            SaveDirectory = SaveDirectory,
            BufferSeconds = BufferSeconds,
            SegmentSeconds = SegmentSeconds,
            Fps = Fps,
            QualityCrf = QualityCrf,
            QualityInputMode = _qualityInputMode,
            QualityBitrateMbps = QualityBitrateMbps,
            MaxTempStorageGb = MaxTempStorageGb,
            CaptureBackend = CaptureBackend,
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

        var estimateResolution = GetQualityEstimateResolution();
        settings.Normalize(settings.FfmpegPath, estimateResolution.Width, estimateResolution.Height);
        return settings;
    }

    private void PopulateFromSettings(AppSettings settings)
    {
        UiMode = settings.UiMode;
        DefaultModeWindowWidth = settings.DefaultModeWindowWidth;
        DefaultModeWindowHeight = settings.DefaultModeWindowHeight;
        AdvancedModeWindowWidth = settings.AdvancedModeWindowWidth;
        AdvancedModeWindowHeight = settings.AdvancedModeWindowHeight;
        SaveDirectory = settings.SaveDirectory;
        FfmpegPath = settings.FfmpegPath;
        BufferSeconds = settings.BufferSeconds;
        SegmentSeconds = settings.SegmentSeconds;
        Fps = settings.Fps;
        MaxTempStorageGb = settings.MaxTempStorageGb;
        CaptureBackend = settings.CaptureBackend;
        CaptureMode = settings.CaptureMode;
        OutputResolution = settings.OutputResolution;
        ApplyQualitySettings(settings);
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

    private QualityEstimateResolution GetQualityEstimateResolution()
    {
        return QualityEstimateResolutionProbe.Resolve(
            OutputResolution,
            GetConfiguredExecutables(),
            _qualityEstimatePreferredProcessName);
    }

    private void ApplySnapshot(ReplayCoordinatorSnapshot snapshot)
    {
        var previousEstimateResolution = GetQualityEstimateResolution();
        _qualityEstimatePreferredProcessName = snapshot.ActiveMatch;
        StateName = snapshot.IsReplayBufferEnabled ? snapshot.State.ToString() : "Disabled";
        StatusMessage = snapshot.IsReplayBufferEnabled
            ? snapshot.StatusMessage
            : "Replay buffer disabled.";
        StatusDetails = snapshot.IsReplayBufferEnabled
            ? snapshot.IsCapturing
                ? $"Buffered segments: {snapshot.BufferedSegmentCount}. Backend: {FormatCaptureBackend(snapshot.ActiveCaptureBackend)}. Target: {snapshot.CaptureTargetDescription}."
                : HotkeyStatus
            : "Enable the replay buffer to resume auto-capture and replay saves.";
        IsCapturing = snapshot.IsCapturing;
        if (previousEstimateResolution != GetQualityEstimateResolution())
        {
            RefreshDerivedQualityValues();
        }

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
            $"Configured Capture Backend: {FormatCaptureBackend(CaptureBackend)}",
            $"Active Capture Backend: {FormatCaptureBackend(snapshot?.ActiveCaptureBackend ?? CaptureBackend)}",
            $"ffmpeg: {(snapshot?.FfmpegPath ?? FfmpegPath)}",
            $"Current Session: {snapshot?.SessionDirectory ?? "(none)"}",
            $"Last Saved Clip: {snapshot?.LastSavedClipPath ?? "(none)"}",
            $"Capture Target: {snapshot?.CaptureTargetDescription ?? "(none)"}",
            $"Replay Buffer Enabled: {ReplayBufferEnabled}",
            $"Capture Mode: {FormatCaptureMode(CaptureMode)}",
            $"Capture Resolution: {FormatResolutionMode(OutputResolution)}",
            $"Replay Save Overlays + Notifications: {NotificationsEnabled}",
            $"Save Drive Free Space: {saveDriveFreeSpace}",
            $"Temp Drive Free Space: {tempDriveFreeSpace}",
            $"Hotkey: {HotkeyStatus}",
            $"Start With Windows: {StartWithWindows}",
            "Audio: currently disabled; video export only."
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

    private static string FormatCaptureBackend(CaptureBackend captureBackend)
    {
        return captureBackend switch
        {
            CaptureBackend.Ffmpeg => "ffmpeg + gdigrab",
            _ => "Native Windows capture"
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

    private static string FormatReplayDuration(int totalSeconds)
    {
        var clampedSeconds = Math.Max(0, totalSeconds);
        var minutes = clampedSeconds / 60;
        var seconds = clampedSeconds % 60;

        if (minutes > 0 && seconds > 0)
        {
            return $"({minutes} min {seconds} sec)";
        }

        if (minutes > 0)
        {
            return $"({minutes} min)";
        }

        return $"({seconds} sec)";
    }

    private void ApplyQualitySettings(AppSettings settings)
    {
        _qualityInputMode = settings.QualityInputMode;
        _qualityCrf = settings.QualityCrf;
        _qualityBitrateMbps = settings.QualityBitrateMbps;
        RaisePropertyChanged(nameof(QualityCrf));
        RaisePropertyChanged(nameof(QualityBitrateMbps));
        RaiseQualityPresentationChanged();
        RefreshDerivedQualityValues();
    }

    private IReadOnlyList<string> GetConfiguredExecutables()
    {
        return AllowedExecutablesText
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private void RefreshDerivedQualityValues()
    {
        if (_qualityInputMode == QualityInputMode.Bitrate)
        {
            var estimateResolution = GetQualityEstimateResolution();
            SetProperty(
                ref _qualityBitrateMbps,
                CaptureQualityEstimator.ClampBitrateMbps(_qualityBitrateMbps),
                nameof(QualityBitrateMbps));
            SetProperty(
                ref _qualityCrf,
                CaptureQualityEstimator.EstimateCrf(
                    estimateResolution.Width,
                    estimateResolution.Height,
                    Fps,
                    CaptureQualityEstimator.MegabitsPerSecondToBitsPerSecond(_qualityBitrateMbps)),
                nameof(QualityCrf));
        }
        else
        {
            var estimateResolution = GetQualityEstimateResolution();
            SetProperty(
                ref _qualityCrf,
                CaptureQualityEstimator.ClampCrf(_qualityCrf),
                nameof(QualityCrf));
            SetProperty(
                ref _qualityBitrateMbps,
                CaptureQualityEstimator.EstimateBitrateMbps(
                    estimateResolution.Width,
                    estimateResolution.Height,
                    Fps,
                    _qualityCrf),
                nameof(QualityBitrateMbps));
        }

        RaiseQualityPresentationChanged();
    }

    private void SetQualityInputMode(QualityInputMode qualityInputMode)
    {
        if (_qualityInputMode == qualityInputMode)
        {
            return;
        }

        _qualityInputMode = qualityInputMode;
        RaiseQualityPresentationChanged();
    }

    private void RaiseQualityPresentationChanged()
    {
        RaisePropertyChanged(nameof(IsCrfQualityActive));
        RaisePropertyChanged(nameof(IsBitrateQualityActive));
        RaisePropertyChanged(nameof(CrfFieldStatus));
        RaisePropertyChanged(nameof(BitrateFieldStatus));
        RaisePropertyChanged(nameof(QualityEstimateSummary));
    }

    private void PersistQualitySettingsDraft()
    {
        try
        {
            var settings = CloneSettings(_appliedSettings);
            settings.QualityCrf = _qualityCrf;
            settings.QualityInputMode = _qualityInputMode;
            settings.QualityBitrateMbps = _qualityBitrateMbps;
            var estimateResolution = QualityEstimateResolutionProbe.Resolve(settings, _qualityEstimatePreferredProcessName);
            _settingsStore.Save(_paths.SettingsFilePath, settings, estimateResolution.Width, estimateResolution.Height);
            _appliedSettings = settings;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to persist quality settings draft. {exception.Message}");
        }
    }

    private async Task ApplySettingsCoreAsync(AppSettings settings, string logMessage)
    {
        var estimateResolution = GetQualityEstimateResolution();
        settings.Normalize(settings.FfmpegPath, estimateResolution.Width, estimateResolution.Height);
        _paths.EnsureDirectories(settings.SaveDirectory);
        _settingsStore.Save(_paths.SettingsFilePath, settings, estimateResolution.Width, estimateResolution.Height);
        await _coordinator.ApplySettingsAsync(settings);
        _appliedSettings = CloneSettings(settings);
        PopulateFromSettings(settings);
        _logger.Info(logMessage);
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            UiMode = settings.UiMode,
            DefaultModeWindowWidth = settings.DefaultModeWindowWidth,
            DefaultModeWindowHeight = settings.DefaultModeWindowHeight,
            AdvancedModeWindowWidth = settings.AdvancedModeWindowWidth,
            AdvancedModeWindowHeight = settings.AdvancedModeWindowHeight,
            ReplayBufferEnabled = settings.ReplayBufferEnabled,
            StartWithWindows = settings.StartWithWindows,
            AutoStartEnabled = settings.AutoStartEnabled,
            RequireForegroundWindow = settings.RequireForegroundWindow,
            SaveDirectory = settings.SaveDirectory,
            BufferSeconds = settings.BufferSeconds,
            SegmentSeconds = settings.SegmentSeconds,
            Fps = settings.Fps,
            QualityCrf = settings.QualityCrf,
            QualityInputMode = settings.QualityInputMode,
            QualityBitrateMbps = settings.QualityBitrateMbps,
            MaxTempStorageGb = settings.MaxTempStorageGb,
            CaptureBackend = settings.CaptureBackend,
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

public sealed record CaptureBackendOption(CaptureBackend Value, string Label);

public sealed record CaptureModeOption(CaptureMode Value, string Label);
