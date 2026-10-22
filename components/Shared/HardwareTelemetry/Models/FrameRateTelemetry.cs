namespace HardwareTelemetry.Models;

// Presented events are not displayed frames or engine utilization. Each swap chain stays separate.
public sealed record PresentedFrameRate(uint ProcessId, string Application, ulong SwapChainAddress,
    int PresentCount, int DroppedCount, double FirstPresentSeconds, double LastPresentSeconds,
    double? PresentsPerSecond)
{
    // Some ETW paths report address zero. Do not claim they identify a window/swap chain.
    public bool SwapChainIdentified => SwapChainAddress != 0;
}

public sealed record FrameRateCapture(IReadOnlyList<PresentedFrameRate> Streams,
    SensorInterfaceProbe Probe, string HelperVersion, double RequestedSeconds);
