using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Standard Windows temperature IOCTL, immediate read only. No policy or cooling writes.
public sealed class WindowsThermalInterface : IDisposable
{
    private readonly object _gate = new();
    private readonly List<(string Id, ReadOnlyNativeDevice Handle)> _devices = [];
    private readonly List<SensorInterfaceProbe> _initialProbes = [];
    private bool _initialized;
    private bool _disposed;

    public SensorInterfaceCapture Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized) Initialize();
            var readings = new List<HardwareSensorReading>();
            var probes = _initialProbes.ToList();
            foreach (var (id, handle) in _devices)
            {
                try
                {
                    var bytes = handle.Query(0x294090, 4, new byte[12]); // THERMAL_WAIT_READ.Timeout = 0.
                    if (bytes.Length != 4) throw new InvalidDataException($"Native thermal query returned {bytes.Length} bytes; expected 4. No reading is available.");
                    var celsius = BatteryTelemetryParser.ParseTemperature(bytes);
                    readings.Add(new($"/windows/thermal/{id}", "Windows Thermal " + id[..6], "Platform",
                        "Thermal Zone Temperature", "Temperature", celsius, "°C"));
                    probes.Add(new("Windows Thermal", id + "/temperature", SensorInterfaceStatus.Readable,
                        "Immediate native tenths-Kelvin query; zone identity is not guessed as CPU/VRM/battery."));
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
                { probes.Add(ReadOnlyNativeDevice.Failure("Windows Thermal", id + "/temperature", exception)); }
            }
            return new(readings, probes);
        }
    }

    private void Initialize()
    {
        _initialized = true;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, guid) in new[] {
            ("temperature-sensor", new Guid("2a6c8538-7895-4d56-8567-795d3844858a")),
            ("thermal-zone", new Guid("4afa3d51-74a7-11d0-be5e-00a0c9062857")) })
        {
            try
            {
                var paths = ReadOnlyNativeDevice.Enumerate(guid);
                if (paths.Length == 0) _initialProbes.Add(new("Windows Thermal", name,
                    SensorInterfaceStatus.NoInterface, "No present interface in this class."));
                foreach (var path in paths)
                {
                    if (!seen.Add(path)) continue;
                    var id = ReadOnlyNativeDevice.DeviceId(path);
                    try { _devices.Add((id, new(path, true))); }
                    catch (Win32Exception exception) { _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Thermal", id + "/open", exception)); }
                }
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
            { _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Thermal", name + "/enumeration", exception)); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var (_, handle) in _devices) handle.Dispose();
            _devices.Clear();
            _disposed = true;
        }
    }
}
