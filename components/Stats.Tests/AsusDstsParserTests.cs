using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class AsusDstsParserTests
{
    public static void Run()
    {
        var battery = AsusDstsParser.Endpoints.Single(item => item.Id == 0x0012005a);
        // Regression: old code considered any nonzero byte 2 valid. Unsupported
        // 0xfffffffe then became Abs(Int16(-2))/100 = a manufactured 0.02 W.
        foreach (var invalid in new uint[] { 0, 0xfffffffe, 0xffffffff, 0x00020001 })
            Reject(() => AsusDstsParser.Parse(battery, invalid));
        if (AsusDstsParser.Parse(battery, 0x00010000)?.Value != 0 ||
            AsusDstsParser.Parse(battery, 0x00010bb8)?.Value != 30 ||
            AsusDstsParser.Parse(battery, 0x0001f448)?.Value != 30)
            throw new InvalidOperationException("ASUS signed-low-word, centiwatt or valid zero failed.");
        var cpu = AsusDstsParser.Endpoints.Single(item => item.Id == 0x00120094);
        if (AsusDstsParser.Parse(cpu, 0x0001004b) is not { Value: 75, Unit: "°C" })
            throw new InvalidOperationException("ASUS Celsius parsing failed.");
        Reject(() => AsusDstsParser.Parse(cpu, 0x000100ff));
        var fan = AsusDstsParser.Endpoints.Single(item => item.Id == 0x00110013);
        if (AsusDstsParser.Parse(fan, 0x00010000)?.Value != 0 ||
            AsusDstsParser.Parse(fan, 0x0001002d) is not { Value: 4500, Unit: "RPM" })
            throw new InvalidOperationException("ASUS stopped fan / hundreds-RPM conversion failed.");
        Reject(() => AsusDstsParser.Parse(fan, 0x000100ff));
        var ac = AsusDstsParser.Endpoints.Single(item => item.Id == 0x0012006c);
        if (AsusDstsParser.Parse(ac, 0x00010001) is not null || AsusDstsParser.Parse(ac, 0x00010000) is not null)
            throw new InvalidOperationException("AC presence was promoted into physical input metering.");
        Reject(() => AsusDstsParser.Parse(ac, 0x00010002));
        foreach (var size in new[] { 0, 1, 2, 3, 17 }) Reject(() => AsusDstsParser.RawStatus(new byte[size]));
        foreach (var size in new[] { 4, 8, 16 })
            if (AsusDstsParser.RawStatus(Bytes(0x0001002d, size)) != 0x0001002d)
                throw new InvalidOperationException("ASUS actual returned-byte parsing failed.");
        try { AsusDstsParser.Request(0x001200a0); throw new InvalidOperationException("Power-limit endpoint was allowed."); }
        catch (ArgumentOutOfRangeException) { }
        var calls = new List<uint>();
        byte[] Query(uint code, int size, byte[]? input)
        {
            if (code != AsusDstsParser.ControlCode || size != 16 || input is not { Length: 16 } ||
                Read(input, 0) != 0x53545344 || Read(input, 4) != 8 || Read(input, 12) != 0)
                throw new InvalidOperationException("ASUS query was not a fixed DSTS GET.");
            var endpoint = Read(input, 8);
            calls.Add(endpoint);
            return endpoint switch {
                0x00110013 => Bytes(0x0001002d),
                0x00110014 => Bytes(0x00010000),
                0x0012006c => Bytes(0x00010001),
                _ => Bytes(0xfffffffe)
            };
        }
        var capture = WindowsAsusFirmwareInterface.CaptureDevice(Query);
        if (calls.Count != 7 || calls.Distinct().Count() != 7 || capture.Endpoints.Count != 7 || capture.Readings.Count != 2 ||
            capture.Readings.Any(item => item.Type == "Power") || capture.Probes.Count(item => item.Status == SensorInterfaceStatus.Unsupported) != 4)
            throw new InvalidOperationException("Unsupported firmware endpoint polluted readings or hid working sibling endpoints.");
        capture = WindowsAsusFirmwareInterface.CaptureDevice((code, size, input) =>
            Read(input!, 8) == 0x00120094 ? new byte[3] : Query(code, size, input));
        if (!capture.Probes.Any(item => item.Endpoint == "00120094" && item.Status == SensorInterfaceStatus.InvalidPayload) || capture.Readings.Count != 2)
            throw new InvalidOperationException("Truncated ASUS reply escaped its diagnostic boundary.");
        capture = WindowsAsusFirmwareInterface.CaptureDevice((code, size, input) =>
        {
            if (Read(input!, 8) == 0x00120094) throw new Win32Exception(5, "Fixture access denied.");
            return Query(code, size, input);
        });
        if (!capture.Probes.Any(item => item.Endpoint == "00120094" && item.Status == SensorInterfaceStatus.PermissionDenied && item.NativeError == 5))
            throw new InvalidOperationException("ASUS permission failure became unsupported firmware.");
        calls.Clear();
        var production = AsusFirmwareSensorReader.CaptureDevice(Query);
        if (calls.Count != 6 || calls.Contains(0x0012006c) || production.Count != 2 || production.Any(item => item.Type == "Power"))
            throw new InvalidOperationException("Production ASUS collector lost decoder guards or added state queries to steady-state sampling.");
        production = AsusFirmwareSensorReader.CaptureDevice((code, size, input) =>
            Read(input!, 8) == 0x00110013 ? new byte[3] : Query(code, size, input));
        if (production.Count != 1 || production[0].Name != "GPU Fan")
            throw new InvalidOperationException("Production short reply created a reading or hid a valid sibling fan.");
        Console.WriteLine("Passed ASUS DSTS sentinel/presence, signed power/zero, units, fixed GET whitelist, short replies and isolated errors.");
    }

    private static byte[] Bytes(uint raw, int size = 4)
    {
        var data = new byte[size]; BinaryPrimitives.WriteUInt32LittleEndian(data, raw); return data;
    }
    private static uint Read(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed/unsupported ASUS reply became a reading.");
    }
}
