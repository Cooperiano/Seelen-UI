using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MenuBar.ViewModels;
using MenuBar.Services;
using Monitoring.Contracts;

namespace MenuBar;

public partial class StatsSettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly List<MetricSettingRow> _rows = [];
    private readonly IReadOnlyList<ModuleNavigationItem> _modules =
    [
        new("suite", "桌面套件", "\uE8FC", "桌面套件", "统一入口 · 菜单栏、系统监控与触控板独立运行。"),
        new("overview", "概览", "\uE80F", "Stats 概览", "查看当前组合，并从常用配置开始调整。"),
        new("all", "全部模块", "\uE8FD", "菜单栏", "选择显示项，并调整它们从左到右的顺序。"),
        new("cpu", "CPU", "\uE950", "CPU", "处理器负载、温度与当前频率。"),
        new("gpu", "GPU", "\uE7F8", "GPU", "图形处理器负载、显存、温度与功耗。"),
        new("memory", "内存", "\uE8B7", "内存", "物理内存占用与压力。"),
        new("disk", "磁盘", "\uE7F0", "磁盘", "系统卷容量与占用情况。"),
        new("network", "网络", "\uE701", "网络", "实时下载和上传速率。"),
        new("battery", "电池", "\uE850", "电池", "剩余电量与实时功耗。"),
        new("bluetooth", "蓝牙", "\uE702", "蓝牙", "已连接、已配对和已记忆设备。"),
        new("sensors", "传感器", "\uE9D9", "传感器", "Temperature、Voltage、Current、Power、Energy 与风扇。"),
        new("alerts", "通知", "\uE7E7", "阈值通知", "持续超过阈值后提醒；默认全部关闭。")
    ];
    private readonly List<AlertSettingRow> _alertRows = [];
    private int _refreshIntervalMilliseconds = 1000;
    private int _historySeconds = 60;
    private bool _isLoading;
    private bool _computerInfoLoaded;

    public StatsSettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        ComputerNameText.Text = Environment.MachineName;
        Loaded += StatsSettingsWindow_Loaded;
        _isLoading = true;
        ModuleList.ItemsSource = _modules;
        LoadSettings(_viewModel.GetApplicationSettings());
        ModuleList.SelectedIndex = 1; // Preserve --settings opening the Stats overview.
        _isLoading = false;
        RefreshModule();
        Activated += (_, _) => RefreshSuiteAvailability();
    }

    public void OpenSuite() => Navigate("suite");

    private void Navigate(string id) => ModuleList.SelectedItem = _modules.First(item => item.Id == id);

    private void RefreshSuiteAvailability()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var menu = DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.MenuBar, AppContext.BaseDirectory, local);
        var touchpad = DesktopSuiteIntegration.IsAvailable(DesktopSuiteModule.Touchpad, AppContext.BaseDirectory, local);
        OpenMenuBarButton.IsEnabled = menu;
        OpenTouchpadButton.IsEnabled = touchpad;
        MenuBarAvailability.Text = menu ? "入口可用 · 按需启动" : "菜单栏入口不可用";
        TouchpadAvailability.Text = touchpad ? "系统注册入口可用" : "触控板入口不可用";
    }

    private void OpenMenuBar_Click(object sender, RoutedEventArgs e) => OpenSuiteModule(DesktopSuiteModule.MenuBar);
    private void OpenTouchpad_Click(object sender, RoutedEventArgs e) => OpenSuiteModule(DesktopSuiteModule.Touchpad);
    private void OpenMonitoring_Click(object sender, RoutedEventArgs e) => Navigate("overview");
    private void OpenDisplayItems_Click(object sender, RoutedEventArgs e) => Navigate("all");

    private void OpenSuiteModule(DesktopSuiteModule module)
    {
        try
        {
            DesktopSuiteIntegration.Open(module);
            SuiteActionStatus.Text = "已发送打开请求；请在对应窗口继续操作。";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SuiteActionStatus.Text = "打开失败：" + exception.Message;
        }
        RefreshSuiteAvailability();
    }

    private async void StatsSettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_computerInfoLoaded)
        {
            return;
        }

        _computerInfoLoaded = true;
        var info = await Task.Run(ComputerInfoService.Capture);
        if (!IsLoaded)
        {
            return;
        }

        ComputerNameText.Text = info.Name;
        ComputerModelText.Text = info.Model;
        ComputerOsText.Text = info.OperatingSystem;
        ComputerProcessorText.Text = info.Processor;
        ComputerMemoryText.Text = info.Memory;
        ComputerGraphicsText.Text = info.Graphics;
        ComputerUptimeText.Text = info.Uptime;
    }

    private void LoadSettings(StatsApplicationSettings settings)
    {
        _rows.Clear();
        foreach (var preference in settings.Metrics)
        {
            var definition = StatsMetricCatalog.Find(preference.Id);
            if (definition is not null)
            {
                _rows.Add(new MetricSettingRow(
                    definition,
                    ModuleFor(definition.Id),
                    preference.IsEnabled,
                    preference.DisplayStyle,
                    preference.Accent));
            }
        }

        _refreshIntervalMilliseconds = settings.RefreshIntervalMilliseconds;
        _historySeconds = settings.HistorySeconds;
        RefreshIntervalButton.Content = RefreshIntervalLabel(_refreshIntervalMilliseconds);
        HistoryButton.Content = HistoryLabel(_historySeconds);
        LowPowerModeCheck.IsChecked = settings.LowPowerMode;
        _alertRows.Clear();
        foreach (var rule in settings.Alerts ?? StatsAlertCatalog.Defaults)
        {
            var definition = StatsMetricCatalog.Find(rule.MetricId);
            _alertRows.Add(new AlertSettingRow(
                rule.MetricId,
                definition?.Description ?? rule.MetricId,
                rule.IsEnabled,
                rule.Threshold,
                rule.Comparison,
                rule.SustainSeconds));
        }
    }

    private void ModuleList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshModule();

    private void RefreshModule()
    {
        if (ModuleList.SelectedItem is not ModuleNavigationItem module)
        {
            return;
        }

        ModuleTitle.Text = module.Header;
        ModuleDescription.Text = module.Description;
        var showingAlerts = module.Id == "alerts";
        var showingOverview = module.Id == "overview";
        var showingSuite = module.Id == "suite";
        SuitePanel.Visibility = showingSuite ? Visibility.Visible : Visibility.Collapsed;
        MonitoringPreview.Visibility = showingSuite ? Visibility.Collapsed : Visibility.Visible;
        CollectionPanel.Visibility = showingSuite ? Visibility.Collapsed : Visibility.Visible;
        CollectionSectionLabel.Visibility = showingSuite ? Visibility.Collapsed : Visibility.Visible;
        RestoreDefaultsButton.Visibility = showingSuite ? Visibility.Collapsed : Visibility.Visible;
        SaveHint.Text = showingSuite ? "各模块独立运行，保留原有配置与自启设置" : "更改立即生效，无需重启";
        OverviewPanel.Visibility = showingOverview ? Visibility.Visible : Visibility.Collapsed;
        MetricsSectionLabel.Visibility = showingOverview || showingAlerts || showingSuite ? Visibility.Collapsed : Visibility.Visible;
        MetricsList.Visibility = showingOverview || showingAlerts || showingSuite ? Visibility.Collapsed : Visibility.Visible;
        AlertsPanel.Visibility = showingAlerts ? Visibility.Visible : Visibility.Collapsed;
        AlertsList.ItemsSource = _alertRows;
        MetricsList.ItemsSource = module.Id switch
        {
            "all" => _rows.ToList(),
            "sensors" => _rows.Where(row => row.Id is
                MetricIds.Power or MetricIds.CpuTemperature or MetricIds.CpuFrequency or
                MetricIds.GpuTemperature or MetricIds.GpuPower).ToList(),
            _ => _rows.Where(row => row.ModuleId == module.Id).ToList()
        };
        if (showingOverview)
        {
            OverviewItems.ItemsSource = _rows.Select(row => new OverviewMetric(
                row.Title,
                row.Description,
                row.Accent,
                SampleFor(row.Id),
                row.IsEnabled ? "已显示" : "已隐藏",
                row.IsEnabled ? "#FF31835C" : "#FF858C95")).ToList();
        }
        RefreshPreview();
        if (showingSuite) RefreshSuiteAvailability();
    }

    private void RefreshPreview()
    {
        var enabled = _rows.Where(row => row.IsEnabled).ToList();
        PreviewItems.ItemsSource = enabled
            .Select(row => new PreviewMetric(row.Title, row.Accent, SampleFor(row.Id)))
            .ToList();
        EnabledSummary.Text = $"{enabled.Count} / {_rows.Count} 已显示";
        OverviewEnabledText.Text = $"{enabled.Count} / {_rows.Count}";
        OverviewRefreshText.Text = RefreshIntervalLabel(_refreshIntervalMilliseconds);
        OverviewHistoryText.Text = HistoryLabel(_historySeconds);
        OverviewPowerText.Text = LowPowerModeCheck.IsChecked == false ? "完整采集" : "低功耗";
    }

    private void MetricToggle_Click(object sender, RoutedEventArgs e) => Apply();
    private void DisplayStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MetricSettingRow row })
        {
            row.DisplayStyle = row.DisplayStyle switch
            {
                MetricDisplayStyles.Stacked => MetricDisplayStyles.Inline,
                MetricDisplayStyles.Inline => MetricDisplayStyles.ValueOnly,
                MetricDisplayStyles.ValueOnly => MetricDisplayStyles.Chart,
                _ => MetricDisplayStyles.Stacked
            };
            Apply();
        }
    }
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MetricSettingRow row })
        {
            row.Accent = MetricColors.Next(row.Accent);
            Apply();
        }
    }
    private void AlertSetting_Changed(object sender, RoutedEventArgs e) => Apply();
    private void TestAlert_Click(object sender, RoutedEventArgs e) =>
        new StatsAlertWindow(new StatsAlertNotification(
            MetricIds.Cpu,
            "CPU 使用率警报",
            92,
            "当前 92%，已超过阈值 90%")) { Owner = this }.Show();
    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int offset)
    {
        if (sender is not Button { Tag: string id } || ModuleList.SelectedItem is not ModuleNavigationItem module)
        {
            return;
        }

        var visible = (module.Id == "all" ? _rows : _rows.Where(row => row.ModuleId == module.Id)).ToList();
        var index = visible.FindIndex(row => row.Id == id);
        var destination = index + offset;
        if (index < 0 || destination < 0 || destination >= visible.Count)
        {
            return;
        }

        var firstGlobalIndex = _rows.IndexOf(visible[index]);
        var secondGlobalIndex = _rows.IndexOf(visible[destination]);
        (_rows[firstGlobalIndex], _rows[secondGlobalIndex]) = (_rows[secondGlobalIndex], _rows[firstGlobalIndex]);
        Apply();
        RefreshModule();
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(new StatsApplicationSettings(StatsMetricCatalog.Defaults()));
        Apply();
        RefreshModule();
    }

    private void RefreshIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        _refreshIntervalMilliseconds = NextValue(_refreshIntervalMilliseconds, [500, 1000, 2000, 5000]);
        RefreshIntervalButton.Content = RefreshIntervalLabel(_refreshIntervalMilliseconds);
        Apply();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _historySeconds = NextValue(_historySeconds, [30, 60, 120, 300]);
        HistoryButton.Content = HistoryLabel(_historySeconds);
        Apply();
    }

    private void LowPowerModeCheck_Click(object sender, RoutedEventArgs e) => Apply();
    private void MinimalProfile_Click(object sender, RoutedEventArgs e) =>
        ApplyProfile([MetricIds.Cpu, MetricIds.Ram]);
    private void BalancedProfile_Click(object sender, RoutedEventArgs e) =>
        ApplyProfile([MetricIds.Cpu, MetricIds.Gpu, MetricIds.Ram, MetricIds.Power]);
    private void SensorProfile_Click(object sender, RoutedEventArgs e) =>
        ApplyProfile([
            MetricIds.Cpu, MetricIds.Gpu, MetricIds.Ram, MetricIds.Power,
            MetricIds.CpuTemperature, MetricIds.CpuFrequency,
            MetricIds.GpuTemperature, MetricIds.GpuPower
        ]);
    private void DisableAll_Click(object sender, RoutedEventArgs e) => ApplyProfile([]);
    private void Done_Click(object sender, RoutedEventArgs e) => Close();
    private void ExitStats_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "只退出 Stats，Seelen 与触控板继续运行，顶栏监控区域留空。可在 Seelen 套件设置中重新启动监控。",
            "退出 Stats",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            Application.Current.Shutdown();
        }
    }

    private void Apply()
    {
        if (_isLoading)
        {
            return;
        }

        _viewModel.ApplyApplicationSettings(new StatsApplicationSettings(
            _rows.Select(row => new StatsMetricPreference(row.Id, row.IsEnabled, row.DisplayStyle, row.Accent)).ToList(),
            _refreshIntervalMilliseconds,
            _historySeconds,
            LowPowerModeCheck.IsChecked != false,
            _alertRows.Select(row => new StatsAlertRule(
                row.MetricId,
                row.IsEnabled,
                row.Threshold,
                row.Comparison,
                row.SustainSeconds)).ToList()));
        RefreshPreview();
    }

    private void ApplyProfile(string[] enabled)
    {
        foreach (var row in _rows)
        {
            row.IsEnabled = enabled.Contains(row.Id);
        }
        Apply();
        RefreshModule();
    }

    private static int NextValue(int current, IReadOnlyList<int> values)
    {
        var index = values.ToList().IndexOf(current);
        return values[(index + 1 + values.Count) % values.Count];
    }

    private static string RefreshIntervalLabel(int milliseconds) => milliseconds == 500 ? "0.5 秒" : $"{milliseconds / 1000d:0.#} 秒";
    private static string HistoryLabel(int seconds) => seconds < 60 ? $"{seconds} 秒" : $"{seconds / 60} 分钟";

    private static string ModuleFor(string id) => id switch
    {
        MetricIds.Cpu or MetricIds.CpuTemperature or MetricIds.CpuFrequency => "cpu",
        MetricIds.Gpu or MetricIds.GpuTemperature or MetricIds.GpuPower or MetricIds.GpuMemory => "gpu",
        MetricIds.Ram => "memory",
        MetricIds.Disk => "disk",
        MetricIds.NetworkDownload or MetricIds.NetworkUpload => "network",
        MetricIds.Battery => "battery",
        MetricIds.Bluetooth => "bluetooth",
        _ => "sensors"
    };

    private static string SampleFor(string id) => id switch
    {
        MetricIds.Cpu => "24%", MetricIds.Gpu => "18%", MetricIds.Ram => "61%", MetricIds.Power => "72°",
        MetricIds.CpuTemperature => "68°", MetricIds.CpuFrequency => "4.1G", MetricIds.GpuTemperature => "57°",
        MetricIds.GpuPower => "38W", MetricIds.GpuMemory => "3.2G", MetricIds.Disk => "72%",
        MetricIds.NetworkDownload => "2.4M", MetricIds.NetworkUpload => "180K", MetricIds.Battery => "84%",
        MetricIds.Bluetooth => "2", _ => "--"
    };

    private sealed record ModuleNavigationItem(string Id, string Title, string Glyph, string Header, string Description);
    private sealed record PreviewMetric(string Title, string Accent, string Sample);
    private sealed record OverviewMetric(
        string Title,
        string Description,
        string Accent,
        string Sample,
        string Status,
        string StatusColor);

    private sealed class MetricSettingRow : INotifyPropertyChanged
    {
        private bool _isEnabled;

        private string _displayStyle;
        private string _accent;

        public MetricSettingRow(
            StatsMetricDefinition definition,
            string moduleId,
            bool isEnabled,
            string displayStyle,
            string? accent)
        {
            Id = definition.Id;
            Title = definition.Title;
            Description = definition.Description;
            ModuleId = moduleId;
            _isEnabled = isEnabled;
            _displayStyle = displayStyle;
            _accent = MetricColors.Normalize(accent, definition.Accent);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string ModuleId { get; }
        public string Accent
        {
            get => _accent;
            set
            {
                var normalized = MetricColors.Normalize(value, _accent);
                if (_accent == normalized) return;
                _accent = normalized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Accent)));
            }
        }
        public string DisplayStyleLabel => _displayStyle switch
        {
            MetricDisplayStyles.Inline => "横排",
            MetricDisplayStyles.ValueOnly => "紧凑",
            MetricDisplayStyles.Chart => "曲线",
            _ => "上下"
        };
        public string DisplayStyle
        {
            get => _displayStyle;
            set
            {
                if (_displayStyle == value) return;
                _displayStyle = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayStyle)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayStyleLabel)));
            }
        }
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    private sealed class AlertSettingRow
    {
        public AlertSettingRow(string metricId, string title, bool isEnabled, double threshold, string comparison, int sustainSeconds)
        {
            MetricId = metricId;
            Title = title;
            IsEnabled = isEnabled;
            Threshold = threshold;
            Comparison = comparison;
            SustainSeconds = sustainSeconds;
        }

        public string MetricId { get; }
        public string Title { get; }
        public bool IsEnabled { get; set; }
        public double Threshold { get; set; }
        public string Comparison { get; }
        public int SustainSeconds { get; set; }
        public string ComparisonLabel => Comparison == "below" ? "低于" : "高于";
        public string Unit => MetricId is MetricIds.CpuTemperature or MetricIds.GpuTemperature ? "°C" : "%";
    }
}
