namespace HardwareTelemetry.Models;

public sealed record GpuNodeMetadata(string AdapterLuid, int PhysicalAdapter, int Node,
    string CounterEngineType, uint NativeEngineType, string NativeEngineTypeName,
    string FriendlyName, uint Flags, bool GpuMmuSupported, bool IoMmuSupported,
    double CounterUtilizationPercent);
public sealed record GpuNodeMetadataCapture(IReadOnlyList<GpuNodeMetadata> Nodes,
    IReadOnlyList<SensorInterfaceProbe> Probes);
