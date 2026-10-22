namespace HardwareTelemetry.Models;

public sealed record AsusFirmwareEndpoint(string Endpoint, string Name, int ReturnedBytes, string? RawStatus);
public sealed record AsusFirmwareCapture(IReadOnlyList<HardwareSensorReading> Readings,
    IReadOnlyList<AsusFirmwareEndpoint> Endpoints, IReadOnlyList<SensorInterfaceProbe> Probes);
