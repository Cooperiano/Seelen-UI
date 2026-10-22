using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HardwareTelemetry.Models;
using Microsoft.Win32.SafeHandles;

namespace HardwareTelemetry.Services;

internal sealed class ReadOnlyNativeDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    public ReadOnlyNativeDevice(string path, bool requiresReadAccess)
    {
        _handle = CreateFile(path, requiresReadAccess ? 0x80000000u : 0, 3,
            IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (!_handle.IsInvalid) return;
        var error = Marshal.GetLastWin32Error();
        _handle.Dispose();
        throw new Win32Exception(error, "Opening a read-only device interface failed.");
    }

    // Callers only use GET/QUERY IOCTLs. This wrapper has no device-write operation.
    public byte[] Query(uint code, int outputSize, byte[]? input = null)
    {
        if (outputSize is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(outputSize));
        var output = new byte[outputSize];
        if (!DeviceIoControl(_handle, code, input, (uint)(input?.Length ?? 0), output,
                (uint)output.Length, out var returned, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Read-only IOCTL 0x{code:x8} failed.");
        if (returned > output.Length) throw new InvalidDataException("Driver returned an oversized buffer.");
        return output.AsSpan(0, (int)returned).ToArray();
    }

    public void Dispose() => _handle.Dispose();

    public static string DeviceId(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16].ToLowerInvariant();

    public static string[] Enumerate(Guid interfaceClass)
    {
        // Configuration Manager uses a UTF-16 multi-string; lengths are characters, not bytes.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var status = GetInterfaceListSize(out var length, ref interfaceClass, null, 0);
            if (status != 0) throw new Win32Exception((int)MapError(status, 31), "Device enumeration failed.");
            if (length is < 1 or > 32768) throw new InvalidDataException("Invalid interface-list size.");
            var buffer = new char[length];
            status = GetInterfaceList(ref interfaceClass, null, buffer, length, 0);
            if (status == 26) continue; // CR_BUFFER_SMALL: hotplug between the two calls.
            if (status != 0) throw new Win32Exception((int)MapError(status, 31), "Device enumeration failed.");
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        throw new IOException("Device interface list changed repeatedly during enumeration.");
    }

    public static SensorInterfaceProbe Failure(string provider, string endpoint, Exception exception)
    {
        var code = (exception as Win32Exception)?.NativeErrorCode;
        var status = exception switch
        {
            Win32Exception when code == 5 => SensorInterfaceStatus.PermissionDenied,
            Win32Exception when code is 1 or 50 or 87 => SensorInterfaceStatus.Unsupported,
            InvalidDataException => SensorInterfaceStatus.InvalidPayload,
            NotSupportedException => SensorInterfaceStatus.Unsupported,
            _ => SensorInterfaceStatus.Failed
        };
        return new(provider, endpoint, status, exception.Message, code);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
        byte[]? input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern uint GetInterfaceListSize(out uint length, ref Guid guid, string? device, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode)]
    private static extern uint GetInterfaceList(ref Guid guid, string? device, [Out] char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_MapCrToWin32Err")]
    private static extern uint MapError(uint status, uint fallback);
}
