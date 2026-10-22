using System.Buffers.Binary;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal sealed record AsusDstsEndpoint(uint Id, string Name, string? SensorType);

internal static class AsusDstsParser
{
    internal const uint ControlCode = 0x0022240c;
    internal static readonly IReadOnlyList<AsusDstsEndpoint> Endpoints = Array.AsReadOnly(new[] {
        new AsusDstsEndpoint(0x00120094, "CPU Temperature", "Temperature"),
        new AsusDstsEndpoint(0x00120097, "GPU Temperature", "Temperature"),
        new AsusDstsEndpoint(0x00110013, "CPU Fan", "Fan"),
        new AsusDstsEndpoint(0x00110014, "GPU Fan", "Fan"),
        new AsusDstsEndpoint(0x00110031, "Middle Fan", "Fan"),
        new AsusDstsEndpoint(0x0012005a, "Battery Discharge", "Power"),
        new AsusDstsEndpoint(0x0012006c, "AC Present (state only)", null)
    });

    public static byte[] Request(uint endpoint)
    {
        if (!Endpoints.Any(item => item.Id == endpoint)) throw new ArgumentOutOfRangeException(nameof(endpoint));
        var input = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(input, 0x53545344); // DSTS, never DEVS/INIT/WDOG.
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8), endpoint);
        return input; // State argument stays zero; no arbitrary method or register input.
    }

    public static uint RawStatus(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 4 or > 16) throw new InvalidDataException("Invalid ASUS DSTS returned-byte count.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public static HardwareSensorReading? Parse(AsusDstsEndpoint endpoint, uint raw)
    {
        // Linux asus-wmi.h: unsupported is a sentinel, not a flagged measurement.
        // Check it BEFORE presence; 0xfffffffe includes bit 16 and low signed value -2.
        if (raw is 0xfffffffe or 0xffffffff || (raw & 0x10000) == 0)
            throw new NotSupportedException($"DSTS endpoint lacks presence or reports unsupported: 0x{raw:x8}.");
        var value = raw & 0xffff;
        if (endpoint.SensorType is null)
        {
            if (value > 1) throw new InvalidDataException("Invalid ASUS AC-present state.");
            return null; // AC state cannot satisfy DC-in voltage/current/power requirements.
        }
        var unit = "";
        double physical;
        switch (endpoint.SensorType)
        {
            case "Temperature":
                if (value > 125) throw new InvalidDataException("Invalid ASUS temperature range.");
                physical = value; unit = "°C"; break;
            case "Fan":
                if (value > 120) throw new InvalidDataException("Invalid ASUS fan speed range.");
                physical = value * 100; unit = "RPM"; break;
            case "Power":
                physical = Math.Abs((int)unchecked((short)value)) / 100d; unit = "W"; break;
            default:
                throw new InvalidDataException("Unknown ASUS sensor kind.");
        }
        return new($"/asus/firmware/{endpoint.SensorType.ToLowerInvariant()}/{endpoint.Id:x8}",
            endpoint.SensorType == "Fan" ? "ASUS EC" : "ASUS Firmware", endpoint.SensorType == "Power" ? "Battery" : "Controller",
            endpoint.Name, endpoint.SensorType, physical, unit);
    }
}
