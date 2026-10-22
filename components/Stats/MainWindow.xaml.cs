using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using MenuBar.Services;
using MenuBar.ViewModels;

namespace MenuBar;

public partial class MainWindow : Window
{
    private readonly AppBarService _appBarService = new();
    private readonly MainViewModel _viewModel = new(new SystemStatusService());
    private StatsSettingsWindow? _statsSettingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseRightButtonUp += (_, _) => ShowStatsSettings();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _appBarService.Attach(handle, Height);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _appBarService.Reposition();
        _viewModel.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _appBarService.Dispose();
    }

    private static void OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
            // Windows may not expose every settings URI on all editions.
        }
    }

    private void NetworkButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:network-status");

    private void VolumeButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:sound");

    private void PowerButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:powersleep");

    private void StatsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowStatsSettings();
    }

    private void ShowStatsSettings()
    {
        if (_statsSettingsWindow is { IsLoaded: true })
        {
            _statsSettingsWindow.Activate();
            return;
        }

        _statsSettingsWindow = new StatsSettingsWindow(_viewModel) { Owner = this };
        _statsSettingsWindow.Closed += (_, _) => _statsSettingsWindow = null;
        _statsSettingsWindow.Show();
    }

    private void ClockButton_Click(object sender, RoutedEventArgs e) => OpenSettings("ms-settings:dateandtime");

}
