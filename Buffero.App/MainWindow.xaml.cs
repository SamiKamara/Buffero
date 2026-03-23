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
    private readonly HotkeyManager _hotkeyManager;
    private readonly GameOverlayNotifier _gameOverlayNotifier;
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
        _hotkeyManager = new HotkeyManager();
        _gameOverlayNotifier = new GameOverlayNotifier();

        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;

        _trayIcon.OpenRequested += ShowFromTray;
        _trayIcon.SaveReplayRequested += async () => await Dispatcher.InvokeAsync(async () => await SaveReplayInternalAsync());
        _trayIcon.ToggleBufferRequested += async () => await Dispatcher.InvokeAsync(async () => await ToggleBufferingAsync());
        _trayIcon.OpenClipsRequested += () => Dispatcher.Invoke(() => OpenFolder(_viewModel.SaveDirectory));
        _trayIcon.ExitRequested += async () => await Dispatcher.InvokeAsync(async () => await ExitApplicationAsync());

        _hotkeyManager.SaveReplayPressed += async () =>
        {
            _logger.Info("Global hotkey pressed.");
            await Dispatcher.InvokeAsync(async () => await SaveReplayInternalAsync());
        };

        _hotkeyManager.RegistrationChanged += (isRegistered, message) =>
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

        _coordinator.SnapshotChanged += snapshot =>
        {
            Dispatcher.Invoke(() =>
            {
                _trayIcon.Update(snapshot);
            });
        };

        _coordinator.ReplaySaved += replaySavedInfo =>
        {
            Dispatcher.Invoke(() =>
            {
                _gameOverlayNotifier.ShowRecordingSaved(replaySavedInfo.TargetWindow, replaySavedInfo.ClipPath);
            });
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _coordinator.InitializeAsync();

        if (_startHidden)
        {
            HideToTrayInternal();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hotkeyManager.Attach(this);
        _hotkeyManager.UpdateBinding(_viewModel.BuildHotkeyBinding());
    }

    private async void StartBuffering_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.StartManualCaptureAsync();
    }

    private async void StopBuffering_Click(object sender, RoutedEventArgs e)
    {
        await _coordinator.StopCaptureAsync("Stopped by user.");
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

    private async Task ToggleBufferingAsync()
    {
        if (_viewModel.IsCapturing)
        {
            await _coordinator.StopCaptureAsync("Stopped from tray.");
            return;
        }

        await _coordinator.StartManualCaptureAsync();
    }

    private async void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ApplySettingsAsync();
            _startupRegistrationService.Apply(_viewModel.StartWithWindows);
            _hotkeyManager.UpdateBinding(_viewModel.BuildHotkeyBinding());
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
        _gameOverlayNotifier.Dispose();
        _trayIcon.Dispose();
        _hotkeyManager.Dispose();
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
}
