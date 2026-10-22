namespace HardwareTelemetry.Models;

public sealed record BluetoothDeviceMetric(
    string Name,
    bool IsConnected,
    bool IsAuthenticated,
    bool IsRemembered,
    DateTimeOffset? LastSeen);
