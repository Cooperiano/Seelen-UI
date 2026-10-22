using System.Globalization;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal static class ProcessorFrequencyParser
{
    public static bool TryParse(string name, double? nominal, double? performance, out ProcessorFrequency? value)
    {
        value = null;
        var parts = name.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var group) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var processor) ||
            group > ushort.MaxValue || processor >= 64 || nominal is not > 0 || performance is null ||
            !double.IsFinite(nominal.Value) || !double.IsFinite(performance.Value) || performance < 0) return false;
        var estimated = nominal.Value * performance.Value / 100;
        if (!double.IsFinite(estimated) || estimated > 20_000) return false;
        value = new(group, processor, nominal.Value, performance.Value, estimated);
        return true;
    }
}
