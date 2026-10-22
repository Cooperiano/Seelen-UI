using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

public sealed class BluetoothMetricsReader
{
    public IReadOnlyList<BluetoothDeviceMetric> Capture()
    {
        var search = new BluetoothDeviceSearchParams
        {
            Size = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = true,
            ReturnConnected = true,
            IssueInquiry = false,
            TimeoutMultiplier = 0,
            RadioHandle = nint.Zero
        };
        var device = new BluetoothDeviceInfo { Size = Marshal.SizeOf<BluetoothDeviceInfo>() };
        var findHandle = BluetoothFindFirstDevice(ref search, ref device);
        if (findHandle == nint.Zero)
        {
            return [];
        }

        var devices = new List<BluetoothDeviceMetric>();
        try
        {
            do
            {
                if (!string.IsNullOrWhiteSpace(device.Name))
                {
                    devices.Add(new BluetoothDeviceMetric(
                        device.Name.Trim(),
                        device.Connected,
                        device.Authenticated,
                        device.Remembered,
                        ToDateTimeOffset(device.LastSeen)));
                }
                device = new BluetoothDeviceInfo { Size = Marshal.SizeOf<BluetoothDeviceInfo>() };
            }
            while (BluetoothFindNextDevice(findHandle, ref device));
        }
        finally
        {
            BluetoothFindDeviceClose(findHandle);
        }

        return devices
            .OrderByDescending(item => item.IsConnected)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset? ToDateTimeOffset(SystemTime value)
    {
        if (value.Year < 1601 || value.Month is < 1 or > 12 || value.Day is < 1 or > 31)
        {
            return null;
        }
        try
        {
            return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, TimeSpan.Zero).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern nint BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams searchParams, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(nint findHandle, ref BluetoothDeviceInfo deviceInfo);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(nint findHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public nint RadioHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }
}
