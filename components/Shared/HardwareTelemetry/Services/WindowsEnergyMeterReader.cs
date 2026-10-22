using System.Management;
using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal sealed class WindowsEnergyMeterReader : IDisposable
{
    private const string EnergyMeterClass = "Win32_PerfFormattedData_PowerMeterCounter_EnergyMeter";
    private readonly EnergyMeterSession _session = new();
    private readonly WindowsEnergyMeterInterface _native = new();
    private readonly EnergyMeterSession _nativeSession = new(10_000_000);
    private readonly Dictionary<string, (ulong Energy, ulong Time)> _previousNative = new();

    public IReadOnlyList<HardwareSensorReading> Capture()
    {
        var nativeReadings = CaptureNative();
        if (nativeReadings.Count > 0) return nativeReadings;
        // Compatibility fallback for drivers without a readable native EMI interface, including warm-up.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                $"SELECT Name, Power, Energy, Time FROM {EnergyMeterClass}");
            var readings = new List<HardwareSensorReading>();
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var instance = Convert.ToString(item["Name"]);
                    // _Total sums overlapping RAPL domains: it is NOT whole-system power.
                    if (string.IsNullOrWhiteSpace(instance) || instance == "_Total")
                    {
                        continue;
                    }

                    var name = instance switch
                    {
                        "RAPL_Package0_PKG" => "CPU Package",
                        "RAPL_Package0_PP0" => "CPU Cores",
                        "RAPL_Package0_PP1" => "Integrated GPU",
                        "RAPL_Package0_DRAM" => "DRAM",
                        _ => instance
                    };
                    var id = $"/windows/energy-meter/{instance.ToLowerInvariant()}";
                    var hasEnergy = TryUInt64(item["Energy"], out var energy);
                    var hasPower = TryUInt64(item["Power"], out var milliwatts);
                    // An unimplemented DRAM rail on some CPUs stays zero for both counters.
                    if (instance.EndsWith("_DRAM", StringComparison.Ordinal) &&
                        hasEnergy && energy == 0 && hasPower && milliwatts == 0) continue;
                    if (hasPower)
                        readings.Add(new(id, "Windows RAPL", "Cpu", name, "Power", milliwatts / 1000d, "W"));

                    // Only known Windows RAPL instances have the verified pWh/ms convention.
                    if (instance.StartsWith("RAPL_", StringComparison.Ordinal) && hasEnergy &&
                        TryUInt64(item["Time"], out var time) &&
                        _session.Observe(instance, energy, time) is { } session)
                    {
                        readings.Add(new(id + "/session-energy", "Windows RAPL", "Cpu",
                            name + " · Session Energy", "Energy", session.MilliWattHours, "mWh"));
                        readings.Add(new(id + "/session-average", "Windows RAPL", "Cpu",
                            name + " · Session Average", "Power", session.AverageWatts, "W"));
                    }
                }
            }

            return readings;
        }
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException or InvalidCastException)
        {
            return [];
        }
    }

    private IReadOnlyList<HardwareSensorReading> CaptureNative()
    {
        var readings = new List<HardwareSensorReading>();
        foreach (var device in _native.Capture().Devices)
        foreach (var channel in device.Channels)
        {
            if (channel.Unit != 0 || channel.Name == "_Total") continue;
            var key = device.Id + "/" + channel.Name;
            var id = $"/windows/energy-meter/{channel.Name.ToLowerInvariant()}/{device.Id}";
            var hardware = channel.Name.StartsWith("RAPL_", StringComparison.Ordinal) ? "Windows RAPL" : $"Windows EMI {device.Model}";
            var hardwareType = hardware == "Windows RAPL" ? "Cpu" : "Platform";
            var name = channel.Name switch
            {
                "RAPL_Package0_PKG" => "CPU Package",
                "RAPL_Package0_PP0" => "CPU Cores",
                "RAPL_Package0_PP1" => "Integrated GPU",
                "RAPL_Package0_DRAM" => "DRAM",
                _ => channel.Name
            };
            var hadPrevious = _previousNative.TryGetValue(key, out var previous);
            if (hadPrevious && channel.AbsoluteTime100ns == previous.Time && channel.AbsoluteEnergy != previous.Energy)
                continue;
            _previousNative[key] = (channel.AbsoluteEnergy, channel.AbsoluteTime100ns);
            var session = _nativeSession.Observe(key, channel.AbsoluteEnergy, channel.AbsoluteTime100ns);
            // A zero accumulated counter has not proved that this rail is physically metered.
            if (channel.AbsoluteEnergy == 0 || session is null) continue;
            if (hadPrevious && channel.AbsoluteTime100ns > previous.Time && channel.AbsoluteEnergy >= previous.Energy)
            {
                var watts = (channel.AbsoluteEnergy - previous.Energy) * 0.036d / (channel.AbsoluteTime100ns - previous.Time);
                if (double.IsFinite(watts) && watts >= 0)
                    readings.Add(new(id, hardware, hardwareType, name, "Power", watts, "W"));
            }
            readings.Add(new(id + "/session-energy", hardware, hardwareType,
                name + " · Session Energy", "Energy", session.Value.MilliWattHours, "mWh"));
            readings.Add(new(id + "/session-average", hardware, hardwareType,
                name + " · Session Average", "Power", session.Value.AverageWatts, "W"));
        }
        return readings;
    }

    public void Dispose() => _native.Dispose();

    private static bool TryUInt64(object? value, out ulong result)
    {
        if (value is null) { result = 0; return false; }
        try
        {
            result = Convert.ToUInt64(value);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }
}
