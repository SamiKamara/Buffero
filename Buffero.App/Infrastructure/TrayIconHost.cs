using System.Drawing;
using System.Windows.Forms;
using Buffero.Core.State;

namespace Buffero.App.Infrastructure;

public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleBufferItem;
    private string? _lastNotifiedClipPath;

    public TrayIconHost()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Buffero"
        };

        _toggleBufferItem = new ToolStripMenuItem("Start Buffering");
        _toggleBufferItem.Click += (_, _) => ToggleBufferRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Buffero", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_toggleBufferItem);
        menu.Items.Add("Save Replay Now", null, (_, _) => SaveReplayRequested?.Invoke());
        menu.Items.Add("Open Clips Folder", null, (_, _) => OpenClipsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? ToggleBufferRequested;

    public event Action? SaveReplayRequested;

    public event Action? OpenClipsRequested;

    public event Action? ExitRequested;

    public void Update(ReplayCoordinatorSnapshot snapshot)
    {
        _toggleBufferItem.Text = snapshot.IsCapturing ? "Stop Buffering" : "Start Buffering";

        _notifyIcon.Icon = snapshot.State switch
        {
            ReplayState.Capturing => SystemIcons.Information,
            ReplayState.Exporting => SystemIcons.Information,
            ReplayState.Recovering => SystemIcons.Warning,
            ReplayState.Faulted => SystemIcons.Error,
            _ => SystemIcons.Application
        };

        _notifyIcon.Text = snapshot.IsCapturing
            ? "Buffero: buffering"
            : $"Buffero: {snapshot.State}";

        if (!string.IsNullOrWhiteSpace(snapshot.LastSavedClipPath)
            && !string.Equals(snapshot.LastSavedClipPath, _lastNotifiedClipPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastNotifiedClipPath = snapshot.LastSavedClipPath;
            ShowSavedNotification(snapshot.LastSavedClipPath);
        }
    }

    public void ShowSavedNotification(string clipPath)
    {
        _notifyIcon.BalloonTipTitle = "Replay saved";
        _notifyIcon.BalloonTipText = Path.GetFileName(clipPath);
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
