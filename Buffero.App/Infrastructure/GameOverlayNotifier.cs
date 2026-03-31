using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Buffero.Core.State;
using IOPath = System.IO.Path;

namespace Buffero.App.Infrastructure;

public sealed class GameOverlayNotifier : IDisposable
{
    private const double MessageOverlayWidth = 360;
    private const double MessageOverlayHeight = 96;
    private const double BufferingWidgetWidth = 184;
    private const double BufferingWidgetHeight = 60;
    private const double HorizontalPadding = 12;
    private const double BottomMargin = 48;
    private const double BufferingWidgetMargin = 18;
    private static readonly TimeSpan SavedWidgetDuration = TimeSpan.FromSeconds(2.1);

    private TransientOverlayWindow? _messageOverlayWindow;
    private BufferingStatusWindow? _bufferingStatusWindow;
    private ReplayCoordinatorSnapshot? _latestSnapshot;
    private CancellationTokenSource? _savedWidgetCts;

    public void ShowRecordingSaving(CaptureTargetWindow? targetWindow, string outputPath)
    {
        var overlayWindow = EnsureMessageOverlayWindow();
        overlayWindow.SetMessage("Saving replay", IOPath.GetFileName(outputPath));
        PositionMessageOverlay(overlayWindow, targetWindow);
        overlayWindow.ShowOverlay();
        CancelSavedWidgetOverride();
        ShowBufferingWidget(targetWindow, BufferingWidgetState.Saving);
    }

    public void ShowRecordingSaved(CaptureTargetWindow? targetWindow, string clipPath)
    {
        var overlayWindow = EnsureMessageOverlayWindow();
        overlayWindow.SetMessage("Recording saved", IOPath.GetFileName(clipPath));
        PositionMessageOverlay(overlayWindow, targetWindow);
        overlayWindow.ShowOverlay();
        ShowSavedWidgetState(targetWindow);
    }

    public void UpdateBufferingStatus(ReplayCoordinatorSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        var overlayWindow = EnsureBufferingStatusWindow();
        if (!snapshot.IsReplayBufferEnabled || !snapshot.IsCapturing)
        {
            CancelSavedWidgetOverride();
            overlayWindow.HideWidget();
            return;
        }

        PositionBufferingWidget(overlayWindow, snapshot.TargetWindow);
        if (_savedWidgetCts is null)
        {
            overlayWindow.SetState(MapSnapshotToBufferingState(snapshot));
        }

        overlayWindow.ShowWidget();
    }

    public void Dispose()
    {
        CancelSavedWidgetOverride();
        _messageOverlayWindow?.Close();
        _messageOverlayWindow = null;
        _bufferingStatusWindow?.Close();
        _bufferingStatusWindow = null;
    }

    private static void PositionMessageOverlay(Window overlayWindow, CaptureTargetWindow? targetWindow)
    {
        var area = ResolveMessageOverlayArea(targetWindow);
        var desiredLeft = area.Left + ((area.Width - MessageOverlayWidth) / 2d);
        var desiredTop = area.Top + Math.Max(24d, area.Height - MessageOverlayHeight - BottomMargin);

        var minLeft = SystemParameters.VirtualScreenLeft + HorizontalPadding;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - MessageOverlayWidth - HorizontalPadding;
        var minTop = SystemParameters.VirtualScreenTop + HorizontalPadding;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - MessageOverlayHeight - HorizontalPadding;

        overlayWindow.Left = Clamp(desiredLeft, minLeft, maxLeft);
        overlayWindow.Top = Clamp(desiredTop, minTop, maxTop);
    }

    private static MonitorBounds ResolveMessageOverlayArea(CaptureTargetWindow? targetWindow)
    {
        if (targetWindow is { HasUsableBounds: true })
        {
            return new MonitorBounds(targetWindow.Left, targetWindow.Top, targetWindow.Width, targetWindow.Height);
        }

        if (MonitorLocator.TryGetMonitorBounds(targetWindow, out var monitorBounds))
        {
            return monitorBounds;
        }

        return new MonitorBounds(
            (int)SystemParameters.WorkArea.Left,
            (int)SystemParameters.WorkArea.Top,
            (int)SystemParameters.WorkArea.Width,
            (int)SystemParameters.WorkArea.Height);
    }

