using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

/// <summary>Read-only battery query interface. Positive rate is charging, negative is discharging.</summary>
public sealed class WindowsBatteryTelemetryInterface : IDisposable
{
    private static readonly Guid BatteryInterface = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private readonly object _gate = new();
    private readonly List<Device> _devices = [];
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
            foreach (var device in _devices)
            {
                var id = device.Id;
                var handle = device.Handle;
                try
                {
                    var tagBytes = handle.Query(0x294040, 4, new byte[4]); // Timeout = 0.
                    if (tagBytes.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(tagBytes) == 0)
                        throw new InvalidDataException("Battery tag not available.");
                    var informationQuery = new byte[12];
                    tagBytes.CopyTo(informationQuery, 0);
                    var tag = BinaryPrimitives.ReadUInt32LittleEndian(tagBytes);
                    if (device.Tag != tag)
                    {
                        device.Information = handle.Query(0x294044, 36, informationQuery);
                        device.Tag = tag;
                        device.TemperatureFailure = null;
                    }
                    var information = device.Information;
                    if (information.Length != 36) throw new InvalidDataException("Invalid BATTERY_INFORMATION length.");
                    var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(information);
                    var relative = (capabilities & 0x40000000) != 0;
                    probes.Add(new("Windows Battery", id + "/information", SensorInterfaceStatus.Readable,
                        relative ? "Capacity/rate are relative units; not exposed as mWh or W." : "Capacity/rate use mWh/mW; signed charging/discharging rate."));

                    // A failed temperature query must not hide independent status values.
                    if (device.TemperatureFailure is { } previousFailure) probes.Add(previousFailure);
                    else try
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(informationQuery.AsSpan(4), 2);
                        var temperature = BatteryTelemetryParser.ParseTemperature(handle.Query(0x294044, 4, informationQuery));
                        Add("temperature", "Battery Temperature", "Temperature", temperature, "°C");
                        probes.Add(new("Windows Battery", id + "/temperature", SensorInterfaceStatus.Readable,
                            "Native tenths-Kelvin reading converted to Celsius."));
                    }
                    catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
                    {
                        var failure = ReadOnlyNativeDevice.Failure("Windows Battery", id + "/temperature", exception);
                        probes.Add(failure);
                        // Optional endpoint unsupported for this battery tag: do not repeatedly query it.
                        // Transient failures are not cached; a changed battery tag resets capabilities.
                        if (failure.Status == SensorInterfaceStatus.Unsupported) device.TemperatureFailure = failure;
                    }

                    var waitStatus = new byte[20];
                    tagBytes.CopyTo(waitStatus, 0); // Timeout 0: never wait for a state change.
                    var state = BatteryTelemetryParser.ParseStatus(handle.Query(0x29404c, 16, waitStatus), relative);
                    if (state.VoltageVolts is { } volts) Add("voltage", "Battery Voltage", "Voltage", volts, "V");
                    if (state.SignedPowerWatts is { } watts) Add("power", "Battery Charge/Discharge Rate", "Power", watts, "W");
                    if (state.CalculatedCurrentAmps is { } current) Add("current", "Battery Current (calculated P/V)", "Current", current, "A");
                    if (state.RemainingMilliWattHours is { } capacity) Add("capacity", "Remaining Capacity", "Energy", capacity, "mWh");
                    probes.Add(new("Windows Battery", id + "/status", SensorInterfaceStatus.Readable,
                        $"PowerState=0x{state.PowerState:x}; voltage={(state.VoltageVolts is null ? "unavailable" : "readable")}; rate={(state.SignedPowerWatts is null ? "unavailable" : "readable")}; current is calculated P/V, not an independent current sensor."));

                    void Add(string key, string name, string type, double value, string unit) =>
                        readings.Add(new($"/windows/battery/{id}/{key}", "Windows Battery", "Battery", name, type, value, unit));
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
                { probes.Add(ReadOnlyNativeDevice.Failure("Windows Battery", id + "/capture", exception)); }
            }
            return new(readings, probes);
        }
    }

    private void Initialize()
    {
        _initialized = true;
        try
        {
            var paths = ReadOnlyNativeDevice.Enumerate(BatteryInterface);
            if (paths.Length == 0) _initialProbes.Add(new("Windows Battery", "enumeration",
                SensorInterfaceStatus.NoInterface, "No battery device interface found."));
            foreach (var path in paths)
            {
                var id = ReadOnlyNativeDevice.DeviceId(path);
                try { _devices.Add(new(id, new(path, requiresReadAccess: true))); }
                catch (Win32Exception exception) { _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Battery", id + "/open", exception)); }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
        { _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Battery", "enumeration", exception)); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var device in _devices) device.Handle.Dispose();
            _devices.Clear();
            _disposed = true;
        }
    }

    private sealed class Device(string id, ReadOnlyNativeDevice handle)
    {
        public string Id { get; } = id;
        public ReadOnlyNativeDevice Handle { get; } = handle;
        public uint Tag { get; set; }
        public byte[] Information { get; set; } = [];
        public SensorInterfaceProbe? TemperatureFailure { get; set; }
    }
}
