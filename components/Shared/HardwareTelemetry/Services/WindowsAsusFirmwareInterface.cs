using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Diagnostic-only fixed named DSTS endpoints. GEN_READ only, not the vendor control app.
public static class WindowsAsusFirmwareInterface
{
    public static AsusFirmwareCapture Capture()
    {
        try
        {
            using var device = new ReadOnlyNativeDevice(@"\\.\ATKACPI", requiresReadAccess: true);
            return CaptureDevice(device.Query);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException)
        {
            return new([], [], [ReadOnlyNativeDevice.Failure("ASUS DSTS", "open/read-only", exception)]);
        }
    }

    internal static AsusFirmwareCapture CaptureDevice(Func<uint, int, byte[]?, byte[]> query)
    {
        var readings = new List<HardwareSensorReading>();
        var endpoints = new List<AsusFirmwareEndpoint>();
        var probes = new List<SensorInterfaceProbe>();
        foreach (var endpoint in AsusDstsParser.Endpoints)
        {
            var id = endpoint.Id.ToString("x8");
            try
            {
                var bytes = query(AsusDstsParser.ControlCode, 16, AsusDstsParser.Request(endpoint.Id));
                endpoints.Add(new(id, endpoint.Name, bytes.Length,
                    bytes.Length >= 4 ? $"0x{AsusDstsParser.RawStatus(bytes):x8}" : null));
                var raw = AsusDstsParser.RawStatus(bytes);
                var reading = AsusDstsParser.Parse(endpoint, raw);
                if (reading is not null) readings.Add(reading);
                probes.Add(new("ASUS DSTS", id, SensorInterfaceStatus.Readable,
                    reading is null ? "AC presence state only; not input voltage/current/power."
                        : "Fixed named GET endpoint; validated bytes, presence/sentinel and units."));
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException or NotSupportedException)
            {
                probes.Add(ReadOnlyNativeDevice.Failure("ASUS DSTS", id, exception));
            }
        }
        return new(readings, endpoints, probes);
    }
}
