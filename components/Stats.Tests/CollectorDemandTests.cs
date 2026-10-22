using MenuBar.Services;
using Monitoring.Contracts;

internal static class CollectorDemandTests
{
    public static void Run()
    {
        var hub = new LiveMetricsHub();
        var empty = new StatsApplicationSettings(
            StatsMetricCatalog.All.Select(item => new StatsMetricPreference(item.Id, false)).ToArray());
        Check(MetricsCollectorDemand.Resolve(empty, hub.GetCollectorInterests()) == default,
            "Disabled extended metrics must not create idle demand.");
        var alerts = empty with { Alerts = [new(MetricIds.CpuTemperature, true, 80), new(MetricIds.GpuTemperature, true, 80)] };
        var alerted = MetricsCollectorDemand.Resolve(alerts, hub.GetCollectorInterests());
        Check(alerted.Platform && alerted.NvidiaExtended && !alerted.Processes && !alerted.MemoryDetails,
            "Alert-only temperature metrics must still be collected, without unrelated detail readers.");
        var defaults = MetricsCollectorDemand.Resolve(new(StatsMetricCatalog.Defaults()), hub.GetCollectorInterests());
        Check(defaults.Platform && defaults.Sensors && !defaults.Processes && !defaults.MemoryDetails,
            "Default SENSOR and its history must remain available.");
        var full = MetricsCollectorDemand.Resolve(empty with { LowPowerMode = false }, hub.GetCollectorInterests());
        Check(full.Platform && full.NvidiaExtended && full.Bluetooth && full.MemoryDetails && full.Processes && full.Sensors,
            "Explicit full collection must retain its existing semantics.");
        using (var first = hub.BeginModuleSession(MetricIds.Cpu))
        {
            var second = hub.BeginModuleSession(MetricIds.CpuTemperature);
            var demand = MetricsCollectorDemand.Resolve(empty, hub.GetCollectorInterests());
            Check(demand.Platform && demand.Processes && !demand.MemoryDetails, "CPU detail dependencies are missing.");
            second.Dispose(); second.Dispose();
            Check(hub.GetCollectorInterests().Processes, "Closing one reference must not close the remaining session.");
            using var ram = hub.BeginModuleSession(MetricIds.Ram);
            Check(hub.GetCollectorInterests().MemoryDetails, "RAM popup must request memory details.");
        }
        Check(MetricsCollectorDemand.Resolve(empty, hub.GetCollectorInterests()) == default,
            "Closing all details must release demand.");
        using (hub.BeginModuleSession(MetricIds.Power))
        {
            var demand = MetricsCollectorDemand.Resolve(empty, hub.GetCollectorInterests());
            Check(demand.Platform && demand.Sensors && demand.NvidiaExtended, "Sensor popup must retain all source dependencies.");
        }
        using (hub.BeginModuleSession(MetricIds.Bluetooth))
            Check(MetricsCollectorDemand.Resolve(empty, hub.GetCollectorInterests()).Bluetooth, "Bluetooth detail demand missing.");
        Console.WriteLine("Passed collector demand: metrics, alert-only dependencies, full mode, popup references and release.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
