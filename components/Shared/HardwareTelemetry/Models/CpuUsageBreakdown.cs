namespace HardwareTelemetry.Models;

public sealed record CpuUsageBreakdown(
    double? UserPercent,
    double? SystemPercent,
    double? IdlePercent,
    double? LoadAverage1Minute,
    double? LoadAverage5Minutes,
    double? LoadAverage15Minutes,
    int? PhysicalCoreCount,
    int? LogicalProcessorCount,
    int? PerformanceCoreCount,
    int? EfficiencyCoreCount);
