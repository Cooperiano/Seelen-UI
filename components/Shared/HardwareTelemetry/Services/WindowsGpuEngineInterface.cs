using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

/// <summary>WDDM engine counters plus DXGI LUID identities; no rendering device or workload is created.</summary>
public sealed class WindowsGpuEngineInterface
{
    private IReadOnlyList<GpuAdapterIdentity> _adapters = [];
    private DateTimeOffset _lastIdentityRead = DateTimeOffset.MinValue;
    private SensorInterfaceProbe _identityProbe = new("DXGI", "EnumAdapters1/GetDesc1", SensorInterfaceStatus.NoInterface, "Not yet enumerated.");
    public GpuEngineCapture Capture()
    {
        var probes = new List<SensorInterfaceProbe>();
        var rows = new List<GpuEngineTelemetry>();
        try
        {
            if (DateTimeOffset.UtcNow - _lastIdentityRead >= TimeSpan.FromSeconds(30))
            {
                _lastIdentityRead = DateTimeOffset.UtcNow;
                _adapters = EnumerateAdapters();
                _identityProbe = new("DXGI", "EnumAdapters1/GetDesc1", _adapters.Count > 0 ? SensorInterfaceStatus.Readable : SensorInterfaceStatus.NoInterface,
                    $"{_adapters.Count} adapter identities.");
            }
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Do not attach stale names after a failed identity refresh.
            _adapters = [];
            _identityProbe = new("DXGI", "EnumAdapters1/GetDesc1", SensorInterfaceStatus.Failed, exception.Message);
        }
        probes.Add(_identityProbe);
        try
        {
            using var query = new ManagementObjectSearcher("ROOT\\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = query.Get();
            foreach (ManagementBaseObject item in results)
            using (item)
                if (item["Name"] is { } name && item["UtilizationPercentage"] is { } usage &&
                    GpuEngineParser.Parse(name.ToString()!, Convert.ToDouble(usage, CultureInfo.InvariantCulture)) is { } row) rows.Add(row);
            var engines = GpuEngineParser.Aggregate(rows);
            probes.Add(new("WDDM", "GPU Engine", engines.Count > 0 ? SensorInterfaceStatus.Readable : SensorInterfaceStatus.NoInterface,
                $"{rows.Count} process engine rows; {engines.Count} distinct engines. Idle zero is valid. Engine types are driver labels, not inferred compute/AI categories."));
            return new(engines, _adapters, probes);
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            probes.Add(new("WDDM", "GPU Engine", exception is UnauthorizedAccessException or ManagementException { ErrorCode: ManagementStatus.AccessDenied }
                ? SensorInterfaceStatus.PermissionDenied : SensorInterfaceStatus.Failed, exception.Message));
            return new([], _adapters, probes);
        }
    }

    private static IReadOnlyList<GpuAdapterIdentity> EnumerateAdapters()
    {
        var iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var values = new List<GpuAdapterIdentity>();
            var enumerate = Method<EnumAdaptersDelegate>(factory, 12);
            for (uint index = 0; index < 256; index++)
            {
                var result = enumerate(factory, index, out var adapter);
                if (result == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescDelegate>(adapter, 10)(adapter, out var desc));
                    values.Add(new($"{desc.LuidHigh:X8}:{desc.LuidLow:X8}", desc.Description.TrimEnd('\0'),
                        desc.VendorId, desc.DeviceId, (desc.Flags & 2) != 0));
                }
                finally { Marshal.Release(adapter); }
            }
            return values;
        }
        finally { Marshal.Release(factory); }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersDelegate(nint factory, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(nint adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh, Flags;
    }
}
