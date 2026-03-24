using Buffero.Core.Configuration;

namespace Buffero.App.Infrastructure;

public interface IReplayCaptureSession
{
    bool IsRunning { get; }

    CaptureBackend Backend { get; }

    event Action<int>? Exited;

    Task StopAsync();
}
