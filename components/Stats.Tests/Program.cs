using MenuBar.Services;
using MenuBar;
using MenuBar.ViewModels;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;
using Monitoring.Contracts;

DesktopSuiteIntegrationTests.Run();
if (args.Contains("--suite-contract-only", StringComparer.Ordinal)) return;
DesktopSuiteWindowTests.Run();
if (args.Contains("--suite-ui-only", StringComparer.Ordinal)) return;

if (args.Contains("--gpu-node-metadata-tests-only", StringComparer.Ordinal))
{
    GpuNodeMetadataTests.Run();
    TelemetryProbeArgumentTests.Run();
    return;
}

if (args.Contains("--provider-selection-only", StringComparer.Ordinal))
{
    LhmProviderFactoryTests.Run();
    return;
}

if (args.Contains("--cpu-capabilities-tests-only", StringComparer.Ordinal))
{
    CpuHardwareCapabilityTests.Run();
    return;
}

if (args.Contains("--cpu-coverage-only", StringComparer.Ordinal))
{
    CpuSensorCoverageTests.Run();
    TelemetryProbeArgumentTests.Run();
    return;
}

if (args.Contains("--intel-dptf-parser-only", StringComparer.Ordinal))
{
    IntelDptfParserTests.Run();
    return;
}

if (args.Contains("--asus-parser-only", StringComparer.Ordinal))
{
    AsusDstsParserTests.Run();
    return;
}

if (args.Contains("--power-meter-parser-only", StringComparer.Ordinal))
{
    PowerMeterParserTests.Run();
    return;
}

if (args.Contains("--publisher-performance", StringComparer.Ordinal))
{
    var snapshot = PublisherDiagnostics.Read();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot));
    return;
}

CollectorDemandTests.Run();
PublisherLifecycleTests.Run(Sample);
if (args.Contains("--collector-only", StringComparer.Ordinal)) return;

var cases = new (string? ProductName, string ProcessName, string Expected)[]
{
    ("Windows Explorer", "explorer", "文件资源管理器"),
    ("Microsoft® Windows® Operating System", "ShellExperienceHost", "Windows"),
    (null, "notepad", "notepad"),
    ("  Visual Studio Code  ", "Code", "Visual Studio Code")
};

