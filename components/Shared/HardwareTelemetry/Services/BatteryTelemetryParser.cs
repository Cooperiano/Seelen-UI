using System.Buffers.Binary;
using System.IO;

namespace HardwareTelemetry.Services;

internal static class BatteryTelemetryParser
{
    public static BatteryNativeStatus ParseStatus(ReadOnlySpan<byte> bytes, bool relative)
    {
        if (bytes.Length != 16) throw new InvalidDataException("Invalid BATTERY_STATUS length.");
        var state = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var capacity = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var voltage = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        double? volts = voltage == uint.MaxValue ? null : voltage / 1000d;
        double? watts = relative || rate == int.MinValue ? null : rate / 1000d;
        return new(state, relative || capacity == uint.MaxValue ? null : capacity,
            volts, watts, volts is > 0 && watts is not null ? watts / volts : null);
    }

    public static double ParseTemperature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4) throw new InvalidDataException("Invalid battery temperature length.");
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var celsius = raw / 10d - 273.15;
        if (raw == uint.MaxValue || celsius is < -40 or > 150)
            throw new InvalidDataException("Battery temperature is unavailable or outside the supported physical range.");
        return celsius;
    }
}

internal sealed record BatteryNativeStatus(uint PowerState, uint? RemainingMilliWattHours,
    double? VoltageVolts, double? SignedPowerWatts, double? CalculatedCurrentAmps);
