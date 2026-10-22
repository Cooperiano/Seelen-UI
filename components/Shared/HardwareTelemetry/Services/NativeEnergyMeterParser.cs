using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace HardwareTelemetry.Services;

internal sealed record EnergyChannelMetadata(string Name, uint Unit);
internal sealed record EnergyDeviceMetadata(string Manufacturer, string Model, IReadOnlyList<EnergyChannelMetadata> Channels);

internal static class NativeEnergyMeterParser
{
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public static EnergyDeviceMetadata ParseMetadata(ushort version, ReadOnlySpan<byte> buffer)
    {
        if (version is not (1 or 2)) throw new NotSupportedException($"EMI version {version} is unsupported.");
        var header = version == 1 ? 72 : 68;
        if (buffer.Length < header) throw new InvalidDataException("Truncated EMI metadata header.");
        var manufacturer = Text(buffer.Slice(version == 1 ? 4 : 0, 32));
        var model = Text(buffer.Slice(version == 1 ? 36 : 32, 32));
        var count = version == 1 ? 1 : BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(66, 2));
        if (count is < 1 or > 128) throw new InvalidDataException("Invalid EMI channel count.");
        var channels = new List<EnergyChannelMetadata>(count);
        var offset = header;
        for (var index = 0; index < count; index++)
        {
            uint unit;
            int nameSize;
            if (version == 1)
            {
                unit = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                nameSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(70, 2));
            }
            else
            {
                if (buffer.Length - offset < 6) throw new InvalidDataException("Truncated EMI channel header.");
                unit = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4));
                nameSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 4, 2));
                offset += 6;
            }
            if (nameSize is < 2 or > 8192 || nameSize % 2 != 0 || nameSize > buffer.Length - offset)
                throw new InvalidDataException("Invalid EMI UTF-16 channel length.");
            var name = Text(buffer.Slice(offset, nameSize));
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Empty EMI channel name.");
            if (channels.Any(item => item.Name == name)) throw new InvalidDataException("Duplicate EMI channel names.");
            channels.Add(new(name, unit));
            offset += nameSize; // Packed variable-length entries; there is no 4-byte padding.
        }
        return new(manufacturer, model, channels);
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        try { return Utf16.GetString(bytes).Split('\0')[0]; }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Malformed EMI UTF-16 name.", exception); }
    }
}
