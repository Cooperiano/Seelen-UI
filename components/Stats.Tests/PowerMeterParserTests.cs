using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Text;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class PowerMeterParserTests
{
    public static void Run()
    {
        var capabilities = Capabilities();
        var parsed = PowerMeterParser.ParseCapabilities(capabilities);
        if (!parsed.SupportsMeasurement || !parsed.DischargeOnly || parsed.MeasurementUnit != 0 ||
            parsed.MeasurementType != 1 || parsed.SamplingPeriodMilliseconds != 1000 || parsed.Model != "Fixture" || parsed.Oem != "Test")
            throw new InvalidOperationException("PMI offsets, padded BOOLEAN layout, model/OEM or discharge direction failed.");
        var measurement = new byte[8];
        Put(measurement, 0, 1);
        Put(measurement, 4, 15_684);
        if (PowerMeterParser.ParseWatts(measurement, parsed) != 15.684)
            throw new InvalidOperationException("PMI mW/W conversion failed.");
        Put(measurement, 4, 0);
        if (PowerMeterParser.ParseWatts(measurement, parsed) != 0)
            throw new InvalidOperationException("PMI valid zero discarded.");
        Reject(() => PowerMeterParser.ParseWatts(measurement, null));
        Reject(() => PowerMeterParser.ParseWatts(measurement, parsed with { Flags = 0 }));
        Reject(() => PowerMeterParser.ParseWatts(measurement, parsed with { MeasurementUnit = 1 }));
        Reject(() => PowerMeterParser.ParseWatts(measurement, parsed with { MeasurementType = 2 }));
        Put(measurement, 4, uint.MaxValue);
        Reject(() => PowerMeterParser.ParseWatts(measurement, parsed));
        Put(measurement, 4, 0);
        for (var length = 0; length < 8; length++)
        {
            var shortBuffer = measurement[..length];
            Reject(() => PowerMeterParser.ParseWatts(shortBuffer, parsed));
        }
        Put(measurement, 0, 2);
        Reject(() => PowerMeterParser.ParseWatts(measurement, parsed));
        Put(measurement, 0, 1);
        for (var length = 0; length < capabilities.Length; length++)
        {
            var shortBuffer = capabilities[..length];
            Reject(() => PowerMeterParser.ParseCapabilities(shortBuffer));
        }
        foreach (var change in new Action<byte[]>[] {
            data => Put(data, 0, 2), data => Put(data, 4, 153), data => Put(data, 8, 1),
            data => Put(data, 24, 100_001), data => { data[56] = 0; data[57] = 0xd8; } })
        {
            var malformed = capabilities.ToArray();
            change(malformed);
            Reject(() => PowerMeterParser.ParseCapabilities(malformed));
        }
        // Serial UTF-16 is intentionally never parsed or exported, even if malformed.
        capabilities[88] = 0; capabilities[89] = 0xd8;
        _ = PowerMeterParser.ParseCapabilities(capabilities);
        var system = Hardware();
        if (PowerMeterParser.ParseHardware(system) is not { Count: 0, SystemMeterDeclared: true })
            throw new InvalidOperationException("PMI NULL system-meter declaration was lost.");
        var components = Hardware("\\Device\\Battery0", "\\Device\\Fixture1");
        var hardware = PowerMeterParser.ParseHardware(components);
        if (hardware.Count != 2 || hardware.SystemMeterDeclared || hardware.DeviceIds.Any(id => id.Length != 16 || id.Contains("Battery")))
            throw new InvalidOperationException("PMI component scope or identifier privacy failed.");
        Reject(() => PowerMeterParser.ParseHardware(Hardware("\\Device\\same", "\\Device\\SAME")));
        Reject(() => PowerMeterParser.ParseHardware(Hardware("not-a-native-path")));
        Put(components, 12, 3);
        Reject(() => PowerMeterParser.ParseHardware(components));
        components = Hardware("\\Device\\Battery0");
        components[^1] = 1;
        Reject(() => PowerMeterParser.ParseHardware(components));
        var configuration = PowerMeterParser.ConfigurationRequest();
        Put(configuration, 12, 2500);
        if (PowerMeterParser.ParseConfiguration(configuration) != 2500)
            throw new InvalidOperationException("PMI existing averaging interval was misread.");
        Reject(() => PowerMeterParser.ParseConfiguration(configuration[..19]));
        Put(configuration, 8, 1);
        Reject(() => PowerMeterParser.ParseConfiguration(configuration));
        configuration = PowerMeterParser.ConfigurationRequest();
        var probes = new List<SensorInterfaceProbe>();
        var calls = new List<uint>();
        byte[] Query(uint code, int size, byte[]? input)
        {
            calls.Add(code);
            if (code == PowerMeterParser.GetCapabilities)
            {
                if (input?.Length != 152 || Read(input, 0) != 1 || Read(input, 4) != 152)
                    throw new InvalidOperationException("PMI GET_CAPABILITIES input ABI mismatch.");
                return Read(input, 8) == 0 ? Capabilities() : Hardware("\\Device\\Battery0");
            }
            if (code == PowerMeterParser.GetConfiguration && size == 20 && input is { Length: 20 }) return configuration;
            if (code == PowerMeterParser.GetMeasurement && size == 8 && input is null) return measurement;
            throw new InvalidOperationException("PMI unexpected non-GET query/input.");
        }
        var device = WindowsPowerMeterInterface.CaptureDevice("test", Query, probes);
        if (device is not { Watts: 0, MeteredHardware.SystemMeterDeclared: false, Capabilities.DischargeOnly: true } ||
            calls.Count != 4 || probes.Count != 5 || probes.Any(probe => probe.Status != SensorInterfaceStatus.Readable))
            throw new InvalidOperationException("PMI query sequence, isolated component identity or valid zero failed.");
        probes.Clear();
        var denied = WindowsPowerMeterInterface.CaptureDevice("test", (code, size, input) =>
        {
            if (code == PowerMeterParser.GetCapabilities && input is not null && Read(input, 8) == 0)
                throw new Win32Exception(5, "Fixture capability access denied.");
            return Query(code, size, input);
        }, probes);
        if (denied.Watts is not null || denied.Capabilities is not null || denied.MeteredHardware is null || denied.MeasurementPayload?.CurrentPowerRaw != 0 ||
            !probes.Any(probe => probe.Status == SensorInterfaceStatus.PermissionDenied && probe.NativeError == 5) ||
            !probes.Any(probe => probe.Endpoint.EndsWith("/measurement") && probe.Status == SensorInterfaceStatus.Readable) ||
            !probes.Any(probe => probe.Endpoint.EndsWith("/physical-reading") && probe.Status == SensorInterfaceStatus.Unsupported))
            throw new InvalidOperationException("PMI partial failure manufactured zero watts or lost native permission evidence.");
        probes.Clear();
        var malformedDevice = WindowsPowerMeterInterface.CaptureDevice("test", (code, size, input) =>
            code == PowerMeterParser.GetMeasurement ? new byte[7] : Query(code, size, input), probes);
        if (malformedDevice.Watts is not null || malformedDevice.MeasurementPayload is not null ||
            !probes.Any(probe => probe.Endpoint.EndsWith("/measurement") && probe.Status == SensorInterfaceStatus.InvalidPayload))
            throw new InvalidOperationException("Malformed native PMI reply escaped its boundary or manufactured a reading.");
        probes.Clear();
        var malformedCapabilities = WindowsPowerMeterInterface.CaptureDevice("test", (code, size, input) =>
            code == PowerMeterParser.GetCapabilities && input is not null && Read(input, 8) == 0 ? new byte[151] : Query(code, size, input), probes);
        if (malformedCapabilities.Watts is not null || malformedCapabilities.MeasurementPayload is null ||
            !probes.Any(probe => probe.Endpoint.EndsWith("/capabilities") && probe.Status == SensorInterfaceStatus.InvalidPayload))
            throw new InvalidOperationException("Malformed PMI metadata escaped its boundary or destroyed unrelated GET evidence.");
        if (PowerMeterParser.GetCapabilities != ((0x45u << 16) | (1u << 14)) ||
            PowerMeterParser.GetConfiguration != ((0x45u << 16) | (1u << 14) | (1u << 2)) ||
            PowerMeterParser.GetMeasurement != ((0x45u << 16) | (1u << 14) | (3u << 2)))
            throw new InvalidOperationException("PMI fixed read-only CTL_CODE definitions changed.");
        Console.WriteLine("Passed PMI ABI, units, valid zero, malformed buffers, component scope, privacy, GET-only sequence and partial failure checks.");
    }

    private static byte[] Capabilities()
    {
        var data = PowerMeterParser.CapabilitiesRequest(0);
        Put(data, 12, 0x101); Put(data, 20, 1); Put(data, 24, 1000); Put(data, 28, 1000);
        Put(data, 32, 1000); Put(data, 36, 60_000);
        Encoding.Unicode.GetBytes("Fixture\0").CopyTo(data, 56);
        Encoding.Unicode.GetBytes("PrivateSerial\0").CopyTo(data, 88);
        Encoding.Unicode.GetBytes("Test\0").CopyTo(data, 120);
        return data;
    }

    private static byte[] Hardware(params string[] paths)
    {
        var text = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var data = new byte[16 + text.Length];
        Put(data, 0, 1); Put(data, 4, (uint)data.Length); Put(data, 8, 1); Put(data, 12, (uint)paths.Length);
        text.CopyTo(data, 16);
        return data;
    }

    private static uint Read(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static void Put(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Reject(Action read)
    {
        try { read(); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed/unsupported PMI payload was accepted.");
    }
}
