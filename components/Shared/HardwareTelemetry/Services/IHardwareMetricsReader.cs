using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

public interface IHardwareMetricsReader
{
    bool ReadPlatformMetrics { get; set; }

    bool ReadNvidiaExtendedMetrics { get; set; }

    bool ReadBluetoothMetrics { get; set; }

    bool ReadMemoryDetails { get; set; }

    bool ReadSensorMetrics { get; set; }

    bool ReadProcessMetrics { get; set; }

    HardwareMetrics Capture();
}
