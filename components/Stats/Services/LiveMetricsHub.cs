using HardwareTelemetry.Models;
using Monitoring.Contracts;

namespace MenuBar.Services;

public sealed class LiveMetricsHub
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _moduleSessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<double>> _history = new(StringComparer.Ordinal);
    private HardwareMetrics? _latest;

    public event Action<HardwareMetrics>? MetricsUpdated;
    public event Action? InterestsChanged;

    public HardwareMetrics? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public bool NeedsPlatformMetrics => GetCollectorInterests().Platform;
    public bool NeedsNvidiaExtendedMetrics => GetCollectorInterests().NvidiaExtended;
    public bool NeedsBluetoothMetrics => GetCollectorInterests().Bluetooth;
    public bool NeedsMemoryDetails => GetCollectorInterests().MemoryDetails;
    public bool NeedsProcessMetrics => GetCollectorInterests().Processes;
    public bool NeedsSensorMetrics => GetCollectorInterests().Sensors;

    public MetricsCollectorDemand GetCollectorInterests()
    {
        // One coherent snapshot; a popup cannot open/close halfway through a policy read.
        lock (_gate)
        {
            bool Has(string id) => _moduleSessions.GetValueOrDefault(id) > 0;
            return new(Has(MetricIds.Cpu) || Has("sensors"), Has(MetricIds.Gpu) || Has("sensors"),
                Has(MetricIds.Bluetooth), Has(MetricIds.Ram), Has(MetricIds.Cpu) || Has(MetricIds.Ram),
                Has("sensors") || Has(MetricIds.Gpu));
        }
    }

    public IDisposable BeginModuleSession(string requestedMetricId)
    {
        var moduleId = NormalizeModuleId(requestedMetricId);
        lock (_gate)
        {
            _moduleSessions[moduleId] = _moduleSessions.GetValueOrDefault(moduleId) + 1;
        }

        InterestsChanged?.Invoke();
        return new ModuleSession(this, moduleId);
    }

    public IReadOnlyList<double> GetHistory(string requestedMetricId)
    {
        var moduleId = NormalizeModuleId(requestedMetricId);
        lock (_gate)
        {
            return _history.TryGetValue(moduleId, out var values) ? values.ToArray() : [];
        }
    }

    public void Publish(HardwareMetrics metrics, int maximumSamples)
    {
        maximumSamples = Math.Clamp(maximumSamples, 2, 600);
        lock (_gate)
        {
            _latest = metrics;
            Append(MetricIds.Cpu, metrics.CpuPercent, maximumSamples);
            Append(MetricIds.Gpu, metrics.GpuPercent, maximumSamples);
            Append(MetricIds.Ram, metrics.MemoryPercent, maximumSamples);
            Append(MetricIds.Disk, metrics.DiskUsedPercent, maximumSamples);
            Append("network", metrics.NetworkDownloadBytesPerSecond, maximumSamples);
            Append(MetricIds.Battery, metrics.BatteryPercent, maximumSamples);
            Append(MetricIds.Bluetooth, metrics.BluetoothConnectedDeviceCount, maximumSamples);
            Append("sensors", SensorMetricSelector.PreferredTemperatureCelsius(metrics), maximumSamples);
        }

        MetricsUpdated?.Invoke(metrics);
    }

    private void Append(string moduleId, double? value, int maximumSamples)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            return;
        }

        if (!_history.TryGetValue(moduleId, out var values))
        {
            values = new Queue<double>();
            _history[moduleId] = values;
        }

        values.Enqueue(value.Value);
        while (values.Count > maximumSamples)
        {
            values.Dequeue();
        }
    }

    private void EndModuleSession(string moduleId)
    {
        lock (_gate)
        {
            var count = _moduleSessions.GetValueOrDefault(moduleId);
            if (count <= 1)
            {
                _moduleSessions.Remove(moduleId);
            }
            else
            {
                _moduleSessions[moduleId] = count - 1;
            }
        }

        InterestsChanged?.Invoke();
    }

    public static string NormalizeModuleId(string id) => id switch
    {
        MetricIds.Cpu or MetricIds.CpuTemperature or MetricIds.CpuFrequency => MetricIds.Cpu,
        MetricIds.Gpu or MetricIds.GpuTemperature or MetricIds.GpuPower or MetricIds.GpuMemory => MetricIds.Gpu,
        MetricIds.Ram => MetricIds.Ram,
        MetricIds.Disk => MetricIds.Disk,
        MetricIds.NetworkDownload or MetricIds.NetworkUpload => "network",
        MetricIds.Battery => MetricIds.Battery,
        MetricIds.Bluetooth => MetricIds.Bluetooth,
        _ => "sensors"
    };

    private sealed class ModuleSession(LiveMetricsHub owner, string moduleId) : IDisposable
    {
        private LiveMetricsHub? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndModuleSession(moduleId);
    }
}
