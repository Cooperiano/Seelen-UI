using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal static class NvidiaFieldParser
{
    // Field-query scope 0 is explicitly GPU-only. Do not reuse another API's scope enum here:
    // header descriptions conflict for scope 2 (base vs memory), so no guessed memory/PCIe rail.
    internal static readonly (uint Id, string Name, string Type, string Unit, double Scale)[] Fields =
    [
        (185, "GPU Package · 1 s Average", "Power", "W", 0.001),
        (186, "GPU Package · Instant", "Power", "W", 0.001),
        (82, "GPU Memory Temperature", "Temperature", "°C", 1)
    ];

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeField
    {
        public uint FieldId;
        public uint ScopeId;
        public long TimestampMicroseconds;
        public long LatencyMicroseconds;
        public int ValueType;
        public int ReturnCode;
        public ulong ValueBits;
    }

    internal static NvidiaSensorField Parse(NativeField field, uint expectedId, string prefix)
    {
        var definition = Fields.Single(item => item.Id == expectedId);
        var endpoint = prefix + $"field/{expectedId}/scope/0";
        var probe = WindowsNvidiaTelemetryInterface.DescribeStatus(endpoint, field.ReturnCode);
        NvidiaSensorField Result(double? value, SensorInterfaceProbe status) => new(expectedId, 0,
            definition.Name, definition.Type, value, definition.Unit,
            status.Status == SensorInterfaceStatus.Readable ? field.TimestampMicroseconds : null, status);
        // A failed field's union and timestamp are undefined, even if the batch call succeeded.
        if (field.ReturnCode != 0) return Result(null, probe);
        if (field.FieldId != expectedId || field.ScopeId != 0 || field.TimestampMicroseconds <= 0 || field.LatencyMicroseconds < 0)
            return Result(null, probe with { Status = SensorInterfaceStatus.InvalidPayload, Detail = "Field identity or timestamp metadata invalid." });
        double? raw = field.ValueType switch
        {
            0 => BitConverter.Int64BitsToDouble(unchecked((long)field.ValueBits)),
            1 or 2 => (uint)field.ValueBits, // Windows unsigned long is 32-bit, unlike Linux LP64.
            3 => field.ValueBits,
            4 => unchecked((long)field.ValueBits),
            5 => unchecked((int)field.ValueBits),
            6 => (ushort)field.ValueBits,
            _ => null
        };
        var value = raw * definition.Scale;
        if (value is not { } valid || !double.IsFinite(valid) ||
            (definition.Type == "Power" ? valid < 0 || valid > 10_000 : valid < -273.15 || valid > 1000))
            return Result(null, probe with { Status = SensorInterfaceStatus.InvalidPayload, Detail = "Unknown value type, sentinel or invalid physical value." });
        return Result(valid, probe with { Detail = definition.Type == "Power"
            ? "GPU-only scope; mW converted to W. Instant and 1-second average remain distinct, neither is system/PCIe input."
            : "Driver-reported GPU memory temperature in Celsius." });
    }
}
