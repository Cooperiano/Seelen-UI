using MenuBar;
using MenuBar.Models;
using MenuBar.Services;
using MenuBar.ViewModels;
using System.Windows;
using System.Windows.Controls;

internal static class DesktopSuiteWindowTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            StatsSettingsWindow? window = null;
            try
            {
                using var model = new MainViewModel(new NoSamplingService());
                window = new StatsSettingsWindow(model) { Opacity = 0, ShowActivated = false };
                window.Show();
                Check(Text(window, "ModuleTitle") == "Stats 概览", "legacy overview remains default");
                foreach (var (width, height) in new[] { (760, 560), (860, 650), (1100, 780) })
                {
                    window.Width = width; window.Height = height;
                    window.OpenSuite(); window.UpdateLayout();
                    Check(Text(window, "ModuleTitle") == "桌面套件", "suite navigates existing window");
                    Check(Element(window, "SuitePanel").Visibility == Visibility.Visible, "suite page visible");
                    foreach (var name in new[] { "MetricsList", "MonitoringPreview", "CollectionPanel", "RestoreDefaultsButton" })
                        Check(Element(window, name).Visibility == Visibility.Collapsed, "no unrelated metric controls: " + name);
                    Check(Element(window, "OpenTouchpadButton").ActualWidth > 0 && Element(window, "OpenMenuBarButton").ActualWidth > 0,
                        "launch actions have layout at supported sizes");
                    window.OpenSuite();
                    Check(Text(window, "ModuleTitle") == "桌面套件", "repeated navigation idempotent");
                    ((ListBox)window.FindName("ModuleList")).SelectedIndex = 1;
                    Check(Element(window, "SuitePanel").Visibility == Visibility.Collapsed &&
                        Element(window, "RestoreDefaultsButton").Visibility == Visibility.Visible, "return to Stats restores scoped controls");
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Suite WPF fixture timed out.");
        if (failure is not null) throw new InvalidOperationException("Suite WPF fixture failed.", failure);
        Console.WriteLine("Passed suite WPF navigation/repeated-open/scoped-control/layout fixtures at 760x560, 860x650, 1100x780; not visual/DPI acceptance.");
    }

    private static FrameworkElement Element(FrameworkElement window, string name) => (FrameworkElement)window.FindName(name);
    private static string Text(FrameworkElement window, string name) => ((TextBlock)window.FindName(name)).Text;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class NoSamplingService : ISystemStatusService
    {
        public void ConfigureMetrics(IReadOnlySet<string> enabledMetricIds) { }
        public SystemSnapshot Capture(nint ownWindowHandle) => throw new InvalidOperationException("Fixture must not start a collector.");
    }
}
