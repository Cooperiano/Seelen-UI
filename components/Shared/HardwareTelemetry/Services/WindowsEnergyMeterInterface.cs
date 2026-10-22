using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Reusable native EMI wrapper. Version, units and all channels are read from the driver.
public sealed class WindowsEnergyMeterInterface : IDisposable
{
    private static readonly Guid InterfaceClass = new("45bd8344-7ed6-49cf-a440-c276c933b053");
    private readonly object _gate = new();
    private readonly List<Device> _devices = [];
    private readonly List<SensorInterfaceProbe> _initialProbes = [];
    private bool _initialized;
    private bool _disposed;

    public EnergyMeterInterfaceCapture Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_initialized) Initialize();
            var snapshots = new List<EnergyMeterDevice>();
            var probes = _initialProbes.ToList();
            foreach (var device in _devices)
            {
                try
                {
                    var bytes = device.Handle.Query(0x22400c, device.Metadata.Channels.Count * 16);
                    if (bytes.Length != device.Metadata.Channels.Count * 16)
                        throw new InvalidDataException("Truncated EMI measurement channels.");
                    var channels = device.Metadata.Channels.Select((channel, index) => new EnergyMeterChannel(
                        channel.Name, channel.Unit,
                        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index * 16, 8)),
                        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(index * 16 + 8, 8)))).ToList();
                    snapshots.Add(new(device.Id, device.Version, device.Metadata.Manufacturer, device.Metadata.Model, channels));
                    foreach (var channel in channels)
                        probes.Add(new("Windows EMI", channel.Name,
                            channel.Unit == 0 ? SensorInterfaceStatus.Readable : SensorInterfaceStatus.Unsupported,
                            channel.Unit == 0 ? "GET_MEASUREMENT; pWh; 100 ns; counter liveness needs two samples." : $"Unknown unit {channel.Unit}."));
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidDataException)
                {
                    probes.Add(ReadOnlyNativeDevice.Failure("Windows EMI", device.Id + "/measurement", exception));
                }
            }
            return new(snapshots, probes);
        }
    }

    private void Initialize()
    {
        _initialized = true;
        try
        {
            var paths = ReadOnlyNativeDevice.Enumerate(InterfaceClass);
            if (paths.Length == 0)
                _initialProbes.Add(new("Windows EMI", "enumeration", SensorInterfaceStatus.NoInterface, "No energy-meter device interface."));
            foreach (var path in paths)
            {
                ReadOnlyNativeDevice? handle = null;
                var id = ReadOnlyNativeDevice.DeviceId(path);
                try
                {
                    handle = new(path, requiresReadAccess: true);
                    var versionBytes = handle.Query(0x224000, 2);
                    var sizeBytes = handle.Query(0x224004, 4);
                    if (versionBytes.Length != 2 || sizeBytes.Length != 4)
                        throw new InvalidDataException("Truncated EMI version or metadata size.");
                    var version = BinaryPrimitives.ReadUInt16LittleEndian(versionBytes);
                    var size = BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);
                    if (size is < 68 or > 65536) throw new InvalidDataException("Invalid EMI metadata size.");
                    var bytes = handle.Query(0x224008, (int)size);
                    if (bytes.Length != size) throw new InvalidDataException("Truncated EMI metadata.");
                    var metadata = NativeEnergyMeterParser.ParseMetadata(version, bytes);
                    _devices.Add(new(id, handle, version, metadata));
                    handle = null; // Ownership is transferred only after successful validation.
                    _initialProbes.Add(new("Windows EMI", id + "/metadata", SensorInterfaceStatus.Readable,
                        $"GET_VERSION + GET_METADATA_SIZE + GET_METADATA; v{version}; {metadata.Channels.Count} channels."));
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException or NotSupportedException)
                {
                    _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows EMI", id + "/metadata", exception));
                }
                finally { handle?.Dispose(); }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
        {
            _initialProbes.Add(ReadOnlyNativeDevice.Failure("Windows EMI", "enumeration", exception));
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

    private sealed record Device(string Id, ReadOnlyNativeDevice Handle, ushort Version, EnergyDeviceMetadata Metadata);
}
