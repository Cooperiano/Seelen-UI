using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// NVIDIA's installed driver DLL only. No downloads, driver installation, or NVML setters.
public sealed class WindowsNvidiaTelemetryInterface : IDisposable
{
    private readonly object _gate = new();
    private readonly EnergyMeterSession _session = new(Stopwatch.Frequency, 3600);
    private bool _attempted;
    private bool _initialized;
    private bool _disposed;
    private SensorInterfaceProbe? _initialProbe;

    public NvidiaTelemetryCapture Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var devices = new List<NvidiaDeviceTelemetry>();
            var readings = new List<HardwareSensorReading>();
            var probes = new List<SensorInterfaceProbe>();
            if (!_attempted)
            {
                _attempted = true;
                try
                {
                    var code = Api.Init();
                    _initialized = code == 0;
                    _initialProbe = Probe("nvmlInit_v2", code);
                }
                catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    _initialProbe = new("NVIDIA NVML", "initialization", SensorInterfaceStatus.NoInterface, exception.GetType().Name);
                }
            }
            if (_initialProbe is not null) probes.Add(_initialProbe);
            if (!_initialized) return new(devices, readings, probes);
            var countStatus = Api.Count(out var count);
            probes.Add(Probe("nvmlDeviceGetCount_v2", countStatus));
            if (countStatus != 0 || count > 32) return new(devices, readings, probes);
            for (uint index = 0; index < count; index++)
            {
                var handleStatus = Api.Handle(index, out var handle);
                var prefix = $"GPU {index}/";
                probes.Add(Probe(prefix + "nvmlDeviceGetHandleByIndex_v2", handleStatus));
                if (handleStatus != 0) continue;
                var nameBuffer = new StringBuilder(128);
                var nameCode = Api.Name(handle, nameBuffer, 128);
                probes.Add(Probe(prefix + "nvmlDeviceGetName", nameCode));
                var name = nameCode == 0 ? nameBuffer.ToString() : $"NVIDIA GPU {index}";
                var cores = Read<uint>(prefix + "cores", (out uint value) => Api.Cores(handle, out value), probes);
                var temperature = Read<uint>(prefix + "temperature", (out uint value) => Api.Temperature(handle, 0, out value), probes);
                var power = Read<uint>(prefix + "power", (out uint value) => Api.Power(handle, out value), probes);
                var energy = Read<ulong>(prefix + "energy", (out ulong value) => Api.Energy(handle, out value), probes);
                var usage = Read<Utilization>(prefix + "utilization", (out Utilization value) => Api.Utilization(handle, out value), probes);
                var encoder = Read<uint>(prefix + "encoder", (out uint value) => Api.Encoder(handle, out value, out _), probes);
                var decoder = Read<uint>(prefix + "decoder", (out uint value) => Api.Decoder(handle, out value, out _), probes);
                var memory = Read<Memory>(prefix + "memory", (out Memory value) => Api.Memory(handle, out value), probes);
                var graphicsClock = Read<uint>(prefix + "graphics-clock", (out uint value) => Api.Clock(handle, 0, out value), probes);
                var memoryClock = Read<uint>(prefix + "memory-clock", (out uint value) => Api.Clock(handle, 2, out value), probes);
                var pcieGeneration = Read<uint>(prefix + "pcie-generation", (out uint value) => Api.PcieGeneration(handle, out value), probes);
                var pcieWidth = Read<uint>(prefix + "pcie-width", (out uint value) => Api.PcieWidth(handle, out value), probes);
                var fields = ReadFields(handle, prefix);
                probes.AddRange(fields.Select(field => field.Probe));
                devices.Add(new(index, name, cores, temperature, power / 1000d, energy, usage?.Gpu, usage?.Memory,
                    encoder, decoder, memory?.Total, memory?.Used, graphicsClock, memoryClock, pcieGeneration, pcieWidth, fields));
                foreach (var field in fields.Where(field => field.Probe.Status == SensorInterfaceStatus.Readable && field.Value is not null))
                    readings.Add(new($"/nvidia/nvml/{index}/field/{field.FieldId}/scope/{field.ScopeId}", name,
                        "GpuNvidia", field.Name, field.Type, field.Value!.Value, field.Unit));
                if (energy is { } milliJoules && milliJoules > 0 &&
                    _session.Observe(index + "/" + name, milliJoules, (ulong)Stopwatch.GetTimestamp()) is { } session)
                {
                    var id = $"/nvidia/nvml/{index}";
                    readings.Add(new(id + "/session-energy", name, "GpuNvidia",
                        "GPU Package · Session Energy", "Energy", session.MilliWattHours, "mWh"));
                    readings.Add(new(id + "/session-average", name, "GpuNvidia",
                        "GPU Package · Session Average", "Power", session.AverageWatts, "W"));
                }
            }
            return new(devices, readings, probes);
        }
    }

    private static IReadOnlyList<NvidiaSensorField> ReadFields(IntPtr handle, string prefix)
    {
        var fields = NvidiaFieldParser.Fields.Select(definition => new NvidiaFieldParser.NativeField
        { FieldId = definition.Id, ScopeId = 0, ReturnCode = 3 }).ToArray();
        try
        {
            var code = Api.Fields(handle, fields.Length, fields);
            if (code != 0)
                return fields.Select((field, index) => NvidiaFieldParser.Parse(new NvidiaFieldParser.NativeField
                { FieldId = NvidiaFieldParser.Fields[index].Id, ReturnCode = code }, NvidiaFieldParser.Fields[index].Id, prefix)).ToList();
            return fields.Select((field, index) => NvidiaFieldParser.Parse(field, NvidiaFieldParser.Fields[index].Id, prefix)).ToList();
        }
        catch (EntryPointNotFoundException)
        {
            return fields.Select(field => NvidiaFieldParser.Parse(field, field.FieldId, prefix)).ToList();
        }
    }

    private delegate int Query<T>(out T value);
    private static T? Read<T>(string endpoint, Query<T> query, List<SensorInterfaceProbe> probes) where T : struct
    {
        try
        {
            var code = query(out var value);
            probes.Add(Probe(endpoint, code));
            return code == 0 ? value : null;
        }
        catch (EntryPointNotFoundException)
        {
            probes.Add(new("NVIDIA NVML", endpoint, SensorInterfaceStatus.Unsupported, "Export not present in the installed driver."));
            return null;
        }
    }

    private static SensorInterfaceProbe Probe(string endpoint, int code) => DescribeStatus(endpoint, code);

    internal static SensorInterfaceProbe DescribeStatus(string endpoint, int code) =>
        new("NVIDIA NVML", endpoint, code switch
        {
            0 => SensorInterfaceStatus.Readable,
            3 => SensorInterfaceStatus.Unsupported,
            4 => SensorInterfaceStatus.PermissionDenied,
            _ => SensorInterfaceStatus.Failed
        }, code == 0 ? "Driver returned a value." : $"NVML status {code}; no fabricated fallback value.", code == 0 ? null : code);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_initialized) Api.Shutdown();
            _initialized = false;
            _disposed = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu; public uint Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total; public ulong Free; public ulong Used; }
    private static class Api
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Init();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)] internal static extern int Shutdown();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Count(out uint count);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)] internal static extern int Handle(uint index, out IntPtr handle);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] internal static extern int Name(IntPtr handle, StringBuilder name, uint length);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetNumGpuCores", CallingConvention = CallingConvention.Cdecl)] internal static extern int Cores(IntPtr handle, out uint cores);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)] internal static extern int Temperature(IntPtr handle, uint sensor, out uint value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)] internal static extern int Power(IntPtr handle, out uint value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTotalEnergyConsumption", CallingConvention = CallingConvention.Cdecl)] internal static extern int Energy(IntPtr handle, out ulong value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)] internal static extern int Utilization(IntPtr handle, out Utilization value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEncoderUtilization", CallingConvention = CallingConvention.Cdecl)] internal static extern int Encoder(IntPtr handle, out uint value, out uint samplingPeriod);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetDecoderUtilization", CallingConvention = CallingConvention.Cdecl)] internal static extern int Decoder(IntPtr handle, out uint value, out uint samplingPeriod);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo", CallingConvention = CallingConvention.Cdecl)] internal static extern int Memory(IntPtr handle, out Memory value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo", CallingConvention = CallingConvention.Cdecl)] internal static extern int Clock(IntPtr handle, uint type, out uint value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkGeneration", CallingConvention = CallingConvention.Cdecl)] internal static extern int PcieGeneration(IntPtr handle, out uint value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkWidth", CallingConvention = CallingConvention.Cdecl)] internal static extern int PcieWidth(IntPtr handle, out uint value);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFieldValues", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Fields(IntPtr handle, int count, [In, Out] NvidiaFieldParser.NativeField[] values);
    }
}
