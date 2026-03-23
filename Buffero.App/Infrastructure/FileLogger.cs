namespace Buffero.App.Infrastructure;

public sealed class FileLogger : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logFilePath;
    private bool _disposed;

    public FileLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        _logFilePath = Path.Combine(logsDirectory, $"buffero-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
    }

    public string LogFilePath => _logFilePath;

    public event Action<string>? LineLogged;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var finalMessage = exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", finalMessage);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        _ = WriteCoreAsync(line);
        LineLogged?.Invoke(line);
    }

    private async Task WriteCoreAsync(string line)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();

        try
        {
            await File.AppendAllTextAsync(_logFilePath, line + Environment.NewLine);
        }
        finally
        {
            _gate.Release();
        }
    }
}
