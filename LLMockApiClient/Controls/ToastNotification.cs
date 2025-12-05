using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LLMockApiClient.Controls;

/// <summary>
/// Non-intrusive toast notification for user feedback
/// </summary>
public class ToastNotification
{
    public enum ToastType
    {
        Success,
        Info,
        Warning,
        Error
    }

    private static Border? _toastContainer;
    private static TextBlock? _toastMessage;
    private static DispatcherTimer? _hideTimer;

    public static void Initialize(Panel rootContainer)
    {
        // Create toast UI (hidden by default)
        _toastContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)), // Green
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 400,
            Opacity = 0,
            IsHitTestVisible = false
        };

        _toastMessage = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold
        };

        _toastContainer.Child = _toastMessage;

        // Add to root (on top of everything)
        Panel.SetZIndex(_toastContainer, 9999);
        rootContainer.Children.Add(_toastContainer);

        // Setup auto-hide timer
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public static void Show(string message, ToastType type = ToastType.Success, int durationSeconds = 3)
    {
        if (_toastContainer == null || _toastMessage == null || _hideTimer == null)
            return;

        // Set color based on type
        _toastContainer.Background = type switch
        {
            ToastType.Success => new SolidColorBrush(Color.FromRgb(46, 125, 50)), // Green
            ToastType.Info => new SolidColorBrush(Color.FromRgb(2, 136, 209)), // Blue
            ToastType.Warning => new SolidColorBrush(Color.FromRgb(245, 124, 0)), // Orange
            ToastType.Error => new SolidColorBrush(Color.FromRgb(211, 47, 47)), // Red
            _ => new SolidColorBrush(Color.FromRgb(46, 125, 50))
        };

        // Set icon prefix based on type
        var icon = type switch
        {
            ToastType.Success => "✅ ",
            ToastType.Info => "ℹ️ ",
            ToastType.Warning => "⚠️ ",
            ToastType.Error => "❌ ",
            _ => ""
        };

        _toastMessage.Text = icon + message;

        // Show with animation
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var slideIn = new ThicknessAnimation
        {
            From = new Thickness(0, 0, 24, -50),
            To = new Thickness(0, 0, 24, 24),
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _toastContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _toastContainer.BeginAnimation(FrameworkElement.MarginProperty, slideIn);

        // Auto-hide after duration
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
        _hideTimer.Start();
    }

    private static void Hide()
    {
        if (_toastContainer == null)
            return;

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var slideOut = new ThicknessAnimation
        {
            From = new Thickness(0, 0, 24, 24),
            To = new Thickness(0, 0, 24, -50),
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        _toastContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        _toastContainer.BeginAnimation(FrameworkElement.MarginProperty, slideOut);
    }
}
