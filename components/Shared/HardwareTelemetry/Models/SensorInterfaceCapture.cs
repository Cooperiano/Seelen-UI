namespace HardwareTelemetry.Models;

public enum SensorInterfaceStatus
{
    Readable,
    NoInterface,
    PermissionDenied,
    Unsupported,
    InvalidPayload,
    Failed
}

public sealed record SensorInterfaceProbe(
    string Provider, string Endpoint, SensorInterfaceStatus Status, string Detail, int? NativeError = null);

public sealed record SensorInterfaceCapture(
    IReadOnlyList<HardwareSensorReading> Readings, IReadOnlyList<SensorInterfaceProbe> Probes);

public sealed record EnergyMeterChannel(string Name, uint Unit, ulong AbsoluteEnergy, ulong AbsoluteTime100ns);
public sealed record EnergyMeterDevice(string Id, ushort Version, string Manufacturer, string Model,
    IReadOnlyList<EnergyMeterChannel> Channels);
public sealed record EnergyMeterInterfaceCapture(
    IReadOnlyList<EnergyMeterDevice> Devices, IReadOnlyList<SensorInterfaceProbe> Probes);
