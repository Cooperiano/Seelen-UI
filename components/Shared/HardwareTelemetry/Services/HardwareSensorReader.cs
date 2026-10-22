using HardwareTelemetry.Models;
using LibreHardwareMonitor.Hardware;

namespace HardwareTelemetry.Services;

internal sealed class HardwareSensorReader : IDisposable
{
    private readonly object _gate = new();
    private readonly AsusFirmwareSensorReader _asusFirmware = new();
    private readonly WindowsEnergyMeterReader _windowsEnergy = new();
    private readonly WindowsStorageTemperatureInterface _storageTemperatures = new();
    private readonly WindowsNvidiaTelemetryInterface _nvidiaTelemetry = new();
    private readonly WindowsBatteryTelemetryInterface _batteryTelemetry = new();
    public IReadOnlyList<NvidiaDeviceTelemetry> NvidiaDevices { get; private set; } = [];
    private Computer? _computer;
    private bool _failed;

    public IReadOnlyList<HardwareSensorReading> Capture()
    {
        lock (_gate)
        {
            var readings = new List<HardwareSensorReading>();
            try
            {
                if (!_failed)
                {
                    _computer ??= OpenComputer();
                    foreach (var hardware in _computer.Hardware)
                        CaptureHardware(hardware, readings);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _failed = true;
                _computer?.Close();
                _computer = null;
            }

            // Independent Windows/vendor providers remain usable if LHM cannot open its driver.
            var windowsEnergy = _windowsEnergy.Capture();
            if (windowsEnergy.Count > 0)
            {
                // Some Intel systems expose placeholder zero-valued RAPL sensors through
                // LibreHardwareMonitor while Windows' Energy Meter has the live values.
                readings.RemoveAll(item =>
                    item.Type == nameof(SensorType.Power) &&
                    item.HardwareType.Equals("Cpu", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(item.Value) < 0.01);
                readings.AddRange(windowsEnergy);
            }

            readings.AddRange(_asusFirmware.Capture());
            var storage = _storageTemperatures.Capture();
            // Prefer the named native storage values if a library also exposes the same drive's temperatures.
            foreach (var device in storage.Readings.GroupBy(sensor => sensor.Hardware))
                readings.RemoveAll(sensor => sensor.Type == "Temperature" && sensor.HardwareType == "Storage" && sensor.Hardware == device.Key);
            readings.AddRange(storage.Readings);
            var nvidia = _nvidiaTelemetry.Capture();
            NvidiaDevices = nvidia.Devices;
            foreach (var device in nvidia.Devices.Where(device => device.SensorFields?.Any(field => field.FieldId == 185 && field.Value is not null) == true))
                readings.RemoveAll(sensor => sensor.HardwareType == "GpuNvidia" && sensor.Hardware == device.Name &&
                    sensor.Type == "Power" && sensor.Name == "GPU Package");
            readings.AddRange(nvidia.Readings);
            var nativeBattery = _batteryTelemetry.Capture().Readings;
            var libraryBatteries = readings.Where(sensor => sensor.HardwareType == "Battery")
                .Select(sensor => sensor.Hardware).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var nativeBatteries = nativeBattery.Select(sensor => sensor.Hardware + sensor.Identifier[..sensor.Identifier.LastIndexOf('/')])
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            // Only replace the equivalent generic library rows when matching is unambiguous.
            // Multiple batteries retain independent source rows rather than guessed identities.
            if (nativeBatteries == 1 && libraryBatteries == 1)
            {
                foreach (var sensor in nativeBattery)
                    readings.RemoveAll(existing => existing.HardwareType == "Battery" && existing.Type == sensor.Type &&
                        (sensor.Type is "Voltage" or "Current" ||
                         sensor.Type == "Power" && existing.Name == "Charge/Discharge Rate" ||
                         sensor.Type == "Energy" && existing.Name == "Remaining Capacity"));
            }
            readings.AddRange(nativeBattery);

            return readings
                .OrderBy(item => SensorTypeOrder(item.Type))
                .ThenBy(item => item.Hardware, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _computer?.Close();
            _computer = null;
            _asusFirmware.Dispose();
            _storageTemperatures.Dispose();
            _windowsEnergy.Dispose();
            _nvidiaTelemetry.Dispose();
            _batteryTelemetry.Dispose();
        }
    }

    private static Computer OpenComputer()
    {
        var computer = LhmProviderFactory.CreateCollectorComputer();
        computer.Open();
        return computer;
    }

    private static void CaptureHardware(IHardware hardware, ICollection<HardwareSensorReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value || !float.IsFinite(value))
            {
                continue;
            }

            var type = sensor.SensorType.ToString();
            readings.Add(new HardwareSensorReading(
                sensor.Identifier.ToString(),
                hardware.Name,
                hardware.HardwareType.ToString(),
                sensor.Name,
                type,
                value,
                UnitFor(sensor.SensorType)));
        }

        foreach (var child in hardware.SubHardware)
        {
            CaptureHardware(child, readings);
        }
    }

    private static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Energy => "mWh",
        SensorType.Fan => "RPM",
        SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => "%",
        SensorType.Clock or SensorType.Frequency => "MHz",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Flow => "L/h",
        _ => string.Empty
    };

    private static int SensorTypeOrder(string type) => type switch
    {
        nameof(SensorType.Temperature) => 0,
        nameof(SensorType.Voltage) => 1,
        nameof(SensorType.Current) => 2,
        nameof(SensorType.Power) => 3,
        nameof(SensorType.Energy) => 4,
        nameof(SensorType.Fan) => 5,
        _ => 6
    };
}
