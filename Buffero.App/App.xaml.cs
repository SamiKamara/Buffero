using Buffero.App.Infrastructure;
using Buffero.Core.Configuration;
using System.Diagnostics;

namespace Buffero.App;

public partial class App : System.Windows.Application
{
    private FileLogger? _logger;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\Buffero.App", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Buffero is already running. Check the system tray for the existing instance.",
                "Buffero",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;

        var locator = new FfmpegLocator();
        var ffmpegPath = locator.FindBestPath();
        var paths = BufferoPaths.Create();
        _logger = new FileLogger(paths.LogsDirectory);
        _logger.Info("Buffero starting up.");
        _logger.Info($"Executable path: {Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "(unknown)"}");
        var settingsStore = new SettingsStore();
        DisplayResolutionProbe.TryGetPrimaryScreenResolution(out var primaryScreenWidth, out var primaryScreenHeight);
        var settings = settingsStore.LoadOrCreate(
            paths.SettingsFilePath,
            () => AppSettings.CreateDefault(ffmpegPath),
            ffmpegPath,
            primaryScreenWidth,
            primaryScreenHeight);
        var estimateResolution = QualityEstimateResolutionProbe.Resolve(settings);
        settings.Normalize(ffmpegPath, estimateResolution.Width, estimateResolution.Height);
        settingsStore.Save(paths.SettingsFilePath, settings, estimateResolution.Width, estimateResolution.Height);

        try
        {
            var gameLibraryScanner = new GameLibraryScanner(_logger);
            var addedExecutables = gameLibraryScanner.ScanAndMerge(settings);
            if (addedExecutables.Count > 0)
            {
                var mergedEstimateResolution = QualityEstimateResolutionProbe.Resolve(settings);
                settingsStore.Save(
                    paths.SettingsFilePath,
                    settings,
                    mergedEstimateResolution.Width,
                    mergedEstimateResolution.Height);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to scan local game libraries.", exception);
        }

        paths.EnsureDirectories(settings.SaveDirectory);
        CleanupStaleTempSessions(paths.TempSessionsDirectory);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        var startupRegistrationService = new StartupRegistrationService();
        try
        {
            startupRegistrationService.Apply(settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to apply Windows startup registration.", exception);
        }

        var startHidden = e.Args.Any(arg => string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase));

        var mainWindow = new MainWindow(settings, settingsStore, paths, _logger, locator, startupRegistrationService, startHidden);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _logger?.Info("Buffero shutting down.");
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        _logger?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled dispatcher exception.", e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.Error("Unhandled domain exception.", exception);
        }
        else
        {
            _logger?.Error($"Unhandled domain exception. IsTerminating={e.IsTerminating}");
        }
    }

    private static void CleanupStaleTempSessions(string tempSessionsDirectory)
    {
        Directory.CreateDirectory(tempSessionsDirectory);

        foreach (var directory in Directory.GetDirectories(tempSessionsDirectory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
