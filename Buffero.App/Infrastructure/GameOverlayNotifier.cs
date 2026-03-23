using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Buffero.App.Infrastructure;

public sealed class GameOverlayNotifier : IDisposable
{
    private const double OverlayWidth = 360;
    private const double OverlayHeight = 96;
    private const double HorizontalPadding = 12;
    private const double BottomMargin = 48;

    private RecordingSavedOverlayWindow? _overlayWindow;

    public void ShowRecordingSaved(CaptureTargetWindow? targetWindow, string clipPath)
    {
        if (targetWindow is null || !targetWindow.HasUsableBounds)
        {
            return;
        }

        var overlayWindow = EnsureOverlayWindow();
        overlayWindow.SetMessage("Recording saved", Path.GetFileName(clipPath));
        PositionOverlay(overlayWindow, targetWindow);
        overlayWindow.ShowOverlay();
    }

    public void Dispose()
    {
        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    private static void PositionOverlay(Window overlayWindow, CaptureTargetWindow targetWindow)
    {
        var desiredLeft = targetWindow.Left + ((targetWindow.Width - OverlayWidth) / 2d);
        var desiredTop = targetWindow.Top + Math.Max(24d, targetWindow.Height - OverlayHeight - BottomMargin);

        var minLeft = SystemParameters.VirtualScreenLeft + HorizontalPadding;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - OverlayWidth - HorizontalPadding;
        var minTop = SystemParameters.VirtualScreenTop + HorizontalPadding;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - OverlayHeight - HorizontalPadding;

        overlayWindow.Left = Clamp(desiredLeft, minLeft, maxLeft);
        overlayWindow.Top = Clamp(desiredTop, minTop, maxTop);
    }

    private RecordingSavedOverlayWindow EnsureOverlayWindow()
    {
        if (_overlayWindow is null)
        {
            _overlayWindow = new RecordingSavedOverlayWindow();
        }

        return _overlayWindow;
    }

    private static double Clamp(double value, double minValue, double maxValue)
    {
        if (maxValue < minValue)
        {
            return minValue;
        }

        return Math.Clamp(value, minValue, maxValue);
    }

    private sealed class RecordingSavedOverlayWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;

        private readonly TextBlock _titleText;
        private readonly TextBlock _subtitleText;
        private CancellationTokenSource? _displayCts;

        public RecordingSavedOverlayWindow()
        {
            Width = OverlayWidth;
            Height = OverlayHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Content = BuildContent(out _titleText, out _subtitleText);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetExtendedStyle(handle) | WsExNoActivate | WsExToolWindow | WsExTransparent;
            SetExtendedStyle(handle, extendedStyle);
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

            return new Border
            {
                IsHitTestVisible = false,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(232, 10, 16, 20)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 60, 198, 124)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(24, 18, 24, 18),
                Child = stackPanel
            };
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
    }
}
