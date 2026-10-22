using System.ComponentModel;
using System.IO;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Read-only access to the ASUS ACPI DSTS method exposed by the installed ATKACPI driver.
// This intentionally does not scan raw EC registers or issue the mutating DEVS method.
internal sealed class AsusFirmwareSensorReader : IDisposable
{
    private ReadOnlyNativeDevice? _handle;
    private bool _unavailable;

    public IReadOnlyList<HardwareSensorReading> Capture()
    {
        if (_unavailable || !OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            // GEN_READ suffices on the tested installed ATKACPI driver; no write access.
            _handle ??= new ReadOnlyNativeDevice(@"\\.\ATKACPI", requiresReadAccess: true);
            return CaptureDevice(_handle.Query);
        }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException or IOException)
        {
            _unavailable = true;
            _handle?.Dispose();
            _handle = null;
            return [];
        }
    }

    internal static IReadOnlyList<HardwareSensorReading> CaptureDevice(Func<uint, int, byte[]?, byte[]> query)
    {
        var readings = new List<HardwareSensorReading>();
        foreach (var endpoint in AsusDstsParser.Endpoints.Where(item => item.SensorType is not null))
        {
            try
            {
                var bytes = query(AsusDstsParser.ControlCode, 16, AsusDstsParser.Request(endpoint.Id));
                var reading = AsusDstsParser.Parse(endpoint, AsusDstsParser.RawStatus(bytes));
                if (reading is not null) readings.Add(reading);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidDataException or NotSupportedException)
            {
                // A failed optional endpoint must not hide working siblings.
            }
        }
        return readings;
    }

    public void Dispose()
    {
        _unavailable = true;
        _handle?.Dispose();
        _handle = null;
    }

}
