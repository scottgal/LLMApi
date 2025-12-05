using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;

namespace LLMockApiClient.Services;

public enum ActivityType
{
    SignalR,
    SSE,
    OpenAPI,
    Grpc,
    MockAPI,
    Server
}

public class ActivityIndicatorService
{
    public event EventHandler<ActivityType>? ActivityOccurred;

    public void TriggerActivity(ActivityType type)
    {
        ActivityOccurred?.Invoke(this, type);
    }

    public static Ellipse CreateIndicator(Color color, double size = 8)
    {
        var indicator = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        return indicator;
    }

    public static void BlinkIndicator(Ellipse indicator, Color color)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Create blink animation
            var colorAnimation = new ColorAnimation
            {
                From = color,
                To = Colors.Gray,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = false
            };

            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.3,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = false
            };

            var brush = new SolidColorBrush(color);
            indicator.Fill = brush;

            brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
            indicator.BeginAnimation(Ellipse.OpacityProperty, opacityAnimation);
        });
    }
}
