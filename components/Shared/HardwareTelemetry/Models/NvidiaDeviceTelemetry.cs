namespace HardwareTelemetry.Models;

public sealed record NvidiaDeviceTelemetry(uint Index, string Name, uint? CoreCount, double? TemperatureCelsius,
    double? PowerWatts, ulong? TotalEnergyMilliJoules, uint? GpuUtilizationPercent,
    uint? MemoryUtilizationPercent, uint? EncoderUtilizationPercent, uint? DecoderUtilizationPercent,
    ulong? MemoryTotalBytes, ulong? MemoryUsedBytes, uint? GraphicsClockMhz, uint? MemoryClockMhz,
    uint? PcieGeneration, uint? PcieWidth, IReadOnlyList<NvidiaSensorField>? SensorFields = null);

public sealed record NvidiaSensorField(uint FieldId, uint ScopeId, string Name, string Type,
    double? Value, string Unit, long? TimestampMicroseconds, SensorInterfaceProbe Probe);

public sealed record NvidiaTelemetryCapture(IReadOnlyList<NvidiaDeviceTelemetry> Devices,
    IReadOnlyList<HardwareSensorReading> Readings, IReadOnlyList<SensorInterfaceProbe> Probes);
