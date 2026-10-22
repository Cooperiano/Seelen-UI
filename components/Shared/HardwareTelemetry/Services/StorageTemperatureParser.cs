using System.Buffers.Binary;
using System.IO;

namespace HardwareTelemetry.Services;

internal sealed record StorageTemperatureValue(ushort Index, double Celsius);

internal static class StorageTemperatureParser
{
    public static IReadOnlyList<StorageTemperatureValue> Parse(ReadOnlySpan<byte> bytes)
    {
        // winioctl.h: descriptor header 24 bytes, each STORAGE_TEMPERATURE_INFO 16 bytes.
        if (bytes.Length < 24) throw new InvalidDataException("Truncated storage-temperature header.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(12, 2));
        if (size < 24 || size > bytes.Length || count > 128 || 24 + count * 16 > size)
            throw new InvalidDataException("Invalid storage-temperature descriptor size or count.");
        var temperatures = new List<StorageTemperatureValue>(count);
        var indices = new HashSet<ushort>();
        for (var index = 0; index < count; index++)
        {
            var offset = 24 + index * 16;
            var sensorIndex = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
            var celsius = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset + 2, 2));
            if (!indices.Add(sensorIndex)) throw new InvalidDataException("Duplicate storage-temperature index.");
            if (celsius == short.MinValue) continue; // STORAGE_TEMPERATURE_VALUE_NOT_REPORTED
            if (celsius is < -40 or > 150) continue; // Reject invalid driver values, not legitimate zero.
            temperatures.Add(new(sensorIndex, celsius));
        }
        return temperatures;
    }
}
