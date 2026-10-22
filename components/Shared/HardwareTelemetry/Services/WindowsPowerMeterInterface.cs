using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Opt-in native PMI diagnosis. No setters, event subscriptions, elevation or production aliasing.
public static class WindowsPowerMeterInterface
{
    private static readonly Guid InterfaceClass = new("e849804e-c719-43d8-ac88-96b894c191e2");

    public static PowerMeterCapture Capture()
    {
        var devices = new List<PowerMeterDevice>();
        var probes = new List<SensorInterfaceProbe>();
        try
        {
            var paths = ReadOnlyNativeDevice.Enumerate(InterfaceClass);
            if (paths.Length == 0)
                probes.Add(new("Windows PMI", "enumeration", SensorInterfaceStatus.NoInterface, "No native power-meter device interface."));
            foreach (var path in paths)
            {
                var id = ReadOnlyNativeDevice.DeviceId(path);
                try
                {
                    using var device = new ReadOnlyNativeDevice(path, requiresReadAccess: true);
                    devices.Add(CaptureDevice(id, device.Query, probes));
                }
                catch (Exception exception) when (Expected(exception))
                {
                    probes.Add(ReadOnlyNativeDevice.Failure("Windows PMI", id + "/open", exception));
                }
            }
        }
        catch (Exception exception) when (Expected(exception))
        {
            probes.Add(ReadOnlyNativeDevice.Failure("Windows PMI", "enumeration", exception));
        }
        return new(devices, probes);
    }

    internal static PowerMeterDevice CaptureDevice(string id, Func<uint, int, byte[]?, byte[]> query,
        ICollection<SensorInterfaceProbe> probes)
    {
        PowerMeterCapabilities? capabilities = null;
        PowerMeterHardware? hardware = null;
        uint? averaging = null;
        PowerMeterMeasurementPayload? measurement = null;
        double? watts = null;
        Probe("capabilities", () => capabilities = PowerMeterParser.ParseCapabilities(
            query(PowerMeterParser.GetCapabilities, PowerMeterParser.CapabilitiesSize, PowerMeterParser.CapabilitiesRequest(0))));
        Probe("metered-hardware", () => hardware = PowerMeterParser.ParseHardware(
            query(PowerMeterParser.GetCapabilities, 65536, PowerMeterParser.CapabilitiesRequest(1))));
        Probe("measurement-configuration", () => averaging = PowerMeterParser.ParseConfiguration(
            query(PowerMeterParser.GetConfiguration, 20, PowerMeterParser.ConfigurationRequest())));
        Probe("measurement", () => measurement = PowerMeterParser.ParseMeasurement(
            query(PowerMeterParser.GetMeasurement, 8, null)));
        if (measurement is not null)
            Probe("physical-reading", () => watts = PowerMeterParser.InterpretWatts(measurement, capabilities));
        return new(id, capabilities, hardware, averaging, measurement, watts);

        void Probe(string endpoint, Action read)
        {
            try
            {
                read();
                probes.Add(new("Windows PMI", id + "/" + endpoint, SensorInterfaceStatus.Readable,
                    endpoint == "physical-reading" ? "Validated mW/W conversion; metered scope/direction remain separate metadata, not a System Total/DC-in alias."
                        : endpoint == "measurement" ? "GET succeeded; validated v1 8-byte payload. Payload alone is not verified rail coverage."
                        : "Validated native GET response; no configuration changed."));
            }
            catch (Exception exception) when (Expected(exception))
            {
                probes.Add(ReadOnlyNativeDevice.Failure("Windows PMI", id + "/" + endpoint, exception));
            }
        }
    }

    private static bool Expected(Exception exception) => exception is Win32Exception or IOException or InvalidDataException or NotSupportedException;
}