    private static void PositionBufferingWidget(Window overlayWindow, CaptureTargetWindow? targetWindow)
    {
        var fallbackBounds = new MonitorBounds(
            (int)SystemParameters.WorkArea.Left,
            (int)SystemParameters.WorkArea.Top,
            (int)SystemParameters.WorkArea.Width,
            (int)SystemParameters.WorkArea.Height);
        var monitorBounds = MonitorLocator.TryGetMonitorWorkArea(targetWindow, out var resolvedBounds)
            ? resolvedBounds
            : fallbackBounds;

        var desiredLeft = monitorBounds.Left + monitorBounds.Width - BufferingWidgetWidth - BufferingWidgetMargin;
        var desiredTop = monitorBounds.Top + monitorBounds.Height - BufferingWidgetHeight - BufferingWidgetMargin;

        var minLeft = SystemParameters.VirtualScreenLeft + HorizontalPadding;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - BufferingWidgetWidth - HorizontalPadding;
        var minTop = SystemParameters.VirtualScreenTop + HorizontalPadding;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - BufferingWidgetHeight - HorizontalPadding;

        overlayWindow.Left = Clamp(desiredLeft, minLeft, maxLeft);
        overlayWindow.Top = Clamp(desiredTop, minTop, maxTop);
    }

    private TransientOverlayWindow EnsureMessageOverlayWindow()
    {
        if (_messageOverlayWindow is null)
        {
            _messageOverlayWindow = new TransientOverlayWindow();
        }

        return _messageOverlayWindow;
    }

    private BufferingStatusWindow EnsureBufferingStatusWindow()
    {
        if (_bufferingStatusWindow is null)
        {
            _bufferingStatusWindow = new BufferingStatusWindow();
        }

        return _bufferingStatusWindow;
    }

    private static double Clamp(double value, double minValue, double maxValue)
    {
        if (maxValue < minValue)
        {
            return minValue;
        }

        return Math.Clamp(value, minValue, maxValue);
    }

    private void ShowSavedWidgetState(CaptureTargetWindow? targetWindow)
    {
        CancelSavedWidgetOverride();

        var overlayWindow = EnsureBufferingStatusWindow();
        PositionBufferingWidget(overlayWindow, targetWindow ?? _latestSnapshot?.TargetWindow);
        overlayWindow.SetState(BufferingWidgetState.Saved);
        overlayWindow.ShowWidget();

        _savedWidgetCts = new CancellationTokenSource();
        _ = RestoreWidgetStateAfterDelayAsync(_savedWidgetCts.Token);
    }

    private async Task RestoreWidgetStateAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SavedWidgetDuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (_savedWidgetCts is not null && _savedWidgetCts.Token == cancellationToken)
        {
            _savedWidgetCts.Dispose();
            _savedWidgetCts = null;
        }

        if (_latestSnapshot is null)
        {
            return;
        }

