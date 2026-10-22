using System.IO;
using System.Windows;
using MenuBar.Services;
using MenuBar.ViewModels;

namespace MenuBar;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private MetricsPublisher? _metricsPublisher;
    private const string OpenRequestEventName = "Local\\Stats.OpenRequest";
    private readonly string _openRequestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Stats",
        "open-request.txt");
    private EventWaitHandle? _openRequestEvent;
    private RegisteredWaitHandle? _openRequestRegistration;
    private StatsSettingsWindow? _settingsWindow;
    private MainViewModel? _settingsViewModel;
    private ModulePopupWindow? _moduleWindow;
    private StatsAlertWindow? _alertWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        // A settings-only launch must not start a disabled suite component.
        if (BuildOpenRequest(e.Args) is "settings" or "suite" && DesktopSuiteIntegration.TryOpenManagedSettings())
        {
            Shutdown();
            return;
        }
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Stats.SingleInstance", out var isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            var request = BuildOpenRequest(e.Args);
            if (request is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_openRequestPath)!);
                    File.WriteAllText(_openRequestPath, request);
                    EventWaitHandle.OpenExisting(OpenRequestEventName).Set();
                }
                catch (Exception exception) when (
                    exception is WaitHandleCannotBeOpenedException or IOException or UnauthorizedAccessException)
                {
                    // The publisher may still be starting.
                }
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        // An exit request without a running instance must not start collection.
        if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown();
            return;
        }
        if (e.Args.Contains("--window", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new MainWindow();
            MainWindow.Show();
            return;
        }

        _metricsPublisher = new MetricsPublisher();
        _metricsPublisher.AlertRaised += notification => Dispatcher.BeginInvoke(() => ShowAlert(notification));
        _openRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenRequestEventName);
        _openRequestRegistration = ThreadPool.RegisterWaitForSingleObject(
            _openRequestEvent,
            (_, _) => Dispatcher.BeginInvoke(() => HandleOpenRequest()),
            null,
            Timeout.Infinite,
            false);
        var initialRequest = BuildOpenRequest(e.Args);
        if (initialRequest is not null)
        {
            HandleOpenRequest(initialRequest);
        }
    }

    private static string? BuildOpenRequest(IReadOnlyList<string> args) => SettingsOpenRequest.Parse(args);

    private void HandleOpenRequest()
    {
        try
        {
            if (File.Exists(_openRequestPath))
            {
                HandleOpenRequest(File.ReadAllText(_openRequestPath).Trim());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A dropped UI request does not affect telemetry publication.
        }
    }

    private void HandleOpenRequest(string request)
    {
        if (string.Equals(request, "exit", StringComparison.Ordinal))
        {
            Shutdown();
            return;
        }
        if (request.StartsWith("module:", StringComparison.Ordinal))
        {
            ShowModuleWindow(request["module:".Length..]);
            return;
        }

        if (string.Equals(request, "suite", StringComparison.Ordinal))
        {
            ShowSettingsWindow(openSuite: true);
        }
        else if (string.Equals(request, "settings", StringComparison.Ordinal))
        {
            ShowSettingsWindow();
        }
    }

    private void ShowModuleWindow(string metricId)
    {
        _moduleWindow?.Close();
        _moduleWindow = new ModulePopupWindow(metricId, _metricsPublisher?.LiveMetrics);
        MainWindow = _moduleWindow;
        _moduleWindow.SettingsRequested += (_, _) => ShowSettingsWindow();
        _moduleWindow.Closed += (_, _) => _moduleWindow = null;
        _moduleWindow.Show();
        _moduleWindow.Activate();
    }

    private void ShowSettingsWindow(bool openSuite = false)
    {
        _moduleWindow?.Close();
        _moduleWindow = null;
        if (DesktopSuiteIntegration.TryOpenManagedSettings()) return;
        if (_settingsWindow is { IsLoaded: true })
        {
            if (openSuite) _settingsWindow.OpenSuite();
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsViewModel = new MainViewModel(new SystemStatusService());
        _settingsWindow = new StatsSettingsWindow(_settingsViewModel);
        if (openSuite) _settingsWindow.OpenSuite();
        MainWindow = _settingsWindow;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsViewModel?.Dispose();
            _settingsViewModel = null;
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowAlert(StatsAlertNotification notification)
    {
        _alertWindow?.Close();
        _alertWindow = new StatsAlertWindow(notification);
        _alertWindow.Closed += (_, _) => _alertWindow = null;
        _alertWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _metricsPublisher?.Dispose();
        _openRequestRegistration?.Unregister(null);
        _openRequestEvent?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

}
