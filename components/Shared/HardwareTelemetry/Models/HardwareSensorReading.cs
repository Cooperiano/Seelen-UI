namespace HardwareTelemetry.Models;

public sealed record HardwareSensorReading(
    string Identifier,
    string Hardware,
    string HardwareType,
    string Name,
    string Type,
    double Value,
    string Unit);
