using System.Runtime.InteropServices;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class NvidiaFieldParserTests
{
    public static void Run()
    {
        if (Marshal.SizeOf<NvidiaFieldParser.NativeField>() != 40 ||
            Marshal.OffsetOf<NvidiaFieldParser.NativeField>("TimestampMicroseconds").ToInt32() != 8 ||
            Marshal.OffsetOf<NvidiaFieldParser.NativeField>("ReturnCode").ToInt32() != 28 ||
            Marshal.OffsetOf<NvidiaFieldParser.NativeField>("ValueBits").ToInt32() != 32)
            throw new InvalidOperationException("NVML field ABI layout mismatch.");
        var source = new NvidiaFieldParser.NativeField
        { FieldId = 185, ScopeId = 0, TimestampMicroseconds = 1_790_000_000_000_000, ValueType = 1, ValueBits = 15_684 };
        var parsed = NvidiaFieldParser.Parse(source, 185, "GPU 0/");
        if (Math.Abs(parsed.Value!.Value - 15.684) > 1e-9 || parsed.Unit != "W" || parsed.Probe.Status != SensorInterfaceStatus.Readable ||
            parsed.Name != "GPU Package · 1 s Average") throw new InvalidOperationException("NVML mW/W units or average identity failed.");
        source.FieldId = 186;
        source.ValueBits = 0;
        if (NvidiaFieldParser.Parse(source, 186, "GPU 0/") is not { Value: 0, Name: "GPU Package · Instant" })
            throw new InvalidOperationException("NVML instant identity or valid zero was discarded.");
        foreach (var type in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            source.ValueType = type;
            source.ValueBits = type == 0 ? unchecked((ulong)BitConverter.DoubleToInt64Bits(15_000)) : type == 2 ? 0xffffffff00003a98UL : 15_000UL;
            if (NvidiaFieldParser.Parse(source, 186, "GPU 0/").Value != 15)
                throw new InvalidOperationException("NVML typed union/Windows unsigned-long conversion failed.");
        }
        foreach (var change in new Action[] {
            () => source.ScopeId = 2, () => source.TimestampMicroseconds = 0, () => source.LatencyMicroseconds = -1,
            () => source.FieldId = 82, () => source.ValueType = 99,
            () => { source.ValueType = 0; source.ValueBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(double.NaN)); },
            () => { source.ValueType = 4; source.ValueBits = ulong.MaxValue; },
            () => { source.ValueType = 1; source.ValueBits = uint.MaxValue; } })
        {
            source = new NvidiaFieldParser.NativeField { FieldId = 186, TimestampMicroseconds = 1, ValueType = 1, ValueBits = 15_000 };
            change();
            if (NvidiaFieldParser.Parse(source, 186, "GPU 0/") is not { Value: null, TimestampMicroseconds: null,
                Probe.Status: SensorInterfaceStatus.InvalidPayload })
                throw new InvalidOperationException("Malformed field metadata/value promoted to a reading.");
        }
        source.ReturnCode = 3;
        if (NvidiaFieldParser.Parse(source, 186, "GPU 0/") is not { Value: null, TimestampMicroseconds: null,
            Probe.Status: SensorInterfaceStatus.Unsupported })
            throw new InvalidOperationException("A failed batch member exposed its undefined union/timestamp.");
        source = new NvidiaFieldParser.NativeField { FieldId = 82, TimestampMicroseconds = 1, ValueType = 5, ValueBits = unchecked((uint)-10) };
        if (NvidiaFieldParser.Parse(source, 82, "GPU 0/") is not { Value: -10, Unit: "°C" })
            throw new InvalidOperationException("Signed GPU-memory Celsius value was misread.");
        Console.WriteLine("Passed NVML field ABI, typed union, units, scope, zero and per-field error checks.");
    }
}
