namespace HardwareTelemetry.Models;

// Windows performance-counter estimate, not a direct per-core MSR clock measurement.
public sealed record ProcessorFrequency(int Group, int Processor, double NominalMhz,
    double PerformancePercent, double EstimatedMhz);
