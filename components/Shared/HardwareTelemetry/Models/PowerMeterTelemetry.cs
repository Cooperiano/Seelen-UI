namespace HardwareTelemetry.Models;

public sealed record PowerMeterCapabilities(uint Flags, uint MeasurementUnit, uint MeasurementType,
    uint AccuracyThousandthsPercent, uint SamplingPeriodMilliseconds,
    uint MinimumAverageIntervalMilliseconds, uint MaximumAverageIntervalMilliseconds,
    string Model, string Oem)
{
    public bool SupportsMeasurement => (Flags & 1) != 0;
    public bool DischargeOnly => (Flags & 0x100) != 0;
}

// Identifiers are hashed; neither asset serial numbers nor raw device paths are exported.
public sealed record PowerMeterHardware(uint Count, IReadOnlyList<string> DeviceIds, bool SystemMeterDeclared);
// A valid response envelope alone does not establish supported meter capabilities or rail scope.
public sealed record PowerMeterMeasurementPayload(uint Version, uint CurrentPowerRaw);
public sealed record PowerMeterDevice(string Id, PowerMeterCapabilities? Capabilities,
    PowerMeterHardware? MeteredHardware, uint? AveragingIntervalMilliseconds,
    PowerMeterMeasurementPayload? MeasurementPayload, double? Watts);
public sealed record PowerMeterCapture(IReadOnlyList<PowerMeterDevice> Devices,
    IReadOnlyList<SensorInterfaceProbe> Probes);
