using System.Diagnostics;
using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public sealed class FfmpegCaptureSession : IReplayCaptureSession
{
    private readonly FileLogger _logger;
    private Process? _process;
    private bool _stopRequested;

    public FfmpegCaptureSession(FileLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunning => _process is { HasExited: false };

    public CaptureBackend Backend => CaptureBackend.Ffmpeg;

    public event Action<int>? Exited;

    public Task StartAsync(string ffmpegPath, IReadOnlyList<string> arguments)
    {
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException("ffmpeg executable was not found.", ffmpegPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _logger.Info($"ffmpeg: {args.Data}");
            }
        };

        _process.Exited += (_, _) =>
        {
            var exitCode = _process?.ExitCode ?? -1;
            if (!_stopRequested)
            {
                _logger.Warn($"Capture process exited unexpectedly with code {exitCode}.");
            }

            Exited?.Invoke(exitCode);
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start the ffmpeg capture process.");
        }

        _stopRequested = false;
        _process.BeginErrorReadLine();
        _logger.Info("Capture process started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        _stopRequested = true;

        try
        {
            await _process.StandardInput.WriteLineAsync("q");
            await _process.StandardInput.FlushAsync();
        }
        catch
        {
            // If stdin is already gone, the wait below will handle it.
        }

        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _process.Kill(entireProcessTree: true);
        }

        _logger.Info("Capture process stopped.");
    }
}
