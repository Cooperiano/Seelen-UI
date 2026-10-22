using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;
using MenuBar.Services;
using Monitoring.Contracts;

namespace MenuBar.ViewModels;

public sealed class ModulePopupViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IHardwareMetricsReader? _reader;
    private readonly bool _ownsReader;
    private bool _disposed;
    private readonly LiveMetricsHub? _liveMetrics;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly BluetoothMetricsReader _bluetoothReader = new();
    private bool _isRefreshing;
    private string _primaryValue = "--";
    private string _primaryCaption = "正在读取";
    private string _secondaryLeftValue = "--";
    private string _secondaryLeftLabel = string.Empty;
    private string _secondaryRightValue = "--";
    private string _secondaryRightLabel = string.Empty;
    private double _primaryPercent;
    private IDisposable? _moduleSession;
    private IReadOnlyList<ModuleProcessItem> _cachedProcesses = [];
    private IReadOnlyList<BluetoothDeviceMetric> _cachedBluetoothDevices = [];

    public ModulePopupViewModel(
        string requestedMetricId,
        IHardwareMetricsReader? reader = null,
        LiveMetricsHub? liveMetrics = null)
    {
        ModuleId = LiveMetricsHub.NormalizeModuleId(requestedMetricId);
        _liveMetrics = liveMetrics;
        _dispatcher = Dispatcher.CurrentDispatcher;
        (Title, Subtitle, Accent) = ModuleId switch
        {
            MetricIds.Cpu => ("CPU", "处理器", "#FF65D5FF"),
            MetricIds.Gpu => ("GPU", "图形处理器", "#FF72E6AE"),
            MetricIds.Ram => ("内存", "物理内存", "#FFFFC568"),
            MetricIds.Disk => ("磁盘", "系统卷", "#FF68C7E8"),
            "network" => ("网络", "活动连接", "#FF66D9C2"),
            MetricIds.Battery => ("电池", "电源状态", "#FF9DD56A"),
            MetricIds.Bluetooth => ("蓝牙", "设备连接", "#FF6EA8FF"),
            _ => ("传感器", "温度、电气与硬件状态", "#FFFF8DA1")
        };

        var settings = new StatsSettingsStore().LoadSettings();
        MaxHistorySamples = Math.Max(2, settings.HistorySeconds * 1000 / settings.RefreshIntervalMilliseconds);
        _ownsReader = liveMetrics is null && reader is null;
        _reader = liveMetrics is null ? reader ?? new HardwareMetricsReader() : null;
        if (_reader is not null)
        {
            _reader.ReadPlatformMetrics = ModuleId is MetricIds.Cpu or "sensors";
            _reader.ReadNvidiaExtendedMetrics = ModuleId is MetricIds.Gpu or "sensors";
            _reader.ReadBluetoothMetrics = ModuleId == MetricIds.Bluetooth;
            _reader.ReadMemoryDetails = ModuleId == MetricIds.Ram;
            _reader.ReadSensorMetrics = ModuleId is MetricIds.Gpu or "sensors";
            _reader.ReadProcessMetrics = ShowsProcesses;
        }
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(
                _liveMetrics is null
                    ? settings.RefreshIntervalMilliseconds
                    : Math.Max(4000, settings.RefreshIntervalMilliseconds))
        };
        _timer.Tick += OnTick;
        RefreshLabel = _liveMetrics is null
            ? $"{settings.RefreshIntervalMilliseconds / 1000d:0.#} 秒更新"
            : $"{settings.RefreshIntervalMilliseconds / 1000d:0.#} 秒 · 按需";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<double?>? SampleUpdated;

    public string ModuleId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Accent { get; }
    public string RefreshLabel { get; }
    public int MaxHistorySamples { get; }
    public IReadOnlyList<double> InitialHistory => _liveMetrics?.GetHistory(ModuleId) ?? [];
    public ObservableCollection<ModuleDetailItem> Details { get; } = [];
    public ObservableCollection<ModuleProcessItem> Processes { get; } = [];
    public ObservableCollection<ModuleCoreItem> Cores { get; } = [];
    public bool ShowsProcesses => ModuleId is MetricIds.Cpu or MetricIds.Ram;
    public string ProcessCaption => ModuleId == MetricIds.Ram ? "高占用进程 · 私有驻留内存" : "高占用进程 · 整机 CPU 占比";
    public bool ShowsCores => ModuleId == MetricIds.Cpu;
    public bool ShowsDetails => true;
    public string PrimaryValue { get => _primaryValue; private set => SetField(ref _primaryValue, value); }
    public string PrimaryCaption { get => _primaryCaption; private set => SetField(ref _primaryCaption, value); }
    public string SecondaryLeftValue { get => _secondaryLeftValue; private set => SetField(ref _secondaryLeftValue, value); }
    public string SecondaryLeftLabel { get => _secondaryLeftLabel; private set => SetField(ref _secondaryLeftLabel, value); }
    public string SecondaryRightValue { get => _secondaryRightValue; private set => SetField(ref _secondaryRightValue, value); }
    public string SecondaryRightLabel { get => _secondaryRightLabel; private set => SetField(ref _secondaryRightLabel, value); }
    public double PrimaryPercent { get => _primaryPercent; private set => SetField(ref _primaryPercent, value); }

    public void Start()
    {
        if (_liveMetrics is not null)
        {
            _liveMetrics.MetricsUpdated += OnLiveMetricsUpdated;
            _moduleSession = _liveMetrics.BeginModuleSession(ModuleId);
            if (_liveMetrics.Latest is { } latest)
            {
                Apply(latest, _cachedProcesses, _cachedBluetoothDevices);
            }
            _ = RefreshAuxiliaryAsync();
            _timer.Start();
            return;
        }

        _ = RefreshAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        if (_liveMetrics is not null)
        {
            _liveMetrics.MetricsUpdated -= OnLiveMetricsUpdated;
        }
        _moduleSession?.Dispose();
        _moduleSession = null;
        if (_ownsReader && _reader is IDisposable ownedReader) ownedReader.Dispose();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_liveMetrics is null)
        {
            await RefreshAsync();
        }
        else
        {
            await RefreshAuxiliaryAsync();
        }
    }

    private void OnLiveMetricsUpdated(HardwareMetrics metrics) =>
        _dispatcher.BeginInvoke(() => { if (!_disposed) Apply(metrics, _cachedProcesses, _cachedBluetoothDevices); });

    private async Task RefreshAuxiliaryAsync()
    {
        // CPU/RAM rows belong to the publisher's cache, not a popup-local poller.
        if (ModuleId != MetricIds.Bluetooth) return;
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var auxiliary = await Task.Run(() => (
                Processes: CaptureProcesses(_liveMetrics?.Latest),
                BluetoothDevices: ModuleId == MetricIds.Bluetooth ? _bluetoothReader.Capture() : (IReadOnlyList<BluetoothDeviceMetric>)[]));
            _cachedProcesses = auxiliary.Processes;
            _cachedBluetoothDevices = auxiliary.BluetoothDevices;
            if (_liveMetrics?.Latest is { } latest)
            {
                await _dispatcher.InvokeAsync(() => { if (!_disposed) Apply(latest, _cachedProcesses, _cachedBluetoothDevices); });
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var metrics = _reader!.Capture();
                return new PopupSnapshot(metrics, CaptureProcesses(metrics),
                    ModuleId == MetricIds.Bluetooth ? _bluetoothReader.Capture() : []);
            });
            await _dispatcher.InvokeAsync(() => { if (!_disposed) Apply(snapshot.Metrics, snapshot.Processes, snapshot.BluetoothDevices); });
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void Apply(
        HardwareMetrics metrics,
        IReadOnlyList<ModuleProcessItem> processes,
        IReadOnlyList<BluetoothDeviceMetric> bluetoothDevices)
    {
        Details.Clear();
        Processes.Clear();
        Cores.Clear();
        foreach (var process in metrics.ProcessResources is null ? processes : CaptureProcesses(metrics))
        {
            Processes.Add(process);
        }
        double? sample;
        switch (ModuleId)
        {
            case MetricIds.Cpu:
                sample = metrics.CpuPercent;
                SetPercentHero(metrics.CpuPercent, "总使用率");
                SecondaryLeftValue = MetricFormatter.Temperature(metrics.CpuTemperatureCelsius);
                SecondaryLeftLabel = "温度";
                SecondaryRightValue = MetricFormatter.Frequency(metrics.CpuFrequencyMhz);
                SecondaryRightLabel = "频率";
                AddHeader("CPU 时间");
                Add("System", MetricFormatter.Percent(metrics.CpuUsage.SystemPercent), Accent);
                Add("User", MetricFormatter.Percent(metrics.CpuUsage.UserPercent));
                Add("Idle", MetricFormatter.Percent(metrics.CpuUsage.IdlePercent));
                AddHeader("核心与频率");
                Add("物理核心 / 逻辑处理器", FormatCorePair(metrics.CpuUsage.PhysicalCoreCount, metrics.CpuUsage.LogicalProcessorCount));
                Add("Performance cores", FormatCount(metrics.CpuUsage.PerformanceCoreCount));
                Add("Efficiency cores", FormatCount(metrics.CpuUsage.EfficiencyCoreCount));
                Add("当前频率", FormatFrequencyLong(metrics.CpuFrequencyMhz));
                Add("ACPI 温区", FormatTemperatureLong(metrics.CpuTemperatureCelsius));
                AddHeader("平均负载");
                Add("1 分钟", FormatLoad(metrics.CpuUsage.LoadAverage1Minute));
                Add("5 分钟", FormatLoad(metrics.CpuUsage.LoadAverage5Minutes));
                Add("15 分钟", FormatLoad(metrics.CpuUsage.LoadAverage15Minutes));
                if (metrics.ProcessorFrequencies is { Count: > 0 } frequencies)
                {
                    AddHeader("逻辑处理器频率 · 系统估算");
                    foreach (var frequency in frequencies)
                        Add($"处理器 {frequency.Group}:{frequency.Processor}", $"{frequency.EstimatedMhz / 1000:0.00} GHz",
                            toolTip: $"Windows ProcessorInformation: {frequency.NominalMhz:0} MHz × {frequency.PerformancePercent:0}%；不是 MSR 直读频率。");
                }
                for (var index = 0; index < metrics.CpuCorePercents.Count; index++)
                {
                    Cores.Add(new ModuleCoreItem($"{index}", metrics.CpuCorePercents[index], $"{metrics.CpuCorePercents[index]:0}%"));
                }
                break;
            case MetricIds.Gpu:
                sample = metrics.GpuPercent;
                SetPercentHero(metrics.GpuPercent, "图形处理器使用率");
                SecondaryLeftValue = MetricFormatter.Temperature(metrics.GpuTemperatureCelsius);
                SecondaryLeftLabel = "温度";
                SecondaryRightValue = MetricFormatter.Power(metrics.GpuPowerWatts);
                SecondaryRightLabel = "功耗";
                AddHeader("GPU 汇总");
                Add("总使用率", MetricFormatter.Percent(metrics.GpuPercent), Accent);
                Add("独立显存", FormatGpuMemory(metrics.GpuMemoryUsedMb, metrics.GpuMemoryTotalMb));
                Add("温度", FormatTemperatureLong(metrics.GpuTemperatureCelsius));
                Add("功耗", FormatPowerLong(metrics.GpuPowerWatts));
                AddGpuDetails(metrics);
                break;
            case MetricIds.Ram:
                sample = metrics.MemoryPercent;
                SetPercentHero(metrics.MemoryPercent, "内存使用率");
                SecondaryLeftValue = FormatBytes(metrics.MemoryUsedBytes);
                SecondaryLeftLabel = "已使用";
                SecondaryRightValue = FormatBytes(metrics.MemoryTotalBytes);
                SecondaryRightLabel = "总容量";
                AddHeader("物理内存");
                Add("使用率", MetricFormatter.Percent(metrics.MemoryPercent), Accent);
                Add("App / Active（估算）", FormatBytesLong(EstimateApplicationMemory(metrics)));
                Add("内核池（分页 / 非分页）", FormatPair(metrics.MemoryBreakdown.PagedPoolBytes, metrics.MemoryBreakdown.NonPagedPoolBytes));
                Add("Compressed · 压缩存储驻留", FormatBytesLong(metrics.MemoryBreakdown.CompressedBytes),
                    toolTip: metrics.MemoryCompression?.Probe.Detail ?? "Windows 压缩存储占用；不是 macOS 的同名分类。");
                Add("文件缓存", FormatBytesLong(metrics.MemoryBreakdown.CacheBytes));
                Add("可用", FormatBytesLong(metrics.MemoryBreakdown.AvailableBytes));
                Add("总容量", FormatBytesLong(metrics.MemoryTotalBytes));
                AddHeader("虚拟内存");
                Add("已提交", FormatBytesLong(metrics.MemoryBreakdown.CommittedBytes));
                Add("提交上限", FormatBytesLong(metrics.MemoryBreakdown.CommitLimitBytes));
                Add("Swap", FormatPair(metrics.MemoryBreakdown.SwapUsedBytes, metrics.MemoryBreakdown.SwapTotalBytes));
                break;
            case MetricIds.Disk:
                sample = metrics.DiskUsedPercent;
                SetPercentHero(metrics.DiskUsedPercent, "系统磁盘占用");
                SecondaryLeftValue = FormatBytes(metrics.DiskUsedBytes);
                SecondaryLeftLabel = "已使用";
                SecondaryRightValue = FormatBytes(metrics.DiskTotalBytes);
                SecondaryRightLabel = "总容量";
                Add("磁盘占用", MetricFormatter.Percent(metrics.DiskUsedPercent), Accent);
                Add("已使用", FormatBytesLong(metrics.DiskUsedBytes));
                Add("可用", FormatBytesLong(Subtract(metrics.DiskTotalBytes, metrics.DiskUsedBytes)));
                Add("系统卷容量", FormatBytesLong(metrics.DiskTotalBytes));
                break;
            case "network":
                sample = metrics.NetworkDownloadBytesPerSecond;
                PrimaryValue = FormatRate(metrics.NetworkDownloadBytesPerSecond);
                PrimaryCaption = "下载速率";
                PrimaryPercent = 0;
                SecondaryLeftValue = FormatRate(metrics.NetworkUploadBytesPerSecond);
                SecondaryLeftLabel = "上传";
                SecondaryRightValue = "实时";
                SecondaryRightLabel = "采样";
                Add("下载", FormatRateLong(metrics.NetworkDownloadBytesPerSecond), Accent);
                Add("上传", FormatRateLong(metrics.NetworkUploadBytesPerSecond));
                break;
            case MetricIds.Battery:
                sample = metrics.BatteryPercent;
                SetPercentHero(metrics.BatteryPercent, "剩余电量");
                SecondaryLeftValue = MetricFormatter.Power(metrics.PowerWatts);
                SecondaryLeftLabel = "实时功耗";
                SecondaryRightValue = metrics.PowerWatts is null ? "外接" : "电池";
                SecondaryRightLabel = "供电";
                Add("剩余电量", MetricFormatter.Percent(metrics.BatteryPercent), Accent);
                Add("充放电功耗", FormatPowerLong(metrics.PowerWatts));
                Add("电池健康度", MetricFormatter.Percent(metrics.BatteryHealthPercent));
                Add("循环次数", metrics.BatteryCycleCount is null ? "不可用" : $"{metrics.BatteryCycleCount} 次");
                Add("当前电压", metrics.BatteryVoltageVolts is null ? "不可用" : $"{metrics.BatteryVoltageVolts:0.00} V");
                Add("满充容量", FormatBatteryCapacity(metrics.BatteryFullChargeCapacityMwh));
                Add("设计容量", FormatBatteryCapacity(metrics.BatteryDesignCapacityMwh));
                break;
            case MetricIds.Bluetooth:
                sample = metrics.BluetoothConnectedDeviceCount;
                PrimaryValue = metrics.BluetoothConnectedDeviceCount is null ? "--" : $"{metrics.BluetoothConnectedDeviceCount:0}";
                PrimaryCaption = "已连接设备";
                PrimaryPercent = 0;
                SecondaryLeftValue = $"{bluetoothDevices.Count(item => item.IsAuthenticated)}";
                SecondaryLeftLabel = "已认证";
                SecondaryRightValue = $"{bluetoothDevices.Count}";
                SecondaryRightLabel = "已记忆";
                if (bluetoothDevices.Count == 0)
                {
                    Add("设备", "未发现已配对设备", Accent);
                }
                else
                {
                    foreach (var device in bluetoothDevices.Take(6))
                    {
                        Add(device.Name, device.IsConnected ? "已连接" : device.IsRemembered ? "已配对" : "已发现", device.IsConnected ? Accent : "#FF68717D");
                    }
                }
                break;
            default:
                var power = SensorMetricSelector.PreferredPowerWatts(metrics);
                var temperature = SensorMetricSelector.PreferredTemperatureCelsius(metrics);
                sample = temperature;
                PrimaryValue = MetricFormatter.Temperature(temperature);
                PrimaryCaption = "CPU 温度";
                PrimaryPercent = Math.Clamp(temperature ?? 0, 0, 100);
                SecondaryLeftValue = MetricFormatter.Power(power);
                SecondaryLeftLabel = SensorMetricSelector.PreferredPowerCaption(metrics);
                SecondaryRightValue = $"{metrics.Sensors.Count}";
                SecondaryRightLabel = "可读传感器";
                AddSensorDetails(metrics);
                break;
        }

        SampleUpdated?.Invoke(sample);
    }

    private void SetPercentHero(double? value, string caption)
    {
        PrimaryValue = MetricFormatter.Percent(value);
        PrimaryCaption = caption;
        PrimaryPercent = Math.Clamp(value ?? 0, 0, 100);
    }

    private void Add(string label, string value, string? accent = null, string? toolTip = null) =>
        Details.Add(new ModuleDetailItem(label, value, accent ?? "#FF68717D", false, toolTip ?? label));

    private void AddHeader(string title) =>
        Details.Add(new ModuleDetailItem(title, string.Empty, "#FF202124", true));

    private void AddGpuDetails(HardwareMetrics metrics)
    {
        if (metrics.GpuEngines is { } windows)
        {
            foreach (var adapter in windows.Engines.GroupBy(engine => (engine.AdapterLuid, engine.PhysicalAdapter)))
            {
                var identity = windows.Adapters.FirstOrDefault(value => value.Luid == adapter.Key.AdapterLuid);
                AddHeader(identity is null ? $"WDDM · {adapter.Key.AdapterLuid}" : ShortHardwareName(identity.Name) + " · WDDM");
                Add("最忙引擎", $"{adapter.Max(engine => engine.UtilizationPercent):0.#} %",
                    toolTip: $"显卡 {adapter.Key.AdapterLuid} / physical {adapter.Key.PhysicalAdapter}；不同显卡不相加。");
                foreach (var engine in adapter.OrderBy(engine => engine.Engine))
                    Add($"{(string.IsNullOrWhiteSpace(engine.EngineType) ? "未命名引擎" : engine.EngineType)} #{engine.Engine}", $"{engine.UtilizationPercent:0.#} %",
                        toolTip: $"WDDM 引擎标签；不是推算的 AI/Compute 利用率。LUID {engine.AdapterLuid}。");
            }
        }
        var sensors = metrics.Sensors;
        var nativeDevices = metrics.NvidiaDevices ?? [];
        var gpuGroups = sensors
            .Where(item => item.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Hardware, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (gpuGroups.Count == 0 && nativeDevices.Count == 0)
        {
            AddHeader("GPU 设备");
            Add("详细引擎", "硬件或驱动未公开");
            return;
        }

        var deviceNames = nativeDevices.Select(device => device.Name).Concat(gpuGroups.Select(group => group.Key))
            .Distinct(StringComparer.CurrentCultureIgnoreCase);
        foreach (var deviceName in deviceNames)
        {
            AddHeader(ShortHardwareName(deviceName));
            var native = nativeDevices.FirstOrDefault(device => device.Name.Equals(deviceName, StringComparison.CurrentCultureIgnoreCase));
            Add("GPU 核心数", native?.CoreCount is { } coreCount ? $"{coreCount}" : "此接口未返回");
            if (native is not null)
            {
                if (native.GpuUtilizationPercent is { } utilization) Add("GPU 利用率 · NVML", $"{utilization} %");
                if (native.MemoryUtilizationPercent is { } memoryBusy) Add("显存控制器忙碌", $"{memoryBusy} %");
                if (native.EncoderUtilizationPercent is { } encoder) Add("编码引擎", $"{encoder} %");
                if (native.DecoderUtilizationPercent is { } decoder) Add("解码引擎", $"{decoder} %");
                if (native.GraphicsClockMhz is { } clock) Add("图形核心频率", $"{clock} MHz");
                if (native.MemoryClockMhz is { } memoryClock) Add("显存频率", $"{memoryClock} MHz");
                if (native.PcieGeneration is { } generation && native.PcieWidth is { } width)
                    Add("PCIe 链路", $"Gen {generation} ×{width}");
            }
            foreach (var sensor in sensors.Where(item => item.Hardware.Equals(deviceName, StringComparison.CurrentCultureIgnoreCase) &&
                item.Type is "Load" or "Clock" or "Temperature" or "Power" or "Energy" or "Data" or "SmallData"))
            {
                Add(SensorDisplayName(sensor.Name), FormatSensor(sensor), sensor.Type is "Temperature" or "Power" ? Accent : null);
            }
            Add("FPS", "帧采集未启用", toolTip: "需要独立的应用呈现事件采集；显示器刷新率不等于应用 FPS。当前未在 Stats 中启用常驻 ETW 采集。");
        }
    }

    private void AddSensorDetails(HardwareMetrics metrics)
    {
        var sensorTypes = new[] { "Temperature", "Voltage", "Current", "Power", "Energy", "Fan" };
        foreach (var type in sensorTypes)
        {
            AddHeader(SensorTypeTitle(type));
            var values = metrics.Sensors.Where(item => item.Type == type).ToList();
            if (type == "Temperature" && metrics.CpuTemperatureCelsius is not null &&
                values.All(item => !item.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)))
            {
                Add("ACPI · CPU 温区", FormatTemperatureLong(metrics.CpuTemperatureCelsius), Accent);
            }
            foreach (var sensor in values)
            {
                var hardwareName = sensor.HardwareType == "Storage" && sensor.Hardware.Contains(' ')
                    ? sensor.Hardware[(sensor.Hardware.IndexOf(' ') + 1)..] : ShortHardwareName(sensor.Hardware);
                Add($"{hardwareName} · {SensorDisplayName(sensor.Name)}", FormatSensor(sensor),
                    type is "Temperature" or "Power" ? Accent : null, sensor.Hardware + " · " + sensor.Name);
            }
            if (values.Count == 0 && !(type == "Temperature" && metrics.CpuTemperatureCelsius is not null))
            {
                Add("设备", "没有公开此类传感器");
            }
            AddRequestedUnavailableRows(type, values);
        }
    }

    private void AddRequestedUnavailableRows(string type, IReadOnlyList<HardwareSensorReading> values)
    {
        static bool Has(IReadOnlyList<HardwareSensorReading> source, params string[] terms) =>
            source.Any(item => terms.Any(term => (item.Hardware + " " + item.Name).Contains(term, StringComparison.OrdinalIgnoreCase)));

        // "AC" also matches "Package"; it must not hide unavailable adapter readings.
        var hasInput = Has(values, "DC-in", "DC input", "Adapter", "AC input", "Brick");
        if (type == "Voltage" && !hasInput) Add("DC-in 电压", "未找到可用输入端读数");
        if (type == "Current" && !hasInput) Add("DC-in 电流", "未找到可用输入端读数");
        if (type != "Power") return;
        if (!hasInput) Add("DC-in 功率", "未找到可用输入端读数");
        if (!Has(values, "System Total")) Add("System Total", "没有有效整机计量源");
        if (!Has(values, "Average System Total")) Add("Average System Total", "需要有效整机计量源");
        if (!Has(values, "USB")) Add("USB 端口功率", "待探测 USB-PD 控制器");
        if (!Has(values, "NPU", "ANE", "Neural")) Add("NPU / ANE 功率", "本机无独立 NPU");
        if (!Has(values, "PCI", "PCIe")) Add("PCIe 功率", "没有独立计量读数");
    }

    private IReadOnlyList<ModuleProcessItem> CaptureProcesses(HardwareMetrics? metrics)
    {
        if (!ShowsProcesses || metrics?.ProcessResources is not { } resources) return [];
        // Keep each PID separate; WMI instance suffixes and same-named apps are not silently merged.
        return resources.Processes.Select(process => (Name: $"{process.Name} · {process.ProcessId}",
                Value: ModuleId == MetricIds.Ram ? process.PrivateWorkingSetBytes / 1024d / 1024d : process.CpuPercent))
            .OrderByDescending(item => item.Value)
            .Take(4)
            .Select(item => new ModuleProcessItem(
                item.Name,
                ModuleId == MetricIds.Ram ? $"{item.Value:0} MB" : $"{item.Value:0.0}%"))
            .ToList();
    }

    private static string FormatTemperatureLong(double? value) => value is null ? "不可用" : $"{value:0.0} °C";
    private static string FormatFrequencyLong(double? value) => value is null ? "不可用" : $"{value:0} MHz";
    private static string FormatPowerLong(double? value) => value is null ? "不可用" : $"{value:0.0} W";
    private static string FormatLoad(double? value) => value is null ? "不可用" : $"{value:0.000}";
    private static string FormatCount(int? value) => value is null ? "硬件未公开" : $"{value}";
    private static string FormatCorePair(int? physical, int? logical) =>
        physical is null || logical is null ? "硬件未公开" : $"{physical} / {logical}";
    private static string FormatGpuMemory(double? used, double? total) => used is null ? "--" : $"{used:0} / {total:0} MB";
    private static ulong? Subtract(ulong? total, ulong? used) => total is not null && used is not null ? total - used : null;
    private static ulong? AddBytes(ulong? first, ulong? second) => first is not null && second is not null ? first + second : null;
    private static ulong? EstimateApplicationMemory(HardwareMetrics metrics)
    {
        if (metrics.MemoryUsedBytes is not { } used)
        {
            return null;
        }
        var cache = metrics.MemoryBreakdown.CacheBytes ?? 0;
        var kernel = AddBytes(metrics.MemoryBreakdown.PagedPoolBytes, metrics.MemoryBreakdown.NonPagedPoolBytes) ?? 0;
        return used > cache + kernel ? used - cache - kernel : 0;
    }
    private static string FormatBytes(ulong? bytes) => bytes is null ? "--" : $"{bytes.Value / 1024d / 1024d / 1024d:0.0}G";
    private static string FormatBytesLong(ulong? bytes) => bytes is null ? "不可用" : $"{bytes.Value / 1024d / 1024d / 1024d:0.00} GB";
    private static string FormatPair(ulong? used, ulong? total) =>
        used is null || total is null ? "不可用" : $"{FormatBytesLong(used)} / {FormatBytesLong(total)}";
    private static string SensorTypeTitle(string type) => type switch
    {
        "Temperature" => "Temperature · 温度",
        "Voltage" => "Voltage · 电压",
        "Current" => "Current · 电流",
        "Power" => "Power · 功率",
        "Energy" => "Energy · 能量",
        "Fan" => "Fans · 风扇",
        _ => type
    };
    private static string ShortHardwareName(string name)
    {
        if (name == "Windows RAPL") return "RAPL";
        if (name.Contains("A32-K55", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Battery", StringComparison.OrdinalIgnoreCase)) return "电池";
        if (name.StartsWith("NVIDIA GeForce ", StringComparison.OrdinalIgnoreCase))
            return name.Replace("NVIDIA GeForce ", "", StringComparison.OrdinalIgnoreCase).Replace(" Laptop GPU", "", StringComparison.OrdinalIgnoreCase);
        if (name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase)) return "Intel UHD";
        if (name.Contains("Core i7-11800H", StringComparison.OrdinalIgnoreCase)) return "CPU";
        return name.Length <= 22 ? name : name[..21] + "…";
    }
    private static string SensorDisplayName(string name) => name
        .Replace("GPU Memory Temperature", "显存温度", StringComparison.Ordinal)
        .Replace("Battery Current (calculated P/V)", "电流（P/V 计算）", StringComparison.Ordinal)
        .Replace("Battery Charge/Discharge Rate", "充放电功率（正充/负放）", StringComparison.Ordinal)
        .Replace("Battery Voltage", "电压", StringComparison.Ordinal)
        .Replace("Battery Temperature", "电池温度", StringComparison.Ordinal)
        .Replace("CPU Package", "CPU 封装", StringComparison.Ordinal)
        .Replace("CPU Cores", "CPU 核心", StringComparison.Ordinal)
        .Replace("GPU Package", "GPU 封装", StringComparison.Ordinal)
        .Replace("Drive Temperature", "综合温度", StringComparison.Ordinal)
        .Replace("Temperature Sensor ", "温度传感器 ", StringComparison.Ordinal)
        .Replace("Integrated GPU", "核显", StringComparison.Ordinal)
        .Replace("Session Average", "本次平均", StringComparison.Ordinal)
        .Replace("1 s Average", "1 秒平均", StringComparison.Ordinal)
        .Replace("Instant", "瞬时", StringComparison.Ordinal)
        .Replace("Session Energy", "本次耗能", StringComparison.Ordinal);
    private static string FormatSensor(HardwareSensorReading sensor)
    {
        var decimals = sensor.Type switch
        {
            "Voltage" => 3,
            "Current" or "Power" => 2,
            "Temperature" => 1,
            _ => sensor.Value >= 100 ? 0 : 2
        };
        return $"{sensor.Value.ToString($"F{decimals}")} {sensor.Unit}".TrimEnd();
    }
    private static string FormatRate(double? bytesPerSecond) => bytesPerSecond switch
    {
        null => "--",
        >= 1024 * 1024 => $"{bytesPerSecond / 1024d / 1024d:0.0}M",
        >= 1024 => $"{bytesPerSecond / 1024d:0}K",
        _ => $"{bytesPerSecond:0}B"
    };
    private static string FormatRateLong(double? bytesPerSecond) => bytesPerSecond is null ? "不可用" : $"{FormatRate(bytesPerSecond)}/s";
    private static string FormatBatteryCapacity(ulong? milliwattHours) => milliwattHours is null ? "不可用" : $"{milliwattHours.Value / 1000d:0.0} Wh";

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ModuleDetailItem(string Label, string Value, string Accent, bool IsHeader, string? ToolTip = null);
public sealed record ModuleProcessItem(string Name, string Value);
public sealed record ModuleCoreItem(string Name, double Percent, string Value);
internal sealed record PopupSnapshot(
    HardwareMetrics Metrics,
    IReadOnlyList<ModuleProcessItem> Processes,
    IReadOnlyList<BluetoothDeviceMetric> BluetoothDevices);
