namespace HardwareTelemetry.Models;

public sealed record CpuCpuidLeaf(uint Leaf, uint Eax, uint Ebx, uint Ecx, uint Edx);
public sealed record CpuHardwareCapabilities(string Vendor, uint MaximumBasicLeaf,
    int? Family, int? Model, int? Stepping, bool? HypervisorPresent,
    bool? DigitalThermalSensorDeclared, bool? PackageThermalManagementDeclared,
    string? ReviewedCpuProvider);
public sealed record CpuHardwareCapabilityCapture(CpuHardwareCapabilities? Capabilities,
    IReadOnlyList<CpuCpuidLeaf> Leaves, IReadOnlyList<SensorInterfaceProbe> Probes);
