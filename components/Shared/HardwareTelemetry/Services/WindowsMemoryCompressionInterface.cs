using System.Buffers.Binary;
using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

/// <summary>Fixed read-only MemCompressionInfoRequest; never exposes store trim/configuration operations.</summary>
public static class WindowsMemoryCompressionInterface
{
    public static MemoryCompressionCapture Capture()
    {
        var size = nint.Size == 8 ? 40 : 24; // System Informer/phnt V1 (version 3), without V2 partition handle.
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var bytes = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 3);
            Marshal.Copy(bytes, 0, buffer, size);
            var envelope = new StoreInformation { Version = 1, InformationClass = 22, Data = buffer, Length = (uint)size };
            var status = NtQuerySystemInformation(109, ref envelope, (uint)Marshal.SizeOf<StoreInformation>(), out _);
            if (status < 0)
                return new(null, new("Windows Compression Store", "MemCompressionInfoRequest V1", DescribeStatus(status),
                    $"NtQuerySystemInformation: 0x{status:X8}; no privilege or OS configuration was changed.", status));
            Marshal.Copy(buffer, bytes, 0, size);
            return new(Parse(bytes, nint.Size), new("Windows Compression Store", "MemCompressionInfoRequest V1", SensorInterfaceStatus.Readable,
                "Compression-store resident bytes and raw compressed-data counters; not macOS compressed-memory semantics."));
        }
        catch (Exception exception) when (exception is InvalidDataException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new(null, new("Windows Compression Store", "MemCompressionInfoRequest V1",
                exception is InvalidDataException ? SensorInterfaceStatus.InvalidPayload : SensorInterfaceStatus.Unsupported, exception.Message));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static SensorInterfaceStatus DescribeStatus(int status) => unchecked((uint)status) switch
    {
        0xC0000022 or 0xC0000061 => SensorInterfaceStatus.PermissionDenied,
        0xC0000002 or 0xC0000003 or 0xC00000BB => SensorInterfaceStatus.Unsupported,
        0xC0000004 or 0xC000000D => SensorInterfaceStatus.InvalidPayload,
        _ => SensorInterfaceStatus.Failed
    };

    internal static MemoryCompressionTelemetry Parse(ReadOnlySpan<byte> bytes, int pointerSize)
    {
        if ((pointerSize != 4 && pointerSize != 8) || bytes.Length != (pointerSize == 8 ? 40 : 24) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 3)
            throw new InvalidDataException("Invalid compression store V1 buffer length/version.");
        var offset = pointerSize == 8 ? 16 : 12;
        return new(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            ReadSize(bytes[offset..], pointerSize), ReadSize(bytes[(offset + pointerSize)..], pointerSize),
            ReadSize(bytes[(offset + pointerSize * 2)..], pointerSize));
    }
    private static ulong ReadSize(ReadOnlySpan<byte> bytes, int size) => size == 8
        ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    [StructLayout(LayoutKind.Sequential)]
    internal struct StoreInformation
    {
        public uint Version, InformationClass;
        public nint Data;
        public uint Length;
    }
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQuerySystemInformation(int informationClass, ref StoreInformation information, uint length, out uint returnedLength);
}
