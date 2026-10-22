using System.Globalization;
using System.Text.RegularExpressions;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal sealed record CoreTemperatureCoverage(int? ExpectedPhysicalCores, int ObservedPhysicalCores,
    bool AllPhysicalCoresPresent, IReadOnlyList<HardwareSensorReading> Readings, IReadOnlyList<string> Diagnostics);

// Coverage audit only, not a collector and not a physical-rail rename in production.
internal static class CpuSensorCoverage
{
    private static readonly Regex TemperatureId = new(@"^/(?<vendor>intelcpu|amdcpu|genericcpu)/(?<package>\d+)/temperature/\d+$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex CoreName = new(@"^(?:CPU )?(?<kind>[PE]-)?Core(?: #(?<number>\d+))?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex IntelDramId = new(@"^/intelcpu/\d+/power/3$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly Regex EmiDramId = new(@"^/windows/energy-meter/rapl_package\d+_dram(?:/[a-f0-9]{16})?$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static CoreTemperatureCoverage EvaluateTemperatures(IEnumerable<HardwareSensorReading> readings,
        int? physicalCoreCount)
    {
        var diagnostics = new List<string>();
        var cores = new Dictionary<(string Group, int Number), HardwareSensorReading>();
        var identities = new Dictionary<string, (string Group, int Number)>(StringComparer.Ordinal);
        var conflict = false;
        foreach (var reading in readings)
        {
            if (reading.HardwareType != "Cpu" || reading.Type != "Temperature" || reading.Unit != "°C" ||
                !double.IsFinite(reading.Value) || reading.Value is < -40 or > 150) continue;
            var id = TemperatureId.Match(reading.Identifier);
            var name = CoreName.Match(reading.Name);
            if (!id.Success || !name.Success) continue; // Package/max/average/thread rows are not physical cores.
            var number = 1;
            if (name.Groups["number"].Success &&
                !int.TryParse(name.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) continue;
            var group = id.Groups["vendor"].Value + "/" + id.Groups["package"].Value + "/" + name.Groups["kind"].Value;
            var key = (group, number);
            if (identities.TryGetValue(reading.Identifier, out var prior) && prior != key) conflict = true;
            identities[reading.Identifier] = key;
            cores.TryAdd(key, reading); // Repeated rows/labels cannot manufacture additional cores.
        }
        if (conflict) diagnostics.Add("A temperature sensor identity has conflicting physical-core labels.");
        var contiguous = true;
        foreach (var group in cores.Keys.GroupBy(item => item.Group))
        {
            var numbers = group.Select(item => item.Number).Order().ToArray();
            if (numbers[0] is not (0 or 1) || !numbers.SequenceEqual(Enumerable.Range(numbers[0], numbers.Length)))
                contiguous = false;
        }
        if (!contiguous) diagnostics.Add("Core labels have gaps or unsupported numbering; count alone is insufficient.");
        if (physicalCoreCount is null or <= 0) diagnostics.Add("Native physical-core topology is unavailable.");
        else if (cores.Count != physicalCoreCount) diagnostics.Add($"Only {cores.Count}/{physicalCoreCount} physical-core temperature rows verified.");
        var complete = physicalCoreCount > 0 && cores.Count == physicalCoreCount && contiguous && !conflict;
        return new(physicalCoreCount, cores.Count, complete, cores.Values.ToList(), diagnostics);
    }

    internal static bool IsDramPower(HardwareSensorReading reading)
    {
        if (reading.HardwareType != "Cpu" || reading.Type != "Power" || reading.Unit != "W" ||
            !double.IsFinite(reading.Value) || reading.Value < 0) return false;
        // Native EMI already gates units, energy availability, elapsed time and counter state.
        if (reading.Name == "DRAM" && EmiDramId.IsMatch(reading.Identifier)) return true;
        // LHM 0.9.6 index 3 is MSR_DRAM_ENERGY_STATUS (0x619), named CPU Memory.
        // A zero library placeholder does not prove metering; do not alias CPU Platform index 4.
        return reading.Name == "CPU Memory" && IntelDramId.IsMatch(reading.Identifier) && reading.Value > 0;
    }
}
