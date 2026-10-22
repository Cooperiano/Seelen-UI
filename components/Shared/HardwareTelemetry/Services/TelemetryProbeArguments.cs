namespace HardwareTelemetry.Services;

internal static class TelemetryProbeArguments
{
    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal)
    {
        "--intel-dptf-only", "--asus-firmware-only", "--power-meter-only", "--legacy-sensor-discovery-only",
        "--fps-only", "--cpu-driver-only", "--cpu-capabilities-only", "--nvidia-fields-only", "--gpu-node-metadata-only"
    };

    internal static void Validate(IReadOnlyList<string> args)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (option is not ("--output" or "--presentmon" or "--require-all") && !Modes.Contains(option))
                throw new ArgumentException("Unknown option. Strict full-coverage mode is --require-all.");
            if (!seen.Add(option)) throw new ArgumentException("Duplicate option is not allowed.");
            if (option is "--output" or "--presentmon")
            {
                if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("A path-valued option requires one path argument.");
            }
        }
        var selectedModes = seen.Where(Modes.Contains).ToList();
        if (selectedModes.Count > 1) throw new ArgumentException("Choose one interface-only mode or the full report, not multiple interface modes.");
        if (selectedModes.Count > 0 && seen.Contains("--require-all"))
            throw new ArgumentException("--require-all is only valid for the full requirement report.");
        if (seen.Contains("--presentmon") && selectedModes.Count > 0 && !seen.Contains("--fps-only"))
            throw new ArgumentException("--presentmon is only valid for the full report or --fps-only.");
        if (seen.Contains("--fps-only") && !seen.Contains("--presentmon"))
            throw new ArgumentException("--fps-only requires an explicit --presentmon path.");
    }
}
