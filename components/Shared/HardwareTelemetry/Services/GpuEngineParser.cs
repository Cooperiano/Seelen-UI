using System.Globalization;
using System.Text.RegularExpressions;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal static partial class GpuEngineParser
{
    // LUID is required: _phys_0 exists on multiple unrelated adapters.
    [GeneratedRegex(@"^pid_\d+_luid_0x([0-9a-f]{8})_0x([0-9a-f]{8})_phys_(\d+)_eng_(\d+)_engtype_(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static GpuEngineTelemetry? Parse(string name, double utilization)
    {
        var match = Pattern().Match(name);
        if (!match.Success || !double.IsFinite(utilization) || utilization < 0 ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var physical) ||
            !int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var engine)) return null;
        return new($"{match.Groups[1].Value.ToUpperInvariant()}:{match.Groups[2].Value.ToUpperInvariant()}", physical, engine,
            match.Groups[5].Value, utilization);
    }

    internal static IReadOnlyList<GpuEngineTelemetry> Aggregate(IEnumerable<GpuEngineTelemetry> rows) => rows
        .GroupBy(row => (row.AdapterLuid, row.PhysicalAdapter, row.Engine, row.EngineType))
        .Select(group => group.First() with { UtilizationPercent = Math.Clamp(group.Sum(row => row.UtilizationPercent), 0, 100) })
        .OrderBy(row => row.AdapterLuid).ThenBy(row => row.PhysicalAdapter).ThenBy(row => row.Engine).ToList();
}
