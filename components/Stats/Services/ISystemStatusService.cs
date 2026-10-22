using MenuBar.Models;

namespace MenuBar.Services;

public interface ISystemStatusService
{
    SystemSnapshot Capture(nint ownWindowHandle);

    void ConfigureMetrics(IReadOnlySet<string> enabledMetricIds);
}
