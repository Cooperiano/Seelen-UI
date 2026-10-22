using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

public sealed partial class HardwareMetricsReader : IHardwareMetricsReader, IDisposable
{
    private FileTime _previousIdle;
    private FileTime _previousKernel;
    private FileTime _previousUser;
    private bool _hasCpuSample;
    private double? _cachedGpuPercent;
    private DateTimeOffset _lastGpuRead = DateTimeOffset.MinValue;
    private readonly WindowsGpuEngineInterface _gpuEngineReader = new();
    private GpuEngineCapture _cachedGpuEngines = new([], [], []);
    private readonly WindowsProcessTelemetryInterface _processReader = new();
    private ProcessResourceCapture _cachedProcesses = new([], []);
    private DateTimeOffset _lastProcessRead = DateTimeOffset.MinValue;
    private NvidiaMetrics _cachedNvidia = new(null, null, null, null);
    private DateTimeOffset _lastNvidiaRead = DateTimeOffset.MinValue;
    private double? _cachedCpuTemperature;
    private double? _cachedCpuFrequency;
    private IReadOnlyList<ProcessorFrequency> _cachedProcessorFrequencies = [];
    private DateTimeOffset _lastPlatformRead = DateTimeOffset.MinValue;
    private IReadOnlyList<double> _cachedCpuCorePercents = [];
    private long? _previousNetworkReceived;
    private long? _previousNetworkSent;
    private DateTimeOffset _previousNetworkRead = DateTimeOffset.MinValue;
    private BatteryDetails _cachedBatteryDetails = new(null, null, null, null, null);
    private DateTimeOffset _lastBatteryDetailsRead = DateTimeOffset.MinValue;
    private readonly BluetoothMetricsReader _bluetoothReader = new();
    private double? _cachedBluetoothConnectedCount;
    private DateTimeOffset _lastBluetoothRead = DateTimeOffset.MinValue;
    private readonly HardwareSensorReader _sensorReader = new();
    private IReadOnlyList<HardwareSensorReading> _cachedSensors = [];
    private DateTimeOffset _lastSensorRead = DateTimeOffset.MinValue;
    private MemoryBreakdown _cachedMemoryBreakdown = EmptyMemoryBreakdown();
    private MemoryCompressionCapture? _cachedCompression;
    private DateTimeOffset _lastMemoryDetailsRead = DateTimeOffset.MinValue;
    private double? _loadAverage1Minute;
    private double? _loadAverage5Minutes;
    private double? _loadAverage15Minutes;
    private DateTimeOffset _lastLoadAverageRead = DateTimeOffset.MinValue;
    private CpuTopology _cpuTopology = new(null, null, null, null);

    public bool ReadPlatformMetrics { get; set; }
    public bool ReadNvidiaExtendedMetrics { get; set; }
    public bool ReadBluetoothMetrics { get; set; }
    public bool ReadMemoryDetails { get; set; }
    public bool ReadSensorMetrics { get; set; }
    public bool ReadProcessMetrics { get; set; }

