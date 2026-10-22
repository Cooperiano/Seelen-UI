namespace HardwareTelemetry.Models;

public sealed record IntelDptfMetadata(string TemperatureDescription, string PowerDescription);
public sealed record IntelDptfParticipant(string Id, bool? Active, uint? RawTemperature,
    uint? RawPower, double? TemperatureCelsius, double? ParticipantRaplWatts);
public sealed record IntelDptfCapture(IntelDptfMetadata? Metadata,
    IReadOnlyList<IntelDptfParticipant> Participants, IReadOnlyList<SensorInterfaceProbe> Probes);
