using HardwareTelemetry.Services;
using Monitoring.Contracts;

namespace MenuBar.Services;

public readonly record struct MetricsCollectorDemand(
    bool Platform, bool NvidiaExtended, bool Bluetooth, bool MemoryDetails, bool Processes, bool Sensors)
{
    internal MetricsCollectorDemand Combine(MetricsCollectorDemand other) => new(
        Platform || other.Platform, NvidiaExtended || other.NvidiaExtended, Bluetooth || other.Bluetooth,
        MemoryDetails || other.MemoryDetails, Processes || other.Processes, Sensors || other.Sensors);

    public static MetricsCollectorDemand Resolve(StatsApplicationSettings settings, MetricsCollectorDemand interests)
    {
        // Alerts have a data dependency even when their menu-bar widget is hidden.
        var enabled = settings.Metrics.Where(item => item.IsEnabled).Select(item => item.Id)
            .Concat(settings.Alerts?.Where(item => item.IsEnabled).Select(item => item.MetricId) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var full = !settings.LowPowerMode;
        return new(
            full || interests.Platform || enabled.Contains(MetricIds.Power) || enabled.Contains(MetricIds.CpuTemperature) || enabled.Contains(MetricIds.CpuFrequency),
            full || interests.NvidiaExtended || enabled.Contains(MetricIds.GpuTemperature) || enabled.Contains(MetricIds.GpuPower) || enabled.Contains(MetricIds.GpuMemory),
            full || interests.Bluetooth || enabled.Contains(MetricIds.Bluetooth),
            full || interests.MemoryDetails,
            full || interests.Processes,
            full || interests.Sensors || enabled.Contains(MetricIds.Power));
    }

    internal void Apply(IHardwareMetricsReader reader)
    {
        reader.ReadPlatformMetrics = Platform;
        reader.ReadNvidiaExtendedMetrics = NvidiaExtended;
        reader.ReadBluetoothMetrics = Bluetooth;
        reader.ReadMemoryDetails = MemoryDetails;
        reader.ReadProcessMetrics = Processes;
        reader.ReadSensorMetrics = Sensors;
    }
}
