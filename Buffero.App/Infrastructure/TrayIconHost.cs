using System.Drawing;
using System.Windows.Forms;
using Buffero.Core.State;

namespace Buffero.App.Infrastructure;

public sealed class TrayIconHost : IDisposable
{
    private readonly Icon? _appIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleBufferItem;
    private readonly ToolStripMenuItem _saveReplayItem;
    private string? _lastNotifiedClipPath;
    private bool _replaySavedNotificationsEnabled = true;

    public TrayIconHost()
    {
        _appIcon = TryLoadAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Visible = true,
            Text = "Buffero"
        };

        _toggleBufferItem = new ToolStripMenuItem("Start Buffering");
        _toggleBufferItem.Click += (_, _) => ToggleBufferRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Buffero", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_toggleBufferItem);
        _saveReplayItem = new ToolStripMenuItem("Save Replay Now");
        _saveReplayItem.Click += (_, _) => SaveReplayRequested?.Invoke();
        menu.Items.Add(_saveReplayItem);
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

    public void SetReplaySavedNotificationsEnabled(bool isEnabled)
    {
        _replaySavedNotificationsEnabled = isEnabled;
    }

    public void Update(ReplayCoordinatorSnapshot snapshot)
    {
        _toggleBufferItem.Text = snapshot.IsReplayBufferEnabled
            ? "Disable Replay Buffer"
            : "Enable Replay Buffer";
        _saveReplayItem.Enabled = snapshot.IsReplayBufferEnabled;

        _notifyIcon.Icon = !snapshot.IsReplayBufferEnabled
            ? _appIcon ?? SystemIcons.Application
            : snapshot.State switch
        {
            ReplayState.Capturing => SystemIcons.Information,
            ReplayState.Exporting => SystemIcons.Information,
            ReplayState.Recovering => SystemIcons.Warning,
            ReplayState.Faulted => SystemIcons.Error,
            _ => _appIcon ?? SystemIcons.Application
        };

        _notifyIcon.Text = !snapshot.IsReplayBufferEnabled
            ? "Buffero: disabled"
            : snapshot.IsCapturing
                ? "Buffero: buffering"
                : $"Buffero: {snapshot.State}";

        if (!snapshot.IsReplayBufferEnabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastSavedClipPath)
            && !string.Equals(snapshot.LastSavedClipPath, _lastNotifiedClipPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastNotifiedClipPath = snapshot.LastSavedClipPath;
            if (_replaySavedNotificationsEnabled)
            {
                ShowSavedNotification(snapshot.LastSavedClipPath);
            }
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
        _appIcon?.Dispose();
    }

    private static Icon? TryLoadAppIcon()
    {
        try
        {
            var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("Assets/buffero.ico", UriKind.Relative));
            if (resourceInfo is null)
            {
                return null;
            }

            using var stream = resourceInfo.Stream;
            return new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}
