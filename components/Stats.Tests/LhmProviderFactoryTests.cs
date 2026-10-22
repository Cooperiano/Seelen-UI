using HardwareTelemetry.Services;

internal static class LhmProviderFactoryTests
{
    public static void Run()
    {
        // Inspect un-opened objects: this test must not initialize any hardware.
        var collector = LhmProviderFactory.CreateCollectorComputer();
        if (!collector.IsBatteryEnabled || !collector.IsCpuEnabled || !collector.IsGpuEnabled ||
            !collector.IsMemoryEnabled || !collector.IsNetworkEnabled || !collector.IsStorageEnabled ||
            collector.IsMotherboardEnabled || collector.IsControllerEnabled || collector.IsPsuEnabled ||
            collector.IsPowerMonitorEnabled || collector.Hardware.Count != 0)
            throw new InvalidOperationException("Collector selection enabled an unreviewed hardware group or dropped a retained provider.");
        var cpu = LhmProviderFactory.CreateCpuProbeComputer();
        if (!cpu.IsCpuEnabled || cpu.IsBatteryEnabled || cpu.IsGpuEnabled || cpu.IsMemoryEnabled ||
            cpu.IsNetworkEnabled || cpu.IsStorageEnabled || cpu.IsMotherboardEnabled ||
            cpu.IsControllerEnabled || cpu.IsPsuEnabled || cpu.IsPowerMonitorEnabled || cpu.Hardware.Count != 0)
            throw new InvalidOperationException("CPU-only probe enabled a non-CPU provider.");
        if (LhmProviderFactory.DeferredProviders.Count != 4 ||
            !LhmProviderFactory.DeferredProviders.Any(reason => reason.Contains("exact-model mapping", StringComparison.Ordinal)))
            throw new InvalidOperationException("Deferred provider reasons were lost from the diagnostic plan.");
        Console.WriteLine("Passed unopened collector/CPU-only provider selection and explicit deferred-provider diagnostics.");
    }
}
