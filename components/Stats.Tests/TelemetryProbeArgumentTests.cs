using HardwareTelemetry.Services;

internal static class TelemetryProbeArgumentTests
{
    public static void Run()
    {
        foreach (var valid in new string[][] { [], ["--require-all"], ["--output", "C:/path with spaces/report.json"],
            ["--cpu-driver-only", "--output", "C:/cpu.json"], ["--cpu-capabilities-only"], ["--gpu-node-metadata-only"], ["--fps-only", "--presentmon", "C:/helper.exe"],
            ["--require-all", "--presentmon", "C:/helper.exe", "--output", "C:/coverage.json"] })
            TelemetryProbeArguments.Validate(valid);
        foreach (var invalid in new string[][] { ["--strict"], ["unknown"], ["--output"], ["--output", "--require-all"],
            ["--output", " "], ["--presentmon"], ["--fps-only"], ["--require-all", "--require-all"],
            ["--output", "C:/a.json", "--output", "C:/b.json"], ["--cpu-driver-only", "--require-all"],
            ["--cpu-driver-only", "--nvidia-fields-only"], ["--cpu-capabilities-only", "--require-all"],
            ["--cpu-capabilities-only", "--cpu-driver-only"], ["--gpu-node-metadata-only", "--require-all"],
            ["--gpu-node-metadata-only", "--cpu-driver-only"], ["--intel-dptf-only", "--presentmon", "C:/helper.exe"] })
        {
            try { TelemetryProbeArguments.Validate(invalid); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("An ambiguous/unknown diagnostic command was accepted.");
        }
        Console.WriteLine("Passed probe argument rejection before hardware/helper access and strict/interface scope checks.");
    }
}