    public HardwareMetrics Capture()
    {
        var cpu = ReadCpuUsage();
        var memory = ReadMemory();
        if (ReadPlatformMetrics)
        {
            ReadPlatformMetricsCached();
        }

        var sensors = ReadSensorMetrics
            ? AddPlatformSensorFallbacks(ReadSensorsCached())
            : [];
        var nvidia = ReadNvidiaExtendedMetrics
            ? ReadNvidiaMetricsCached()
            : new NvidiaMetrics(null, null, null, null);
        var battery = ReadBattery();
        var disk = ReadDisk();
        var network = ReadNetwork();
        var batteryDetails = ReadBatteryDetailsCached();
        var bluetoothCount = ReadBluetoothMetrics ? ReadBluetoothConnectedCountCached() : null;
        var memoryBreakdown = ReadMemoryDetails ? ReadMemoryBreakdownCached() : EmptyMemoryBreakdown();
        var processes = ReadProcessMetrics ? ReadProcessesCached() : _cachedProcesses;
        return new HardwareMetrics(
            cpu.TotalPercent,
            ReadGpuPercentCached(),
            memory.Percent,
            ReadBatteryPowerWatts(),
            ReadPlatformMetrics ? _cachedCpuTemperature : null,
            ReadPlatformMetrics ? _cachedCpuFrequency : null,
            nvidia.TemperatureCelsius,
            nvidia.PowerWatts,
            nvidia.MemoryUsedMb,
            nvidia.MemoryTotalMb,
            memory.UsedBytes,
            memory.TotalBytes,
            battery.Percent,
            disk.Percent,
            disk.UsedBytes,
            disk.TotalBytes,
            network.DownloadBytesPerSecond,
            network.UploadBytesPerSecond,
            batteryDetails.HealthPercent,
            batteryDetails.CycleCount,
            batteryDetails.VoltageVolts,
            batteryDetails.DesignCapacityMwh,
            batteryDetails.FullChargeCapacityMwh,
            bluetoothCount,
            ReadPlatformMetrics ? _cachedCpuCorePercents : [],
            new CpuUsageBreakdown(
                cpu.UserPercent,
                cpu.SystemPercent,
                cpu.IdlePercent,
                ReadPlatformMetrics ? _loadAverage1Minute : null,
                ReadPlatformMetrics ? _loadAverage5Minutes : null,
                ReadPlatformMetrics ? _loadAverage15Minutes : null,
                ReadPlatformMetrics ? _cpuTopology.PhysicalCoreCount : null,
                ReadPlatformMetrics ? _cpuTopology.LogicalProcessorCount : null,
                ReadPlatformMetrics ? _cpuTopology.PerformanceCoreCount : null,
                ReadPlatformMetrics ? _cpuTopology.EfficiencyCoreCount : null),
            memoryBreakdown,
            sensors,
            ReadSensorMetrics ? _sensorReader.NvidiaDevices : [],
            ReadPlatformMetrics ? _cachedProcessorFrequencies : [],
            processes, _cachedGpuEngines, ReadMemoryDetails ? _cachedCompression : null);
    }

