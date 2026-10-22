namespace HardwareTelemetry.Models;

public sealed record MemoryBreakdown(
    ulong? AvailableBytes,
    ulong? CacheBytes,
    ulong? CommittedBytes,
    ulong? CommitLimitBytes,
    ulong? PagedPoolBytes,
    ulong? NonPagedPoolBytes,
    ulong? CompressedBytes,
    ulong? SwapUsedBytes,
    ulong? SwapTotalBytes);
