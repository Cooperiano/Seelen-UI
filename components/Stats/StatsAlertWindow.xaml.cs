using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MenuBar.Services;

namespace MenuBar;

public partial class StatsAlertWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromSeconds(7) };

    public StatsAlertWindow(StatsAlertNotification notification)
    {
        InitializeComponent();
        DataContext = notification;
        Loaded += OnLoaded;
        Closed += (_, _) => _closeTimer.Stop();
        _closeTimer.Tick += (_, _) => Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right / dpi.DpiScaleX - ActualWidth - 18;
        Top = workArea.Top / dpi.DpiScaleY + 42 / dpi.DpiScaleY;
        _closeTimer.Start();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
