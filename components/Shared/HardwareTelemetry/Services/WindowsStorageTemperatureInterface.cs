using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

public sealed class WindowsStorageTemperatureInterface : IDisposable
{
    private static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
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
                try
                {
                    var query = new byte[12]; // STORAGE_PROPERTY_QUERY, PropertyStandardQuery = 0.
                    BinaryPrimitives.WriteUInt32LittleEndian(query, 52); // StorageDeviceTemperatureProperty
                    byte[] bytes;
                    try { bytes = device.Handle.Query(0x2d1400, 4096, query); }
                    catch (Win32Exception exception) when (exception.NativeErrorCode == 1117)
                    {
                        // The storage driver can reject a concurrent SMART query transiently.
                        // One bounded read-only retry; preserve evidence of the failure.
                        probes.Add(ReadOnlyNativeDevice.Failure("Windows Storage", device.Name + "/temperature-attempt-1", exception));
                        bytes = device.Handle.Query(0x2d1400, 4096, query);
                    }
                    var values = StorageTemperatureParser.Parse(bytes);
                    foreach (var value in values)
                        readings.Add(new($"/windows/storage-temperature/{device.Id}/{value.Index}", device.Name,
                            "Storage", value.Index == 0 ? "Drive Temperature" : $"Temperature Sensor {value.Index}",
                            "Temperature", value.Celsius, "°C"));
                    probes.Add(new("Windows Storage", device.Name + "/temperature",
                        values.Count == 0 ? SensorInterfaceStatus.Unsupported : SensorInterfaceStatus.Readable,
                        values.Count == 0 ? "Temperature property returned no valid sensor values." : $"QUERY_PROPERTY returned {values.Count} temperature sensors; no elevated access."));
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
                {
                    probes.Add(ReadOnlyNativeDevice.Failure("Windows Storage", device.Name + "/temperature", exception));
                }
            }
            return new(readings, probes);
        }
    }

    private void Initialize()
    {
        _initialized = true;
        try
        {
            var paths = ReadOnlyNativeDevice.Enumerate(DiskInterface);
            if (paths.Length == 0)
                _initialProbes.Add(new("Windows Storage", "enumeration", SensorInterfaceStatus.NoInterface, "No disk interface found."));
            foreach (var path in paths)
            {
                ReadOnlyNativeDevice? handle = null;
                var id = ReadOnlyNativeDevice.DeviceId(path);
                try
                {
                    // Query IOCTL is FILE_ANY_ACCESS. Do not request unnecessary disk read/write access.
                    handle = new(path, requiresReadAccess: false);
                    var name = "Storage " + id;
                    try
                    {
                        var descriptor = handle.Query(0x2d1400, 4096, new byte[12]);
                        if (descriptor.Length >= 36)
                        {
                            var offset = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(16, 4));
                            if (offset >= 36 && offset < descriptor.Length)
                            {
                                var text = Encoding.ASCII.GetString(descriptor.AsSpan((int)offset)).Split('\0')[0].Trim();
                                if (text.Length is > 0 and < 128) name = text;
                            }
                        }
                    }
                    catch (Win32Exception exception)
                    {
                        // A missing friendly model name must not hide the independent temperature query.
                        _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Storage", id + "/model", exception));
                    }
                    _devices.Add(new(id, name, handle));
                    handle = null;
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
                {
                    _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Storage", id + "/open", exception));
                }
                finally { handle?.Dispose(); }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
        {
            _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows Storage", "enumeration", exception));
        }
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

    private sealed record Device(string Id, string Name, ReadOnlyNativeDevice Handle);
}
