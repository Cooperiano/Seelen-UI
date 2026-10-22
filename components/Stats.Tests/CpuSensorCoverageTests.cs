using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class CpuSensorCoverageTests
{
    public static void Run()
    {
        var eight = Enumerable.Range(1, 8).Select(core => Core(core)).ToArray();
        Assert(CpuSensorCoverage.EvaluateTemperatures(eight, 8).AllPhysicalCoresPresent, "Eight physical cores should pass.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(eight.Take(1), 8).AllPhysicalCoresPresent, "One core cannot prove all eight.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(eight, null).AllPhysicalCoresPresent, "Unknown topology cannot pass.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(eight.Take(7).Append(Core(99)), 8).AllPhysicalCoresPresent, "A gapped label cannot replace a missing core.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(Enumerable.Repeat(Core(1), 8), 8).AllPhysicalCoresPresent, "Duplicates cannot increase coverage.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(eight.Take(7).Append(Core(8) with { Value = double.NaN }), 8).AllPhysicalCoresPresent, "Nonfinite data cannot pass.");
        Assert(!CpuSensorCoverage.EvaluateTemperatures(eight.Take(7).Append(Core(8) with { Unit = "K" }), 8).AllPhysicalCoresPresent, "Unconverted units cannot pass.");
        var conflict = eight.ToArray();
        conflict[7] = conflict[7] with { Identifier = conflict[0].Identifier };
        Assert(!CpuSensorCoverage.EvaluateTemperatures(conflict, 8).AllPhysicalCoresPresent, "One sensor cannot identify two physical cores.");
        var hybrid = Enumerable.Range(1, 4).Select(core => Core(core) with { Name = $"P-Core #{core}" })
            .Concat(Enumerable.Range(1, 4).Select(core => Core(core + 4) with { Name = $"E-Core #{core}" })).ToArray();
        Assert(CpuSensorCoverage.EvaluateTemperatures(hybrid, 8).AllPhysicalCoresPresent, "P/E labels must be independently counted.");
        var sockets = Enumerable.Range(1, 4).Select(core => Core(core))
            .Concat(Enumerable.Range(1, 4).Select(core => Core(core) with { Identifier = $"/intelcpu/1/temperature/{core}" })).ToArray();
        Assert(CpuSensorCoverage.EvaluateTemperatures(sockets, 8).AllPhysicalCoresPresent, "Socket-local numbering must preserve package identity.");
        Assert(CpuSensorCoverage.EvaluateTemperatures([Core(1) with { Name = "CPU Core" }], 1).AllPhysicalCoresPresent, "Single-core upstream naming should pass.");
        foreach (var label in new[] { "CPU Package", "Core Average", "Core Max", "CPU Core #1 Thread #1" })
            Assert(!CpuSensorCoverage.EvaluateTemperatures([Core(1) with { Name = label }], 1).AllPhysicalCoresPresent, "Aggregate/thread labels cannot pass.");
        var dram = new HardwareSensorReading("/intelcpu/0/power/3", "Intel CPU", "Cpu", "CPU Memory", "Power", 4.5, "W");
        Assert(CpuSensorCoverage.IsDramPower(dram), "Verified LHM DRAM identity should be recognized.");
        foreach (var invalid in new[] { dram with { Name = "CPU Platform", Identifier = "/intelcpu/0/power/4" },
            dram with { Value = 0 }, dram with { Value = double.NaN }, dram with { Unit = "mW" },
            dram with { Identifier = "/intelcpu/0/power/4" }, dram with { HardwareType = "Memory" } })
            Assert(!CpuSensorCoverage.IsDramPower(invalid), "Unverified memory/platform power was aliased as DRAM.");
        Assert(CpuSensorCoverage.IsDramPower(dram with { Name = "DRAM", Value = 0,
            Identifier = "/windows/energy-meter/rapl_package0_dram/0123456789abcdef" }), "Validated native EMI zero should be preserved.");
        Assert(!CpuSensorCoverage.IsDramPower(dram with { Name = "DRAM", Identifier = "/unknown/power" }), "A display name alone is not rail provenance.");
        Console.WriteLine("Passed complete physical-core coverage, identity/gap/duplicate/hybrid/socket checks and source-bound DRAM matching.");
    }
    private static HardwareSensorReading Core(int number) => new($"/intelcpu/0/temperature/{number}",
        "Intel CPU", "Cpu", $"CPU Core #{number}", "Temperature", 65, "°C");
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