foreach (var testCase in cases)
{
    var actual = ActiveApplicationFormatter.Normalize(testCase.ProductName, testCase.ProcessName);
    if (!string.Equals(actual, testCase.Expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{testCase.Expected}', got '{actual}'.");
    }
}

Console.WriteLine($"Passed {cases.Length} formatter checks.");

var metricCases = new (string Name, string Actual, string Expected)[]
{
    ("missing percent", MetricFormatter.Percent(null), "--"),
    ("rounded percent", MetricFormatter.Percent(42.6), "43%"),
    ("clamped percent", MetricFormatter.Percent(140), "100%"),
    ("missing power", MetricFormatter.Power(null), "--"),
    ("small power", MetricFormatter.Power(7.26), "7.3W"),
    ("large power", MetricFormatter.Power(18.8), "19W"),
    ("temperature", MetricFormatter.Temperature(66.4), "66°"),
    ("frequency", MetricFormatter.Frequency(4210), "4.2G"),
    ("small vram", MetricFormatter.MemoryMb(768), "768M"),
    ("large vram", MetricFormatter.MemoryMb(1536), "1.5G"),
    ("network kilobytes", MetricFormatter.Rate(42 * 1024), "42K"),
    ("network megabytes", MetricFormatter.Rate(2.4 * 1024 * 1024), "2.4M")
};

foreach (var testCase in metricCases)
{
    if (!string.Equals(testCase.Actual, testCase.Expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{testCase.Name}: expected '{testCase.Expected}', got '{testCase.Actual}'.");
    }
}

Console.WriteLine($"Passed {metricCases.Length} metric formatting checks.");

var energySession = new EnergyMeterSession();
if (energySession.Observe("PKG", 100_000_000_000, 1000) is not null)
    throw new InvalidOperationException("An energy counter must warm up before showing session consumption.");
var energySample = energySession.Observe("PKG", 110_000_000_000, 11000);
if (energySample is not { } measured || measured.MilliWattHours != 10 || Math.Abs(measured.AverageWatts - 3.6) > 0.000001)
    throw new InvalidOperationException("pWh/ms conversion produced incorrect session energy or power.");
if (energySession.Observe("PKG", 111_000_000_000, 11000) is not null)
    throw new InvalidOperationException("A changing energy value with no elapsed time must be rejected.");
if (energySession.Observe("PKG", 1, 12000) is not null ||
    energySession.Observe("PKG", 10_000_000_001, 22000)?.MilliWattHours != 10)
    throw new InvalidOperationException("An energy counter reset must establish a new baseline.");
if (energySession.Observe("PKG", 20_000_000_001, 100) is not null ||
    energySession.Observe("PP0", 3000, 100) is not null)
    throw new InvalidOperationException("Time resets and independent rails must establish separate baselines.");
if (energySession.Observe("PP0", 3000, 200)?.AverageWatts != 0)
    throw new InvalidOperationException("A valid stationary rail must retain real zero power.");
Console.WriteLine("Passed energy units, warm-up, invalid samples, counter resets, rail isolation, and zero checks.");
NativeInterfaceParserTests.Run();
PresentMonInterfaceTests.Run();
NvidiaFieldParserTests.Run();
LegacySensorDiscoveryTests.Run();
PowerMeterParserTests.Run();
AsusDstsParserTests.Run();
IntelDptfParserTests.Run();
CpuSensorCoverageTests.Run();
TelemetryProbeArgumentTests.Run();
CpuHardwareCapabilityTests.Run();
LhmProviderFactoryTests.Run();
GpuNodeMetadataTests.Run();

var reader = new HardwareMetricsReader();
_ = reader.Capture();
await Task.Delay(2200);
var liveMetrics = reader.Capture();
if (liveMetrics.CpuPercent is null || liveMetrics.MemoryPercent is null ||
    liveMetrics.DiskUsedPercent is null || liveMetrics.DiskTotalBytes is null ||
    liveMetrics.NetworkDownloadBytesPerSecond is null || liveMetrics.NetworkUploadBytesPerSecond is null)
{
    throw new InvalidOperationException("Expected core Windows CPU, memory, disk, and network metrics to be available.");
}

Console.WriteLine(
    $"Live metrics: CPU {MetricFormatter.Percent(liveMetrics.CpuPercent)}, " +
    $"GPU {MetricFormatter.Percent(liveMetrics.GpuPercent)}, " +
    $"RAM {MetricFormatter.Percent(liveMetrics.MemoryPercent)}, " +
    $"DISK {MetricFormatter.Percent(liveMetrics.DiskUsedPercent)}, " +
    $"NET ↓{liveMetrics.NetworkDownloadBytesPerSecond / 1024d:0}K ↑{liveMetrics.NetworkUploadBytesPerSecond / 1024d:0}K, " +
    $"BAT {MetricFormatter.Percent(liveMetrics.BatteryPercent)}.");

reader.ReadPlatformMetrics = true;
reader.ReadNvidiaExtendedMetrics = true;
reader.ReadBluetoothMetrics = true;
reader.ReadMemoryDetails = true;
reader.ReadSensorMetrics = true;
reader.ReadProcessMetrics = true;
_ = reader.Capture();
await Task.Delay(5200);
var extendedMetrics = reader.Capture();
if (extendedMetrics.ProcessResources is not { Processes.Count: > 0 } resources ||
    !resources.Processes.Any(process => process.ProcessId == Environment.ProcessId && process.WorkingSetBytes > 0) ||
    resources.Processes.Any(process => !double.IsFinite(process.CpuPercent) || process.PrivateWorkingSetBytes > process.WorkingSetBytes))
    throw new InvalidOperationException("Live process counters failed identity, CPU, or resident memory validation.");
if (extendedMetrics.GpuEngines is not { Engines.Count: > 0, Adapters.Count: > 0 } engines ||
    engines.Engines.Any(engine => !engines.Adapters.Any(adapter => adapter.Luid == engine.AdapterLuid)))
    throw new InvalidOperationException("Live GPU engine identities did not match native DXGI adapter LUIDs.");
Console.WriteLine($"Windows resources: {resources.Processes.Count} processes; {engines.Engines.Count} engines on {engines.Engines.Select(engine => engine.AdapterLuid).Distinct().Count()} mapped adapters.");
if (extendedMetrics.MemoryCompression is { Value: { } compressionValue })
{
    if (extendedMetrics.MemoryBreakdown.CompressedBytes != compressionValue.ResidentBytes ||
        (compressionValue.ResidentBytes > 0 && !resources.Processes.Any(process => process.ProcessId == compressionValue.ProcessId && process.Name == "Memory Compression")))
        throw new InvalidOperationException("Compression store native PID/resident bytes did not match process/cached memory identity.");
    Console.WriteLine($"Native compression store: {compressionValue.ResidentBytes} resident bytes; PID {compressionValue.ProcessId}.");
}
if (extendedMetrics.CpuFrequencyMhz is not null && extendedMetrics.ProcessorFrequencies is not { Count: > 0 })
    throw new InvalidOperationException("Aggregate CPU frequency is present but per-processor data was lost.");
Console.WriteLine($"Per-processor Windows frequency estimates: {extendedMetrics.ProcessorFrequencies?.Count ?? 0} logical processors.");
Console.WriteLine(
    $"Extended metrics: SENSOR {MetricFormatter.Temperature(extendedMetrics.CpuTemperatureCelsius)}, " +
    $"CPU {MetricFormatter.Temperature(extendedMetrics.CpuTemperatureCelsius)} " +
    $"at {MetricFormatter.Frequency(extendedMetrics.CpuFrequencyMhz)}, " +
    $"{extendedMetrics.CpuUsage.PerformanceCoreCount ?? 0}P/{extendedMetrics.CpuUsage.EfficiencyCoreCount ?? 0}E, " +
    $"{extendedMetrics.CpuUsage.LogicalProcessorCount ?? extendedMetrics.CpuCorePercents.Count} logical processors, " +
    $"GPU {MetricFormatter.Temperature(extendedMetrics.GpuTemperatureCelsius)} / " +
    $"{MetricFormatter.Power(extendedMetrics.GpuPowerWatts)}, " +
    $"VRAM {MetricFormatter.MemoryMb(extendedMetrics.GpuMemoryUsedMb)}.");
Console.WriteLine(
    $"Battery details: health {MetricFormatter.Percent(extendedMetrics.BatteryHealthPercent)}, " +
    $"cycles {(extendedMetrics.BatteryCycleCount?.ToString() ?? "--")}, " +
    $"voltage {(extendedMetrics.BatteryVoltageVolts is null ? "--" : $"{extendedMetrics.BatteryVoltageVolts:0.00}V")}.");
Console.WriteLine($"Bluetooth connected devices: {extendedMetrics.BluetoothConnectedDeviceCount?.ToString("0") ?? "--"}.");
Console.WriteLine(
    $"Hardware sensors: {extendedMetrics.Sensors.Count} total; " +
    string.Join(", ", extendedMetrics.Sensors.GroupBy(item => item.Type).Select(group => $"{group.Key} {group.Count()}")));
foreach (var sensor in extendedMetrics.Sensors.Where(item => item.Type is "Temperature" or "Voltage" or "Current" or "Power" or "Energy"))
{
    Console.WriteLine($"  {sensor.Type}: {sensor.Hardware} / {sensor.Name} = {sensor.Value:0.###} {sensor.Unit}");
}
if (extendedMetrics.Sensors.Any(item => item.Identifier.Contains("/windows/energy-meter/rapl_") && item.Type == "Power") &&
    !extendedMetrics.Sensors.Any(item => item.Identifier.EndsWith("/session-energy")))
    throw new InvalidOperationException("Live RAPL counters did not produce any session energy readings after warm-up.");
foreach (var nativeGpu in extendedMetrics.NvidiaDevices ?? [])
{
    foreach (var field in (nativeGpu.SensorFields ?? []).Where(field => field.Value is not null))
        if (!extendedMetrics.Sensors.Any(sensor => sensor.Hardware == nativeGpu.Name && sensor.Name == field.Name &&
            sensor.Unit == field.Unit && Math.Abs(sensor.Value - field.Value!.Value) < 0.000001))
            throw new InvalidOperationException("A readable native GPU field did not reach the shared sensor model.");
    if (nativeGpu.SensorFields?.Any(field => field.FieldId == 185 && field.Value is not null) == true &&
        extendedMetrics.Sensors.Any(sensor => sensor.Hardware == nativeGpu.Name && sensor.Type == "Power" && sensor.Name == "GPU Package"))
        throw new InvalidOperationException("Ambiguous duplicate GPU package power remained beside explicit one-second average.");
    if (nativeGpu.TotalEnergyMilliJoules > 0 &&
        !extendedMetrics.Sensors.Any(item => item.Type == "Energy" && item.Hardware == nativeGpu.Name))
        throw new InvalidOperationException("A live GPU energy counter did not reach the sensor model after warm-up.");
}

var defaultMetrics = StatsMetricCatalog.Defaults();
if (defaultMetrics.Count(metric => metric.IsEnabled) != 4 ||
    defaultMetrics.Skip(4).Any(metric => metric.IsEnabled))
{
    throw new InvalidOperationException("Only the four legacy Stats metrics should be enabled by default.");
}

Console.WriteLine($"Passed configurable Stats catalog check ({defaultMetrics.Count} metrics, 4 enabled by default).");
if (MetricDisplayStyles.ToProtocolValue(MetricDisplayStyles.Stacked) != 0 ||
    MetricDisplayStyles.ToProtocolValue(MetricDisplayStyles.Inline) != 1 ||
    MetricDisplayStyles.ToProtocolValue(MetricDisplayStyles.ValueOnly) != 2 ||
    MetricDisplayStyles.ToProtocolValue(MetricDisplayStyles.Chart) != 3)
{
    throw new InvalidOperationException("Metric display-style protocol mapping is invalid.");
}
Console.WriteLine("Passed four menu-bar display-style protocol checks.");

if (MetricColors.Normalize("#ff112233", "#FF000000") != "#FF112233" ||
    MetricColors.Normalize("not-a-color", "#FF65D5FF") != "#FF65D5FF" ||
    MetricColors.ToProtocolArgb("#FF65D5FF") != 0xFF65D5FFU ||
    MetricColors.Next(MetricColors.Palette[^1]) != MetricColors.Palette[0])
{
    throw new InvalidOperationException("Metric color normalization or palette cycling is invalid.");
}
if (SharedMetricsLayout.Version != 7 || SharedMetricsLayout.ColorOffset != 216 || SharedMetricsLayout.Capacity != 320)
{
    throw new InvalidOperationException("Shared metrics v6 color layout is invalid.");
}
Console.WriteLine("Passed per-metric color palette and v7 protocol checks.");

if (MetricAnchorLayout.Version != 1 || MetricAnchorLayout.RectanglesOffset != 24 ||
    MetricAnchorLayout.RectangleSize != 16 || MetricAnchorLayout.Capacity != 256)
{
    throw new InvalidOperationException("Metric anchor v1 layout is invalid.");
}
var popupWorkArea = new ScreenRect(0, 28, 1920, 1080);
var cpuAnchor = new ScreenRect(1380, 1, 1426, 27);
var cpuPlacement = PopupPlacement.Calculate(cpuAnchor, popupWorkArea, 291, 690);
if (cpuPlacement.Top != 33 || Math.Abs((cpuPlacement.Left + 291 / 2) - cpuAnchor.CenterX) > 1)
{
    throw new InvalidOperationException("Popup was not centered directly below its metric anchor.");
}
var movedPlacement = PopupPlacement.Calculate(
    new ScreenRect(cpuAnchor.Left - 120, cpuAnchor.Top, cpuAnchor.Right - 120, cpuAnchor.Bottom),
    popupWorkArea,
    291,
    690);
if (movedPlacement.Left != cpuPlacement.Left - 120 || movedPlacement.Top != cpuPlacement.Top)
{
    throw new InvalidOperationException("Popup placement did not follow a moved metric anchor.");
}
var rightEdgePlacement = PopupPlacement.Calculate(new ScreenRect(1890, 1, 1918, 27), popupWorkArea, 291, 690);
if (rightEdgePlacement.Left + 291 > popupWorkArea.Right - 8)
{
    throw new InvalidOperationException("Popup placement escaped the monitor work area.");
}
Console.WriteLine("Passed metric-anchor contract, centering, tracking, and edge-clamping checks.");

var liveHub = new LiveMetricsHub();
var liveUpdates = 0;
liveHub.MetricsUpdated += _ => liveUpdates++;
liveHub.Publish(Sample(cpu: 10), 2);
liveHub.Publish(Sample(cpu: 20), 2);
liveHub.Publish(Sample(cpu: 30), 2);
var cpuHistory = liveHub.GetHistory(MetricIds.Cpu);
if (cpuHistory.Count != 2 || cpuHistory[0] != 20 || cpuHistory[1] != 30 || liveUpdates != 3)
{
    throw new InvalidOperationException("Live metric history did not retain the latest bounded samples.");
}

using (liveHub.BeginModuleSession(MetricIds.CpuTemperature))
{
    if (!liveHub.NeedsPlatformMetrics || liveHub.NeedsNvidiaExtendedMetrics)
    {
        throw new InvalidOperationException("CPU session did not request only platform metrics.");
    }
}
using (liveHub.BeginModuleSession(MetricIds.GpuPower))
{
    if (!liveHub.NeedsNvidiaExtendedMetrics || liveHub.NeedsPlatformMetrics)
    {
        throw new InvalidOperationException("GPU session did not request only NVIDIA extended metrics.");
    }
}
using (liveHub.BeginModuleSession(MetricIds.Bluetooth))
{
    if (!liveHub.NeedsBluetoothMetrics)
    {
        throw new InvalidOperationException("Bluetooth session did not request Bluetooth metrics.");
    }
}
if (liveHub.NeedsPlatformMetrics || liveHub.NeedsNvidiaExtendedMetrics || liveHub.NeedsBluetoothMetrics)
{
    throw new InvalidOperationException("Module sessions did not release their on-demand readers.");
}
Console.WriteLine("Passed warm live-metrics cache and on-demand sensor checks.");

var computerInfo = ComputerInfoService.Capture();
if (string.IsNullOrWhiteSpace(computerInfo.Name) ||
    string.IsNullOrWhiteSpace(computerInfo.OperatingSystem) ||
    string.IsNullOrWhiteSpace(computerInfo.Processor) ||
    string.IsNullOrWhiteSpace(computerInfo.Memory))
{
    throw new InvalidOperationException("Computer overview did not return its required identity fields.");
}
Console.WriteLine($"Computer overview: {computerInfo.Name} · {computerInfo.Processor} · {computerInfo.Memory}.");

Exception? uiFailure = null;
var uiThread = new Thread(() =>
{
    try
    {
        var sensorHub = new LiveMetricsHub();
        var sensorSample = Sample(cpu: 10) with
        {
            Sensors = [
                new("avg", "Windows RAPL", "Cpu", "CPU Package · Session Average", "Power", 45, "W"),
                new("pkg", "Windows RAPL", "Cpu", "CPU Package", "Power", 20, "W"),
                new("energy", "Windows RAPL", "Cpu", "CPU Package · Session Energy", "Energy", 12, "mWh")]
        };
        sensorHub.Publish(sensorSample, 10);
        using (var sensorViewModel = new ModulePopupViewModel(MetricIds.Power, liveMetrics: sensorHub))
        {
            sensorViewModel.Start();
            if (!sensorViewModel.Details.Any(item => item.Label == "DC-in 功率") ||
                !sensorViewModel.Details.Any(item => item.Label == "DC-in 电压") ||
                !sensorViewModel.Details.Any(item => item.Label.Contains("本次耗能")))
                throw new InvalidOperationException("Package readings hid missing DC-in fields or session energy.");
            if (SensorMetricSelector.PreferredPowerWatts(sensorSample) != 20)
                throw new InvalidOperationException("Session average replaced instantaneous headline power.");
        }

        var frequencyHub = new LiveMetricsHub();
        frequencyHub.Publish(Sample(20) with { ProcessorFrequencies = [new(0, 0, 2304, 150, 3456), new(1, 2, 2304, 0, 0)] }, 10);
        using (var frequencyViewModel = new ModulePopupViewModel(MetricIds.Cpu, liveMetrics: frequencyHub))
        {
            frequencyViewModel.Start();
            if (!frequencyViewModel.Details.Any(item => item.IsHeader && item.Label == "逻辑处理器频率 · 系统估算") ||
                !frequencyViewModel.Details.Any(item => item.Label == "处理器 0:0" && item.Value == "3.46 GHz") ||
                !frequencyViewModel.Details.Any(item => item.Label == "处理器 1:2" && item.Value == "0.00 GHz"))
                throw new InvalidOperationException("Per-processor frequency UI lost estimate labels, processor groups, or valid zero.");
        }
        var resourceHub = new LiveMetricsHub();
        resourceHub.Publish(Sample(20) with
        {
            ProcessResources = new([new(123, "Example", 25, 200UL * 1024 * 1024, 150UL * 1024 * 1024)], []),
            GpuEngines = new([new("00000000:00000001", 0, 0, "3D", 0)], [new("00000000:00000001", "Example GPU", 1, 2, false)], [])
        }, 10);
        using (var cpuResources = new ModulePopupViewModel(MetricIds.Cpu, liveMetrics: resourceHub))
        using (var ramResources = new ModulePopupViewModel(MetricIds.Ram, liveMetrics: resourceHub))
        using (var gpuResources = new ModulePopupViewModel(MetricIds.Gpu, liveMetrics: resourceHub))
        {
            cpuResources.Start(); ramResources.Start(); gpuResources.Start();
            if (!cpuResources.Processes.Any(process => process.Name == "Example · 123" && process.Value == "25.0%") ||
                !ramResources.Processes.Any(process => process.Name == "Example · 123" && process.Value == "150 MB") ||
                !gpuResources.Details.Any(item => item.Label == "3D #0" && item.Value == "0 %") || !resourceHub.NeedsProcessMetrics)
                throw new InvalidOperationException("Shared process cache, private working-set UI, GPU idle zero, or collector interest was lost.");
        }
        if (resourceHub.NeedsProcessMetrics) throw new InvalidOperationException("Process collector interest remained after popups closed.");
        foreach (var module in new[] { MetricIds.Cpu, MetricIds.Gpu, MetricIds.Ram, MetricIds.Disk, MetricIds.NetworkDownload, MetricIds.Battery, MetricIds.Bluetooth, MetricIds.Power })
        {
            var popup = new ModulePopupWindow(module) { Opacity = 0, ShowActivated = false };
            popup.Show();
            popup.UpdateLayout();
            popup.Close();
        }

        using var settingsViewModel = new MainViewModel(new SystemStatusService());
        var settings = new StatsSettingsWindow(settingsViewModel) { Opacity = 0, ShowActivated = false };
        settings.Show();
        settings.UpdateLayout();
        settings.Close();

        var alert = new StatsAlertWindow(new StatsAlertNotification("cpu", "测试警报", 95, "CPU 已超过阈值"))
        {
            Opacity = 0,
            ShowActivated = false
        };
        alert.Show();
        alert.UpdateLayout();
        alert.Close();
    }
    catch (Exception exception)
    {
        uiFailure = exception;
    }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (uiFailure is not null)
{
    throw new InvalidOperationException("WPF window smoke test failed.", uiFailure);
}

Console.WriteLine("Passed popup and settings WPF smoke tests (8 modules plus alerts).");

static HardwareMetrics Sample(double cpu) => new(
    CpuPercent: cpu,
    GpuPercent: 12,
    MemoryPercent: 50,
    PowerWatts: 8,
    CpuTemperatureCelsius: 65,
    CpuFrequencyMhz: 3800,
    GpuTemperatureCelsius: 55,
    GpuPowerWatts: 20,
    GpuMemoryUsedMb: 1024,
    GpuMemoryTotalMb: 6144,
    MemoryUsedBytes: 16UL * 1024 * 1024 * 1024,
    MemoryTotalBytes: 32UL * 1024 * 1024 * 1024,
    BatteryPercent: 80,
    DiskUsedPercent: 60,
    DiskUsedBytes: 600,
    DiskTotalBytes: 1000,
    NetworkDownloadBytesPerSecond: 1024,
    NetworkUploadBytesPerSecond: 512,
    BatteryHealthPercent: 98,
    BatteryCycleCount: 50,
    BatteryVoltageVolts: 15.4,
    BatteryDesignCapacityMwh: 90000,
    BatteryFullChargeCapacityMwh: 88000,
    BluetoothConnectedDeviceCount: 1,
    CpuCorePercents: [cpu],
    CpuUsage: new CpuUsageBreakdown(30, 20, 50, 0.1, 0.2, 0.3, 8, 16, 8, 0),
    MemoryBreakdown: new MemoryBreakdown(16UL * 1024 * 1024 * 1024, 1024, 2048, 4096, 512, 256, null, 128, 1024),
    Sensors: []);
