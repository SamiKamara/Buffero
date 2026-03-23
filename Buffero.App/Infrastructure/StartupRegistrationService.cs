using Microsoft.Win32;

namespace Buffero.App.Infrastructure;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Buffero";

    public void Apply(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open the Windows startup registry key.");

        if (!enabled)
        {
            if (runKey.GetValue(ValueName) is not null)
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return;
        }

        var executablePath = GetExecutablePath();
        runKey.SetValue(ValueName, $"\"{executablePath}\" --background");
    }

    private static string GetExecutablePath()
    {
        var appHostPath = Path.Combine(AppContext.BaseDirectory, "Buffero.App.exe");
        if (File.Exists(appHostPath))
        {
            return appHostPath;
        }

        return Environment.ProcessPath
               ?? throw new InvalidOperationException("Could not determine the Buffero executable path.");
    }
}
