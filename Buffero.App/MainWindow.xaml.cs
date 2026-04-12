using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using Buffero.App.Infrastructure;
using Buffero.App.ViewModels;
using Buffero.Core.Configuration;

namespace Buffero.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ReplayCoordinator _coordinator;
    private readonly TrayIconHost _trayIcon;
    private readonly HotkeyManager _saveHotkeyManager;
    private readonly HotkeyManager _toggleBufferHotkeyManager;
    private readonly GameOverlayNotifier _gameOverlayNotifier;
    private readonly ForegroundWindowHook _foregroundWindowHook;
    private readonly FileLogger _logger;
    private readonly BufferoPaths _paths;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly bool _startHidden;
    private bool _isExiting;

    public MainWindow(
        AppSettings settings,
        SettingsStore settingsStore,
        BufferoPaths paths,
        FileLogger logger,
        FfmpegLocator ffmpegLocator,
        StartupRegistrationService startupRegistrationService,
        bool startHidden)
    {
        InitializeComponent();

        _logger = logger;
        _paths = paths;
        _startupRegistrationService = startupRegistrationService;
        _startHidden = startHidden;
        _coordinator = new ReplayCoordinator(settings, paths, logger, ffmpegLocator);
        _viewModel = new MainViewModel(settings, settingsStore, paths, logger, _coordinator);
        _trayIcon = new TrayIconHost();
        _trayIcon.SetReplaySavedNotificationsEnabled(settings.NotificationsEnabled);
        _saveHotkeyManager = new HotkeyManager("Save hotkey", 0xB00F, 0xB010);
        _toggleBufferHotkeyManager = new HotkeyManager("Buffer toggle hotkey", 0xB011, 0xB012);
        _gameOverlayNotifier = new GameOverlayNotifier();
        _gameOverlayNotifier.UpdateBufferingWidgetOpacity(settings.BufferingWidgetOpacity);
        _foregroundWindowHook = new ForegroundWindowHook();

        DataContext = _viewModel;
        ApplyPersistedWindowSizeForCurrentMode();

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;

        _trayIcon.OpenRequested += ShowFromTray;
        _trayIcon.SaveReplayRequested += async () => await Dispatcher.InvokeAsync(async () => await SaveReplayInternalAsync());
        _trayIcon.ToggleBufferRequested += async () => await Dispatcher.InvokeAsync(async () => await ToggleReplayBufferEnabledAsync());
        _trayIcon.OpenClipsRequested += () => Dispatcher.Invoke(() => OpenFolder(_viewModel.SaveDirectory));
        _trayIcon.ExitRequested += async () => await Dispatcher.InvokeAsync(async () => await ExitApplicationAsync());

        _saveHotkeyManager.Pressed += async () =>
        {
            _logger.Info("Global hotkey pressed.");
            await Dispatcher.InvokeAsync(async () => await SaveReplayInternalAsync());
        };

        _toggleBufferHotkeyManager.Pressed += async () =>
        {
            _logger.Info("Buffer toggle hotkey pressed.");
            await Dispatcher.InvokeAsync(async () => await ToggleBufferingAsync("Stopped from buffer toggle hotkey."));
        };

        _saveHotkeyManager.RegistrationChanged += (isRegistered, message) =>
        {
            if (isRegistered)
            {
                _logger.Info(message);
            }
            else
            {
                _logger.Warn(message);
            }

            Dispatcher.Invoke(() => _viewModel.SetHotkeyStatus(isRegistered, message));
        };

        _toggleBufferHotkeyManager.RegistrationChanged += (isRegistered, message) =>
        {
            if (isRegistered)
            {
                _logger.Info(message);
            }
            else
            {
                _logger.Warn(message);
            }

            Dispatcher.Invoke(() => _viewModel.SetToggleHotkeyStatus(isRegistered, message));
        };

        _coordinator.SnapshotChanged += snapshot =>
        {
            Dispatcher.Invoke(() =>
            {
                _trayIcon.Update(snapshot);
                _gameOverlayNotifier.UpdateBufferingStatus(snapshot);
            });
        };

        _coordinator.ReplaySavingStarted += replaySavingInfo =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_viewModel.NotificationsEnabled)
                {
                    _gameOverlayNotifier.ShowRecordingSaving(replaySavingInfo.TargetWindow, replaySavingInfo.OutputPath);
                }
            });
        };

        _coordinator.ReplaySaved += replaySavedInfo =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_viewModel.NotificationsEnabled)
                {
                    _gameOverlayNotifier.ShowRecordingSaved(replaySavedInfo.TargetWindow, replaySavedInfo.ClipPath);
                }
            });
        };

        _foregroundWindowHook.ForegroundChanged += () => _ = _coordinator.TriggerDetectionAsync();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _coordinator.InitializeAsync();

        try
        {
            _foregroundWindowHook.Start();
            _logger.Info("Foreground window hook installed.");
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to install foreground window hook. Falling back to polling only.", exception);
        }

        if (_startHidden)
        {
            HideToTrayInternal();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _saveHotkeyManager.Attach(this);
        _toggleBufferHotkeyManager.Attach(this);
        ApplyHotkeyBindings();
    }

    private async void StartBuffering_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.StartManualCaptureAsync();
    }

    private async void StopBuffering_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.StopCaptureAsync("Stopped by user.");
        await _coordinator.TriggerDetectionAsync();
    }

    private async void SaveReplay_Click(object sender, RoutedEventArgs e)
    {
        await SaveReplayInternalAsync();
    }

    private async Task SaveReplayInternalAsync()
    {
        try
        {
            await _coordinator.SaveReplayAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Replay export failed.", exception);
            System.Windows.MessageBox.Show(this, exception.Message, "Buffero", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ToggleBufferingAsync(string stopReason)
    {
        if (_viewModel.IsCapturing)
        {
            await _coordinator.StopCaptureAsync(stopReason);
            await _coordinator.TriggerDetectionAsync();
            return;
        }

        await _coordinator.StartManualCaptureAsync();
        await _coordinator.TriggerDetectionAsync();
    }

    private async Task ToggleReplayBufferEnabledAsync()
    {
        try
        {
            await _viewModel.SetReplayBufferEnabledAsync(!_viewModel.ReplayBufferEnabled);
            await _coordinator.TriggerDetectionAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to toggle replay buffer.", exception);
        }
    }

    private async void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ApplySettingsAsync();
            _startupRegistrationService.Apply(_viewModel.StartWithWindows);
            _trayIcon.SetReplaySavedNotificationsEnabled(_viewModel.NotificationsEnabled);
            _gameOverlayNotifier.UpdateBufferingWidgetOpacity(_viewModel.BufferingWidgetOpacity);
            ApplyHotkeyBindings();
            await _coordinator.TriggerDetectionAsync();
            ApplyPersistedWindowSizeForCurrentMode();
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to apply settings.", exception);
            System.Windows.MessageBox.Show(this, exception.Message, "Buffero", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = _viewModel.SaveDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _viewModel.SaveDirectory = dialog.SelectedPath;
        }
    }

    private void OpenClipsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(_viewModel.SaveDirectory);
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(_paths.LogsDirectory);
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        HideToTrayInternal();
    }

    private void ToggleUiMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleUiMode();
        ApplyPersistedWindowSizeForCurrentMode();
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _ = ExitApplicationAsync();
    }

    private async Task ExitApplicationAsync()
    {
        _isExiting = true;
        await _coordinator.ShutdownAsync();
        _foregroundWindowHook.Dispose();
        _gameOverlayNotifier.Dispose();
        _trayIcon.Dispose();
        _saveHotkeyManager.Dispose();
        _toggleBufferHotkeyManager.Dispose();
        Close();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void HideToTrayInternal()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ApplyHotkeyBindings()
    {
        _saveHotkeyManager.UpdateBinding(_viewModel.BuildHotkeyBinding());
        _toggleBufferHotkeyManager.UpdateBinding(
            _viewModel.IsHotkeyBufferActivationMode
                ? _viewModel.BuildToggleHotkeyBinding()
                : null);
        _gameOverlayNotifier.UpdateToggleHotkeyLabel(_viewModel.ToggleHotkeyPreview);
    }

    private void ApplyPersistedWindowSizeForCurrentMode()
    {
        ApplyWindowSize(_viewModel.UiMode, _viewModel.GetAppliedWindowSize(_viewModel.UiMode));
    }

    private void ApplyWindowSize(UiMode uiMode, (double Width, double Height) size)
    {
        var (minWidth, minHeight) = AppSettings.GetMinimumWindowSize(uiMode);
        MinWidth = minWidth;
        MinHeight = minHeight;

        var (width, height) = size;
        Width = Math.Max(width, MinWidth);
        Height = Math.Max(height, MinHeight);
    }
}
