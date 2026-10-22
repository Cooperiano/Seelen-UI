using System.Buffers.Binary;
using System.IO;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal static class PowerMeterParser
{
    // Microsoft.Windows.WDK.x64 10.0.26100.6584, km/pmi.h; devioctl.h FILE_DEVICE_PMI=0x45.
    internal const uint GetCapabilities = 0x454000;
    internal const uint GetConfiguration = 0x454004;
    internal const uint GetMeasurement = 0x45400c;
    internal const int CapabilitiesSize = 152; // 12-byte header + 140-byte reported union.
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public static byte[] CapabilitiesRequest(uint type)
    {
        if (type > 1) throw new ArgumentOutOfRangeException(nameof(type));
        var input = new byte[CapabilitiesSize];
        Write(input, 0, 1);
        Write(input, 4, CapabilitiesSize);
        Write(input, 8, type);
        return input;
    }

    public static byte[] ConfigurationRequest()
    {
        var input = new byte[20]; // ULONG Version, USHORT Size, padding, enum, 8-byte union.
        Write(input, 0, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(4), 20);
        return input; // PmiMeasurementConfiguration=0; no budget or threshold requests.
    }

    public static PowerMeterCapabilities ParseCapabilities(ReadOnlySpan<byte> buffer)
    {
        Header(buffer, 0, CapabilitiesSize);
        if (buffer.Length != CapabilitiesSize) throw new InvalidDataException("Invalid PMI reported-capability size.");
        var accuracy = Read(buffer, 24);
        if (accuracy > 100_000) throw new InvalidDataException("Invalid PMI accuracy percentage.");
        return new(Read(buffer, 12), Read(buffer, 16), Read(buffer, 20), accuracy,
            Read(buffer, 28), Read(buffer, 32), Read(buffer, 36),
            Text(buffer.Slice(56, 32)), Text(buffer.Slice(120, 32)));
        // Native BOOLEAN Writeable is at offset 44; budget ULONGs follow at 48/52.
        // SerialNumber is at 88..119 and is deliberately never decoded/exported.
    }

    public static PowerMeterHardware ParseHardware(ReadOnlySpan<byte> buffer)
    {
        Header(buffer, 1, 20);
        var count = Read(buffer, 12);
        if (count > 1024 || (buffer.Length - 16) % 2 != 0)
            throw new InvalidDataException("Invalid PMI hardware-list size/count.");
        string text;
        try { text = Utf16.GetString(buffer[16..]); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Malformed PMI hardware UTF-16.", exception); }
        var end = text.IndexOf("\0\0", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("PMI hardware list lacks its double-NUL terminator.");
        var paths = text[..end].Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length != count || paths.Any(path => !path.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase)) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || text[(end + 2)..].Any(character => character != '\0'))
            throw new InvalidDataException("PMI hardware count/paths do not match their envelope.");
        return new(count, paths.Select(ReadOnlyNativeDevice.DeviceId).ToArray(), count == 0);
    }

    public static uint ParseConfiguration(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length != 20 || Read(buffer, 0) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]) != 20 || Read(buffer, 8) != 0)
            throw new InvalidDataException("Invalid PMI measurement-configuration envelope.");
        return Read(buffer, 12);
    }

    public static PowerMeterMeasurementPayload ParseMeasurement(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length != 8 || Read(buffer, 0) != 1 || Read(buffer, 4) == uint.MaxValue)
            throw new InvalidDataException("Invalid PMI measurement envelope/value.");
        return new(Read(buffer, 0), Read(buffer, 4));
    }

    public static double ParseWatts(ReadOnlySpan<byte> buffer, PowerMeterCapabilities? capabilities) =>
        InterpretWatts(ParseMeasurement(buffer), capabilities);

    public static double InterpretWatts(PowerMeterMeasurementPayload measurement, PowerMeterCapabilities? capabilities)
    {
        if (capabilities is null || !capabilities.SupportsMeasurement || capabilities.MeasurementUnit != 0 || capabilities.MeasurementType > 1)
            throw new NotSupportedException("PMI measurement units/type/support were not verified; no physical power value is exposed.");
        return measurement.CurrentPowerRaw / 1000d; // CurrentPower is mW, including a valid zero.
    }

    private static void Header(ReadOnlySpan<byte> buffer, uint type, int minimum)
    {
        if (buffer.Length < minimum || Read(buffer, 0) != 1 || Read(buffer, 4) != buffer.Length || Read(buffer, 8) != type)
            throw new InvalidDataException("Invalid PMI capability envelope/version/type/size.");
    }

    private static string Text(ReadOnlySpan<byte> buffer)
    {
        try { return Utf16.GetString(buffer).Split('\0')[0]; }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Malformed PMI asset UTF-16.", exception); }
    }

    private static uint Read(ReadOnlySpan<byte> buffer, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
    private static void Write(Span<byte> buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], value);
}
