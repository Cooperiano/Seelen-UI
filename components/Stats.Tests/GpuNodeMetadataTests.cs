using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class GpuNodeMetadataTests
{
    public static void Run()
    {
        if (nint.Size == 8 && (Marshal.SizeOf<WindowsGpuNodeMetadataInterface.OpenAdapter>() != 12 ||
            Marshal.SizeOf<WindowsGpuNodeMetadataInterface.CloseAdapter>() != 4 ||
            Marshal.SizeOf<WindowsGpuNodeMetadataInterface.QueryAdapterInfo>() != 24 ||
            Marshal.OffsetOf<WindowsGpuNodeMetadataInterface.QueryAdapterInfo>("Data").ToInt32() != 8 ||
            Marshal.OffsetOf<WindowsGpuNodeMetadataInterface.QueryAdapterInfo>("DataSize").ToInt32() != 16))
            throw new InvalidOperationException("D3DKMT Windows x64 ABI mismatch.");
        var candidate = new GpuEngineTelemetry("FEDCBA98:01234567", 2, 3, "3D", 0);
        var bytes = new byte[78];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x00020003);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        Encoding.Unicode.GetBytes("Shared graphics engine").CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(72), 0x80000001);
        bytes[76] = 1;
        var node = WindowsGpuNodeMetadataInterface.Parse(candidate, bytes);
        if (node is not { NativeEngineTypeName: "3D", FriendlyName: "Shared graphics engine",
            PhysicalAdapter: 2, Node: 3, GpuMmuSupported: true, IoMmuSupported: false, CounterUtilizationPercent: 0 } ||
            node.Flags != 0x80000001 || node.CounterEngineType != "3D")
            throw new InvalidOperationException("Packed node metadata identity/Unicode/flags/valid-zero decoding failed.");
        if (!WindowsGpuNodeMetadataInterface.TryParseLuid(candidate.AdapterLuid, out var low, out var high) ||
            low != 0x01234567 || unchecked((uint)high) != 0xFEDCBA98 ||
            WindowsGpuNodeMetadataInterface.TryParseLuid("bad-luid", out _, out _))
            throw new InvalidOperationException("High/low LUID ordering or signed high bits failed.");
        foreach (var bad in new[] { candidate with { Engine = -1 }, candidate with { PhysicalAdapter = 64 },
            candidate with { Engine = 256 }, candidate with { UtilizationPercent = double.NaN }, candidate with { UtilizationPercent = 101 } })
            if (WindowsGpuNodeMetadataInterface.IsValidCandidate(bad)) throw new InvalidOperationException("Unsafe/unverified node candidate accepted.");
        Reject(bytes[..77]);
        var malformed = bytes.ToArray(); malformed[0] = 4; Reject(malformed);
        malformed = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(4), 10); Reject(malformed);
        malformed = bytes.ToArray(); Array.Fill(malformed, (byte)0x41, 8, 64); Reject(malformed);
        malformed = bytes.ToArray(); malformed[8] = 0; malformed[9] = 0xD8; malformed[10] = 0; malformed[11] = 0; Reject(malformed);
        malformed = bytes.ToArray(); malformed[77] = 2; Reject(malformed);
        if (WindowsGpuNodeMetadataInterface.Failure("fixture", unchecked((int)0xC0000022)).Status != SensorInterfaceStatus.PermissionDenied ||
            WindowsGpuNodeMetadataInterface.Failure("fixture", unchecked((int)0xC00000BB)).Status != SensorInterfaceStatus.Unsupported)
            throw new InvalidOperationException("NTSTATUS was confused with a Win32 error or successful output.");
        Console.WriteLine("Passed fixed node-metadata ABI, packed identity/Unicode/boolean/flags, LUID order, bounds, zero and NTSTATUS tests.");
        void Reject(byte[] invalid)
        {
            try { WindowsGpuNodeMetadataInterface.Parse(candidate, invalid); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Invalid GPU node metadata was accepted.");
        }
    }
}
