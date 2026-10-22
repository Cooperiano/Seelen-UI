using LibreHardwareMonitor.Hardware;

namespace HardwareTelemetry.Services;

// Provider selection, not a claim that every model supported by a retained
// provider has been fully audited. Named native firmware readers are separate.
internal static class LhmProviderFactory
{
    internal static Computer CreateCollectorComputer() => new()
    {
        IsBatteryEnabled = true,
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsNetworkEnabled = true,
        IsStorageEnabled = true,
        // LHM 0.9.6 generic LPC detection enters configuration mode and may
        // disable I/O-space locks; it is not a GET-only model-specific reader.
        IsMotherboardEnabled = false,
        // Controller/PSU constructors may issue serial/HID initialization.
        // Re-enable only through a separately reviewed device-specific path.
        IsControllerEnabled = false,
        IsPsuEnabled = false,
        IsPowerMonitorEnabled = false
    };

    internal static Computer CreateCpuProbeComputer() => new() { IsCpuEnabled = true };

    internal static IReadOnlyList<string> DeferredProviders { get; } = Array.AsReadOnly(new[]
    {
        "Motherboard: generic LPC/SuperIO configuration and lock changes are not an approved GET-only mapping; no separately reviewed exact-model mapping and shared locking path has been enabled.",
        "Controller: generic serial/HID initialization is not an independently reviewed device-specific query.",
        "PSU: generic USB/HID initialization is not an independently verified system-total or DC-in meter.",
        "PowerMonitor: no named device/protocol was independently verified for this machine."
    });
}
