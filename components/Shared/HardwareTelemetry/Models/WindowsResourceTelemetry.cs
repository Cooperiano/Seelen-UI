namespace HardwareTelemetry.Models;

public sealed record ProcessResourceTelemetry(int ProcessId, string Name, double CpuPercent,
    ulong WorkingSetBytes, ulong PrivateWorkingSetBytes);
public sealed record ProcessResourceCapture(IReadOnlyList<ProcessResourceTelemetry> Processes,
    IReadOnlyList<SensorInterfaceProbe> Probes);
public sealed record GpuEngineTelemetry(string AdapterLuid, int PhysicalAdapter, int Engine,
    string EngineType, double UtilizationPercent);
public sealed record GpuAdapterIdentity(string Luid, string Name, uint VendorId, uint DeviceId, bool Software);
public sealed record GpuEngineCapture(IReadOnlyList<GpuEngineTelemetry> Engines,
    IReadOnlyList<GpuAdapterIdentity> Adapters, IReadOnlyList<SensorInterfaceProbe> Probes);
public sealed record MemoryCompressionTelemetry(uint ProcessId, ulong ResidentBytes,
    ulong TotalDataCompressed, ulong TotalCompressedSize, ulong TotalUniqueDataCompressed);
public sealed record MemoryCompressionCapture(MemoryCompressionTelemetry? Value, SensorInterfaceProbe Probe);