        UpdateBufferingStatus(_latestSnapshot);
    }

    private void CancelSavedWidgetOverride()
    {
        _savedWidgetCts?.Cancel();
        _savedWidgetCts?.Dispose();
        _savedWidgetCts = null;
    }

    private void ShowBufferingWidget(CaptureTargetWindow? targetWindow, BufferingWidgetState state)
    {
        if (_latestSnapshot is not { IsReplayBufferEnabled: true, IsCapturing: true })
        {
            return;
        }

        var overlayWindow = EnsureBufferingStatusWindow();
        PositionBufferingWidget(overlayWindow, targetWindow ?? _latestSnapshot.TargetWindow);
        overlayWindow.SetState(state);
        overlayWindow.ShowWidget();
    }

    private static BufferingWidgetState MapSnapshotToBufferingState(ReplayCoordinatorSnapshot snapshot)
    {
        return snapshot.State == ReplayState.Exporting
            ? BufferingWidgetState.Saving
            : BufferingWidgetState.ReadyToSave;
    }

    private abstract class OverlayWindowBase : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;

        protected OverlayWindowBase()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetExtendedStyle(handle) | WsExNoActivate | WsExToolWindow | WsExTransparent;
            SetExtendedStyle(handle, extendedStyle);
        }

        private static int GetExtendedStyle(IntPtr handle)
        {
            return IntPtr.Size == 8
                ? unchecked((int)GetWindowLongPtr(handle, GwlExStyle).ToInt64())
                : GetWindowLong(handle, GwlExStyle);
        }

        private static void SetExtendedStyle(IntPtr handle, int value)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr(handle, GwlExStyle, new IntPtr(value));
            }
            else
            {
                SetWindowLong(handle, GwlExStyle, value);
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        protected static Border BuildChrome(
            FrameworkElement child,
            Thickness padding,
            CornerRadius cornerRadius,
            double borderThickness,
            byte backgroundAlpha = 232,
            byte borderAlpha = 255)
        {
            return new Border
            {
                IsHitTestVisible = false,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(backgroundAlpha, 10, 16, 20)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(borderAlpha, 60, 198, 124)),
                BorderThickness = new Thickness(borderThickness),
                CornerRadius = cornerRadius,
                Padding = padding,
                Child = child
            };
        }
    }

    private sealed class TransientOverlayWindow : OverlayWindowBase
    {
        private readonly TextBlock _titleText;
        private readonly TextBlock _subtitleText;
        private CancellationTokenSource? _displayCts;

        public TransientOverlayWindow()
        {
            Width = MessageOverlayWidth;
            Height = MessageOverlayHeight;
            Content = BuildContent(out _titleText, out _subtitleText);
        }

        public void SetMessage(string title, string subtitle)
        {
            _titleText.Text = title;
            _subtitleText.Text = subtitle;
        }

        public void ShowOverlay()
        {
            _displayCts?.Cancel();
            _displayCts?.Dispose();
            _displayCts = new CancellationTokenSource();

            BeginAnimation(OpacityProperty, null);
            Opacity = 0;

            if (!IsVisible)
            {
                Show();
            }

            StartFade(0, 1, 120, null);
            _ = HideAfterDelayAsync(_displayCts.Token);
        }

        protected override void OnClosed(EventArgs e)
        {
            _displayCts?.Cancel();
            _displayCts?.Dispose();
            _displayCts = null;
            base.OnClosed(e);
        }

        private async Task HideAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2.1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            StartFade(1, 0, 220, () =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Hide();
                }
            });
        }

        private void StartFade(double from, double to, int durationMs, Action? completed)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase()
            };

            if (completed is not null)
            {
                animation.Completed += (_, _) => completed();
            }

            BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static FrameworkElement BuildContent(out TextBlock titleText, out TextBlock subtitleText)
        {
            titleText = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            subtitleText = new TextBlock
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 214, 225, 233)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            stackPanel.Children.Add(titleText);
            stackPanel.Children.Add(subtitleText);

            return BuildChrome(stackPanel, new Thickness(24, 18, 24, 18), new CornerRadius(20), 2);
        }
    }

    private sealed class BufferingStatusWindow : OverlayWindowBase
    {
        private readonly System.Windows.Shapes.Ellipse _activityDot;
        private readonly ScaleTransform _activityDotScale;
        private readonly TextBlock _statusText;

        public BufferingStatusWindow()
        {
            Width = BufferingWidgetWidth;
            Height = BufferingWidgetHeight;
            Content = BuildContent(out _activityDot, out _activityDotScale, out _statusText);
        }

        public void ShowWidget()
        {
            if (IsVisible)
            {
                return;
            }

            StartPulse();
            Show();
        }

        public void HideWidget()
        {
            if (!IsVisible)
            {
                return;
            }

            StopPulse();
            Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopPulse();
            base.OnClosed(e);
        }

        public void SetState(BufferingWidgetState state)
        {
            var (label, dotColor) = state switch
            {
                BufferingWidgetState.Saving => ("Saving", System.Windows.Media.Color.FromArgb(255, 255, 196, 79)),
                BufferingWidgetState.Saved => ("Saved", System.Windows.Media.Color.FromArgb(255, 89, 199, 255)),
                _ => ("Ready to save", System.Windows.Media.Color.FromArgb(255, 60, 198, 124))
            };

            _statusText.Text = label;
            _activityDot.Fill = new SolidColorBrush(dotColor);
        }

        private void StartPulse()
        {
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.5,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var scaleAnimation = new DoubleAnimation
            {
                From = 0.92,
                To = 1.08,
                Duration = TimeSpan.FromMilliseconds(900),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            _activityDot.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
            _activityDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
            _activityDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        }

        private void StopPulse()
        {
            _activityDot.BeginAnimation(UIElement.OpacityProperty, null);
            _activityDotScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _activityDotScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _activityDot.Opacity = 1;
            _activityDotScale.ScaleX = 1;
            _activityDotScale.ScaleY = 1;
        }

        private static FrameworkElement BuildContent(
            out System.Windows.Shapes.Ellipse activityDot,
            out ScaleTransform activityDotScale,
            out TextBlock statusText)
        {
            var titleText = new TextBlock
            {
                Text = "Buffering",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Opacity = 0.82
            };

            statusText = new TextBlock
            {
                Text = "Ready to save",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            textStack.Children.Add(titleText);
            textStack.Children.Add(statusText);

            activityDotScale = new ScaleTransform(1, 1);
            activityDot = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 60, 198, 124)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                RenderTransform = activityDotScale,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };

            var panel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(activityDot);
            panel.Children.Add(textStack);

            return BuildChrome(panel, new Thickness(16, 12, 16, 12), new CornerRadius(16), 1.5, backgroundAlpha: 156, borderAlpha: 204);
        }
    }

    private enum BufferingWidgetState
    {
        ReadyToSave,
        Saving,
        Saved
    }
}
