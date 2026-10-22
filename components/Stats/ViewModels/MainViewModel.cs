using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MenuBar.Models;
using MenuBar.Services;
using Monitoring.Contracts;

namespace MenuBar.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISystemStatusService _statusService;
    private readonly StatsSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _timer;
    private string _activeApplication = "桌面";
    private string _activeWindowTitle = string.Empty;
    private string _networkLabel = "检查中";
    private string _volumeLabel = "--";
    private string _volumeGlyph = "\uE767";
    private string _powerLabel = "--";
    private string _dateLabel = string.Empty;
    private string _timeLabel = string.Empty;
    private bool _isRefreshing;
    private List<StatsMetricPreference> _metricPreferences;
    private SystemSnapshot? _lastSnapshot;

    public MainViewModel(ISystemStatusService statusService)
    {
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _metricPreferences = _settingsStore.Load();
        ConfigureMetricReaders();
        RebuildVisibleStats();
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StatsMetricItem> VisibleStats { get; } = [];

    public string ActiveApplication { get => _activeApplication; private set => SetField(ref _activeApplication, value); }
    public string ActiveWindowTitle { get => _activeWindowTitle; private set => SetField(ref _activeWindowTitle, value); }
    public string NetworkLabel { get => _networkLabel; private set => SetField(ref _networkLabel, value); }
    public string VolumeLabel { get => _volumeLabel; private set => SetField(ref _volumeLabel, value); }
    public string VolumeGlyph { get => _volumeGlyph; private set => SetField(ref _volumeGlyph, value); }
    public string PowerLabel { get => _powerLabel; private set => SetField(ref _powerLabel, value); }
    public string DateLabel { get => _dateLabel; private set => SetField(ref _dateLabel, value); }
    public string TimeLabel { get => _timeLabel; private set => SetField(ref _timeLabel, value); }

    public IReadOnlyList<StatsMetricPreference> GetMetricPreferences() =>
        _metricPreferences.Select(item => new StatsMetricPreference(item.Id, item.IsEnabled, item.DisplayStyle, item.Accent)).ToList();

    public StatsApplicationSettings GetApplicationSettings() => _settingsStore.LoadSettings();

    public void ApplyApplicationSettings(StatsApplicationSettings settings)
    {
        _settingsStore.SaveSettings(settings);
        _metricPreferences = settings.Metrics.ToList();
        ConfigureMetricReaders();
        RebuildVisibleStats();
    }

    public void ApplyMetricPreferences(IEnumerable<StatsMetricPreference> preferences)
    {
        _metricPreferences = preferences
            .Select(item => new StatsMetricPreference(item.Id, item.IsEnabled, item.DisplayStyle, item.Accent))
            .ToList();
        _settingsStore.Save(_metricPreferences);
        ConfigureMetricReaders();
        RebuildVisibleStats();
    }

    public void RestoreDefaultMetricPreferences() =>
        ApplyMetricPreferences(StatsMetricCatalog.Defaults());

    public void Start()
    {
        _ = RefreshAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        var mainWindow = Application.Current?.MainWindow;
        var ownHandle = mainWindow is null ? nint.Zero : new WindowInteropHelper(mainWindow).Handle;
        try
        {
            var snapshot = await Task.Run(() => _statusService.Capture(ownHandle));
            Apply(snapshot);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void Apply(SystemSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        ActiveApplication = snapshot.ActiveApplication;
        ActiveWindowTitle = snapshot.ActiveWindowTitle;
        NetworkLabel = snapshot.IsNetworkAvailable ? "在线" : "离线";
        VolumeLabel = snapshot.VolumePercent is null ? "--" : $"{snapshot.VolumePercent}%";
        VolumeGlyph = snapshot.IsMuted ? "\uE74F" : "\uE767";
        PowerLabel = snapshot.BatteryPercent is null
            ? "接通电源"
            : snapshot.IsCharging
                ? $"{snapshot.BatteryPercent}% ⚡"
                : $"{snapshot.BatteryPercent}%";
        UpdateVisibleStats(snapshot);
        DateLabel = snapshot.Now.ToString("M月d日");
        TimeLabel = snapshot.Now.ToString("HH:mm");
    }

    private void RebuildVisibleStats()
    {
        VisibleStats.Clear();
        foreach (var preference in _metricPreferences.Where(item => item.IsEnabled))
        {
            var definition = StatsMetricCatalog.Find(preference.Id);
            if (definition is not null)
            {
                VisibleStats.Add(new StatsMetricItem(definition, preference.Accent));
            }
        }

        if (_lastSnapshot is not null)
        {
            UpdateVisibleStats(_lastSnapshot);
        }
    }

    private void ConfigureMetricReaders()
    {
        var enabled = _metricPreferences
            .Where(item => item.IsEnabled)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        _statusService.ConfigureMetrics(enabled);
    }

    private void UpdateVisibleStats(SystemSnapshot snapshot)
    {
        foreach (var metric in VisibleStats)
        {
            switch (metric.Id)
            {
                case "cpu":
                    metric.Value = MetricFormatter.Percent(snapshot.CpuPercent);
                    metric.Tooltip = MetricFormatter.PercentTooltip("CPU", snapshot.CpuPercent);
                    break;
                case "gpu":
                    metric.Value = MetricFormatter.Percent(snapshot.GpuPercent);
                    metric.Tooltip = MetricFormatter.PercentTooltip("GPU", snapshot.GpuPercent);
                    break;
                case "ram":
                    metric.Value = MetricFormatter.Percent(snapshot.MemoryPercent);
                    metric.Tooltip = MetricFormatter.PercentTooltip("RAM", snapshot.MemoryPercent);
                    break;
                case "power":
                    metric.Value = MetricFormatter.Temperature(snapshot.CpuTemperatureCelsius);
                    metric.Tooltip = snapshot.CpuTemperatureCelsius is null
                        ? "当前设备未提供可读温度传感器"
                        : $"Sensor · CPU 温度 {snapshot.CpuTemperatureCelsius:0.0} °C";
                    break;
                case "cpu_temp":
                    metric.Value = MetricFormatter.Temperature(snapshot.CpuTemperatureCelsius);
                    metric.Tooltip = snapshot.CpuTemperatureCelsius is null
                        ? "CPU 温区数据暂不可用"
                        : $"ACPI CPU 温区 {snapshot.CpuTemperatureCelsius:0.0} °C";
                    break;
                case "cpu_freq":
                    metric.Value = MetricFormatter.Frequency(snapshot.CpuFrequencyMhz);
                    metric.Tooltip = snapshot.CpuFrequencyMhz is null
                        ? "CPU 频率暂不可用"
                        : $"CPU 当前频率 {snapshot.CpuFrequencyMhz:0} MHz";
                    break;
                case "gpu_temp":
                    metric.Value = MetricFormatter.Temperature(snapshot.GpuTemperatureCelsius);
                    metric.Tooltip = snapshot.GpuTemperatureCelsius is null
                        ? "GPU 温度暂不可用"
                        : $"GPU 温度 {snapshot.GpuTemperatureCelsius:0} °C";
                    break;
                case "gpu_power":
                    metric.Value = MetricFormatter.Power(snapshot.GpuPowerWatts);
                    metric.Tooltip = snapshot.GpuPowerWatts is null
                        ? "GPU 功耗暂不可用"
                        : $"GPU 当前功耗 {snapshot.GpuPowerWatts:0.0} W";
                    break;
                case "gpu_memory":
                    metric.Value = MetricFormatter.MemoryMb(snapshot.GpuMemoryUsedMb);
                    metric.Tooltip = snapshot.GpuMemoryUsedMb is null
                        ? "显存数据暂不可用"
                        : $"显存 {snapshot.GpuMemoryUsedMb:0} / {snapshot.GpuMemoryTotalMb:0} MB";
                    break;
                case MetricIds.Disk:
                    metric.Value = MetricFormatter.Percent(snapshot.DiskUsedPercent);
                    metric.Tooltip = MetricFormatter.PercentTooltip("系统磁盘", snapshot.DiskUsedPercent);
                    break;
                case MetricIds.NetworkDownload:
                    metric.Value = MetricFormatter.Rate(snapshot.NetworkDownloadBytesPerSecond);
                    metric.Tooltip = $"下载 {MetricFormatter.Rate(snapshot.NetworkDownloadBytesPerSecond)}/s";
                    break;
                case MetricIds.NetworkUpload:
                    metric.Value = MetricFormatter.Rate(snapshot.NetworkUploadBytesPerSecond);
                    metric.Tooltip = $"上传 {MetricFormatter.Rate(snapshot.NetworkUploadBytesPerSecond)}/s";
                    break;
                case MetricIds.Battery:
                    metric.Value = snapshot.BatteryPercent is null ? "--" : $"{snapshot.BatteryPercent}%";
                    metric.Tooltip = snapshot.BatteryPercent is null ? "电池数据暂不可用" : $"剩余电量 {snapshot.BatteryPercent}%";
                    break;
                case MetricIds.Bluetooth:
                    metric.Value = snapshot.BluetoothConnectedDeviceCount is null ? "--" : $"{snapshot.BluetoothConnectedDeviceCount:0}";
                    metric.Tooltip = snapshot.BluetoothConnectedDeviceCount is null
                        ? "蓝牙设备数据暂不可用"
                        : $"已连接蓝牙设备 {snapshot.BluetoothConnectedDeviceCount:0} 个";
                    break;
            }
        }
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