    private IReadOnlyList<HardwareSensorReading> AddPlatformSensorFallbacks(
        IReadOnlyList<HardwareSensorReading> sensors)
    {
        if (_cachedCpuTemperature is not { } cpuTemperature ||
            !double.IsFinite(cpuTemperature) ||
            cpuTemperature is <= -20 or >= 150 ||
            sensors.Any(sensor =>
                sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase) &&
                sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)))
        {
            return sensors;
        }

        var combined = sensors.ToList();
        combined.Add(new HardwareSensorReading(
            "/windows/thermal-zone/cpu",
            "Windows Thermal Zone",
            "Platform",
            "CPU Temperature",
            "Temperature",
            cpuTemperature,
            "°C"));
        return combined;
    }

    public void Dispose() => _sensorReader.Dispose();

    private void ReadPlatformMetricsCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPlatformRead < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastPlatformRead = now;
        _cachedCpuTemperature = ReadCpuTemperature();
        (_cachedCpuFrequency, _cachedProcessorFrequencies) = ReadCpuFrequencies();
        _cachedCpuCorePercents = ReadCpuCorePercents();
        _cpuTopology = ReadCpuTopology();
        UpdateLoadAverages(now, ReadProcessorQueueLength());
    }

    private static IReadOnlyList<double> ReadCpuCorePercents()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor");
            using var results = searcher.Get();
            var cores = new List<(int Index, double Value)>();
            foreach (ManagementBaseObject processor in results)
            {
                using (processor)
                {
                    var name = Convert.ToString(processor["Name"], CultureInfo.InvariantCulture);
                    if (!int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    {
                        continue;
                    }
                    var value = Convert.ToDouble(processor["PercentProcessorTime"], CultureInfo.InvariantCulture);
                    cores.Add((index, Math.Clamp(value, 0, 100)));
                }
            }
            return cores.OrderBy(item => item.Index).Select(item => item.Value).ToList();
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException)
        {
            return [];
        }
    }

    private static double? ReadCpuTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            using var results = searcher.Get();
            var temperatures = new List<double>();
            foreach (ManagementBaseObject zone in results)
            {
                using (zone)
                {
                    var kelvinTenths = Convert.ToDouble(zone["HighPrecisionTemperature"], CultureInfo.InvariantCulture);
                    var celsius = kelvinTenths / 10d - 273.15d;
                    if (celsius is > -20 and < 150)
                    {
                        temperatures.Add(celsius);
                    }
                }
            }

            return temperatures.Count == 0 ? null : temperatures.Max();
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static (double? Total, IReadOnlyList<ProcessorFrequency> Processors) ReadCpuFrequencies()
    {
        double? total = null;
        var processors = new List<ProcessorFrequency>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT Name, ProcessorFrequency, PercentProcessorPerformance " +
                "FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
            using var results = searcher.Get();
            foreach (ManagementBaseObject processor in results)
            {
                using (processor)
                {
                    if (processor["ProcessorFrequency"] is null || processor["PercentProcessorPerformance"] is null) continue;
                    var name = processor["Name"]?.ToString() ?? "";
                    var baseFrequency = Convert.ToDouble(processor["ProcessorFrequency"], CultureInfo.InvariantCulture);
                    var performance = Convert.ToDouble(processor["PercentProcessorPerformance"], CultureInfo.InvariantCulture);
                    if (name == "_Total")
                    {
                        var frequency = baseFrequency * performance / 100d;
                        if (baseFrequency > 0 && performance >= 0 && double.IsFinite(frequency) && frequency <= 20_000) total = frequency;
                    }
                    else if (ProcessorFrequencyParser.TryParse(name, baseFrequency, performance, out var value)) processors.Add(value!);
                }
            }
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException)
        {
            return (null, []);
        }

        return (total, processors.OrderBy(value => value.Group).ThenBy(value => value.Processor).ToList());
    }

    private NvidiaMetrics ReadNvidiaMetricsCached()
    {
        // Reuse the just-captured native snapshot instead of launching nvidia-smi as a second collector.
        if (ReadSensorMetrics && _sensorReader.NvidiaDevices.FirstOrDefault() is
            { TemperatureCelsius: { } temperature, PowerWatts: { } power,
              MemoryUsedBytes: { } used, MemoryTotalBytes: { } total })
        {
            _cachedNvidia = new(temperature, power, used / 1024d / 1024d, total / 1024d / 1024d);
            _lastNvidiaRead = DateTimeOffset.UtcNow;
            return _cachedNvidia;
        }
        var now = DateTimeOffset.UtcNow;
        if (now - _lastNvidiaRead < TimeSpan.FromSeconds(5))
        {
            return _cachedNvidia;
        }

        _lastNvidiaRead = now;
        _cachedNvidia = ReadNvidiaMetrics();
        return _cachedNvidia;
    }

    private static NvidiaMetrics ReadNvidiaMetrics()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=temperature.gpu,power.draw,memory.used,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return new(null, null, null, null);
            }

            var output = process.StandardOutput.ReadLine();
            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
                return new(null, null, null, null);
            }

            var values = output?.Split(',', StringSplitOptions.TrimEntries);
            if (values is null || values.Length < 4)
            {
                return new(null, null, null, null);
            }

            return new(
                ParseMetric(values[0]),
                ParseMetric(values[1]),
                ParseMetric(values[2]),
                ParseMetric(values[3]));
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or FormatException)
        {
            return new(null, null, null, null);
        }
    }

    private static double? ParseMetric(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private CpuUsage ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return new(null, null, null, null);
        }

        if (!_hasCpuSample)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasCpuSample = true;
            return new(null, null, null, null);
        }

        var idleDelta = idle.ToUInt64() - _previousIdle.ToUInt64();
        var kernelDelta = kernel.ToUInt64() - _previousKernel.ToUInt64();
        var userDelta = user.ToUInt64() - _previousUser.ToUInt64();
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var total = kernelDelta + userDelta;
        if (total == 0)
        {
            return new(null, null, null, null);
        }

        var idlePercent = Math.Clamp(idleDelta * 100d / total, 0d, 100d);
        var userPercent = Math.Clamp(userDelta * 100d / total, 0d, 100d);
        var systemPercent = Math.Clamp((kernelDelta - idleDelta) * 100d / total, 0d, 100d);
        return new(100d - idlePercent, userPercent, systemPercent, idlePercent);
    }

    private void UpdateLoadAverages(DateTimeOffset now, double? queueLength)
    {
        if (queueLength is null)
        {
            return;
        }

        var normalized = queueLength.Value / Math.Max(1, Environment.ProcessorCount);
        var elapsed = _lastLoadAverageRead == DateTimeOffset.MinValue
            ? 5d
            : Math.Max(0.1, (now - _lastLoadAverageRead).TotalSeconds);
        _lastLoadAverageRead = now;
        _loadAverage1Minute = ExponentialAverage(_loadAverage1Minute, normalized, elapsed, 60);
        _loadAverage5Minutes = ExponentialAverage(_loadAverage5Minutes, normalized, elapsed, 300);
        _loadAverage15Minutes = ExponentialAverage(_loadAverage15Minutes, normalized, elapsed, 900);
    }

    private static double ExponentialAverage(double? previous, double value, double elapsedSeconds, double periodSeconds)
    {
        if (previous is null)
        {
            return value;
        }
        var alpha = 1d - Math.Exp(-elapsedSeconds / periodSeconds);
        return previous.Value + alpha * (value - previous.Value);
    }

    private static double? ReadProcessorQueueLength()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT ProcessorQueueLength FROM Win32_PerfFormattedData_PerfOS_System");
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    return Convert.ToDouble(item["ProcessorQueueLength"], CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException)
        {
        }
        return null;
    }

    private static CpuTopology ReadCpuTopology()
    {
        if (!GetSystemCpuSetInformation(nint.Zero, 0, out var required, nint.Zero, 0) && required == 0)
        {
            return new(null, Environment.ProcessorCount, null, null);
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (!GetSystemCpuSetInformation(buffer, required, out var returned, nint.Zero, 0))
            {
                return new(null, Environment.ProcessorCount, null, null);
            }

            var returnedLength = (int)returned;
            var cores = new Dictionary<(ushort Group, byte Core), byte>();
            var logical = 0;
            for (var offset = 0; offset + 20 <= returnedLength;)
            {
                var size = Marshal.ReadInt32(buffer, offset);
                if (size < 20 || offset + size > returnedLength)
                {
                    break;
                }
                var type = Marshal.ReadInt32(buffer, offset + 4);
                if (type == 0)
                {
                    var group = unchecked((ushort)Marshal.ReadInt16(buffer, offset + 12));
                    var core = Marshal.ReadByte(buffer, offset + 15);
                    var efficiency = Marshal.ReadByte(buffer, offset + 18);
                    cores[(group, core)] = Math.Max(cores.GetValueOrDefault((group, core)), efficiency);
                    logical++;
                }
                offset += size;
            }

            if (cores.Count == 0)
            {
                return new(null, logical > 0 ? logical : Environment.ProcessorCount, null, null);
            }
            var classes = cores.Values.Distinct().Order().ToList();
            if (classes.Count == 1)
            {
                return new(cores.Count, logical, cores.Count, 0);
            }
            var performanceClass = classes[^1];
            var performance = cores.Values.Count(value => value == performanceClass);
            return new(cores.Count, logical, performance, cores.Count - performance);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private MemoryBreakdown ReadMemoryBreakdownCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastMemoryDetailsRead < TimeSpan.FromSeconds(5))
        {
            return _cachedMemoryBreakdown;
        }
        _lastMemoryDetailsRead = now;
        _cachedMemoryBreakdown = ReadMemoryBreakdown();
        _cachedCompression = WindowsMemoryCompressionInterface.Capture();
        _cachedMemoryBreakdown = _cachedMemoryBreakdown with { CompressedBytes = _cachedCompression.Value?.ResidentBytes };
        return _cachedMemoryBreakdown;
    }

    private static MemoryBreakdown ReadMemoryBreakdown()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT AvailableBytes, CacheBytes, CommittedBytes, CommitLimit, PoolPagedBytes, PoolNonpagedBytes " +
                "FROM Win32_PerfFormattedData_PerfOS_Memory");
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var swap = ReadSwapUsage();
                    return new MemoryBreakdown(
                        ReadUInt64(item, "AvailableBytes"),
                        ReadUInt64(item, "CacheBytes"),
                        ReadUInt64(item, "CommittedBytes"),
                        ReadUInt64(item, "CommitLimit"),
                        ReadUInt64(item, "PoolPagedBytes"),
                        ReadUInt64(item, "PoolNonpagedBytes"),
                        null,
                        swap.UsedBytes,
                        swap.TotalBytes);
                }
            }
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException)
        {
        }
        return EmptyMemoryBreakdown();
    }

    private static ulong? ReadUInt64(ManagementBaseObject item, string propertyName)
    {
        var value = item[propertyName];
        return value is null ? null : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
    }

    private static (ulong? UsedBytes, ulong? TotalBytes) ReadSwapUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "ROOT\\CIMV2",
                "SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
            using var results = searcher.Get();
            ulong usedMb = 0;
            ulong totalMb = 0;
            var found = false;
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    usedMb += ReadUInt64(item, "CurrentUsage") ?? 0;
                    totalMb += ReadUInt64(item, "AllocatedBaseSize") ?? 0;
                    found = true;
                }
            }
            const ulong megabyte = 1024UL * 1024UL;
            return found ? (usedMb * megabyte, totalMb * megabyte) : (null, null);
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return (null, null);
        }
    }

    private IReadOnlyList<HardwareSensorReading> ReadSensorsCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSensorRead < TimeSpan.FromSeconds(5))
        {
            return _cachedSensors;
        }
        _lastSensorRead = now;
        _cachedSensors = _sensorReader.Capture();
        return _cachedSensors;
    }

    private static MemoryBreakdown EmptyMemoryBreakdown() => new(null, null, null, null, null, null, null, null, null);

    private static MemoryMetrics ReadMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(ref status)
            ? new MemoryMetrics(status.MemoryLoad, status.TotalPhysical - status.AvailablePhysical, status.TotalPhysical)
            : new MemoryMetrics(null, null, null);
    }

    private double? ReadGpuPercentCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastGpuRead < TimeSpan.FromSeconds(2))
        {
            return _cachedGpuPercent;
        }

        _lastGpuRead = now;
        _cachedGpuEngines = _gpuEngineReader.Capture();
        _cachedGpuPercent = _cachedGpuEngines.Engines.Count == 0 ? null : _cachedGpuEngines.Engines.Max(item => item.UtilizationPercent);
        return _cachedGpuPercent;
    }

    private ProcessResourceCapture ReadProcessesCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastProcessRead < TimeSpan.FromSeconds(5)) return _cachedProcesses;
        _lastProcessRead = now;
        return _cachedProcesses = _processReader.Capture();
    }

    private static double? ReadBatteryPowerWatts()
    {
        var state = new SystemBatteryState();
        var status = CallNtPowerInformation(
            SystemBatteryStateInformation,
            nint.Zero,
            0,
            ref state,
            (uint)Marshal.SizeOf<SystemBatteryState>());

        if (status != 0 || state.BatteryPresent == 0 || state.Rate == 0)
        {
            return null;
        }

        return Math.Abs(state.Rate) / 1000d;
    }

    private static BatteryMetrics ReadBattery()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryLifePercent == byte.MaxValue)
        {
            return new BatteryMetrics(null);
        }

        return new BatteryMetrics(status.BatteryLifePercent);
    }

    private BatteryDetails ReadBatteryDetailsCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastBatteryDetailsRead < TimeSpan.FromSeconds(30))
        {
            return _cachedBatteryDetails;
        }

        _lastBatteryDetailsRead = now;
        var designed = ReadWmiUnsigned("BatteryStaticData", "DesignedCapacity");
        var full = ReadWmiUnsigned("BatteryFullChargedCapacity", "FullChargedCapacity");
        var cycles = ReadWmiUnsigned("BatteryCycleCount", "CycleCount");
        var voltageMillivolts = ReadWmiUnsigned("BatteryStatus", "Voltage");
        _cachedBatteryDetails = new BatteryDetails(
            designed is > 0 && full is not null ? Math.Clamp(full.Value * 100d / designed.Value, 0, 200) : null,
            cycles is null ? null : (uint)Math.Min(cycles.Value, uint.MaxValue),
            voltageMillivolts is null ? null : voltageMillivolts.Value / 1000d,
            designed,
            full);
        return _cachedBatteryDetails;
    }

    private static ulong? ReadWmiUnsigned(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("ROOT\\WMI", $"SELECT {propertyName} FROM {className}");
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    var value = item[propertyName];
                    return value is null ? null : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException)
        {
        }

        return null;
    }

    private double? ReadBluetoothConnectedCountCached()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastBluetoothRead < TimeSpan.FromSeconds(5))
        {
            return _cachedBluetoothConnectedCount;
        }
        _lastBluetoothRead = now;
        _cachedBluetoothConnectedCount = _bluetoothReader.Capture().Count(item => item.IsConnected);
        return _cachedBluetoothConnectedCount;
    }

    private static DiskMetrics ReadDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (root is null)
            {
                return new DiskMetrics(null, null, null);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return new DiskMetrics(null, null, null);
            }

            var total = (ulong)drive.TotalSize;
            var used = total - (ulong)drive.AvailableFreeSpace;
            return new DiskMetrics(used * 100d / total, used, total);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DiskMetrics(null, null, null);
        }
    }

    private NetworkMetrics ReadNetwork()
    {
        try
        {
            long received = 0;
            long sent = 0;
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var statistics = networkInterface.GetIPStatistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _previousNetworkRead).TotalSeconds;
            var result = _previousNetworkReceived is not null && _previousNetworkSent is not null && elapsed > 0
                ? new NetworkMetrics(
                    Math.Max(0, received - _previousNetworkReceived.Value) / elapsed,
                    Math.Max(0, sent - _previousNetworkSent.Value) / elapsed)
                : new NetworkMetrics(null, null);
            _previousNetworkReceived = received;
            _previousNetworkSent = sent;
            _previousNetworkRead = now;
            return result;
        }
        catch (NetworkInformationException)
        {
            return new NetworkMetrics(null, null);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(
        nint information,
        uint bufferLength,
        out uint returnedLength,
        nint process,
        uint flags);

    private const int SystemBatteryStateInformation = 5;

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel,
        nint inputBuffer,
        uint inputBufferSize,
        ref SystemBatteryState outputBuffer,
        uint outputBufferSize);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemBatteryState
    {
        public byte AcOnLine;
        public byte BatteryPresent;
        public byte Charging;
        public byte Discharging;
        public uint Spare;
        public uint MaxCapacity;
        public uint RemainingCapacity;
        public int Rate;
        public uint EstimatedTime;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private sealed record NvidiaMetrics(
        double? TemperatureCelsius,
        double? PowerWatts,
        double? MemoryUsedMb,
        double? MemoryTotalMb);

    private sealed record MemoryMetrics(double? Percent, ulong? UsedBytes, ulong? TotalBytes);
    private sealed record CpuUsage(
        double? TotalPercent,
        double? UserPercent,
        double? SystemPercent,
        double? IdlePercent);
    private sealed record CpuTopology(
        int? PhysicalCoreCount,
        int? LogicalProcessorCount,
        int? PerformanceCoreCount,
        int? EfficiencyCoreCount);
    private sealed record BatteryMetrics(double? Percent);
    private sealed record DiskMetrics(double? Percent, ulong? UsedBytes, ulong? TotalBytes);
    private sealed record NetworkMetrics(double? DownloadBytesPerSecond, double? UploadBytesPerSecond);
    private sealed record BatteryDetails(
        double? HealthPercent,
        uint? CycleCount,
        double? VoltageVolts,
        ulong? DesignCapacityMwh,
        ulong? FullChargeCapacityMwh);
}
