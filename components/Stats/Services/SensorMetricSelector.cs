using HardwareTelemetry.Models;

namespace MenuBar.Services;

internal static class SensorMetricSelector
{
    public static double? PreferredTemperatureCelsius(HardwareMetrics metrics)
    {
        if (metrics.CpuTemperatureCelsius is { } cpu && double.IsFinite(cpu) && cpu is > -20 and < 150)
        {
            return cpu;
        }

        var cpuSensor = metrics.Sensors.FirstOrDefault(sensor =>
            sensor.Type == "Temperature" &&
            sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
            double.IsFinite(sensor.Value));
        if (cpuSensor is not null)
        {
            return cpuSensor.Value;
        }

        if (metrics.GpuTemperatureCelsius is { } gpu && double.IsFinite(gpu) && gpu is > -20 and < 150)
        {
            return gpu;
        }

        return HottestTemperature(metrics);
    }

    public static HardwareSensorReading? PreferredPowerSensor(HardwareMetrics metrics)
    {
        var powerSensors = metrics.Sensors
            .Where(sensor => sensor.Type == "Power" && double.IsFinite(sensor.Value) && sensor.Value >= 0 &&
                !sensor.Name.Contains("Average", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Find(powerSensors, "System Total")
            ?? Find(powerSensors, "Platform Power")
            ?? Find(powerSensors, "CPU Package")
            ?? powerSensors.FirstOrDefault(sensor => sensor.Value > 0)
            ?? powerSensors.FirstOrDefault();
    }

    public static double? PreferredPowerWatts(HardwareMetrics metrics) =>
        PreferredPowerSensor(metrics)?.Value;

    public static string PreferredPowerCaption(HardwareMetrics metrics)
    {
        var sensor = PreferredPowerSensor(metrics);
        if (sensor is null)
        {
            return "功耗";
        }

        return sensor.Name switch
        {
            "System Total" => "整机功耗",
            "Platform Power" => "平台功耗",
            "CPU Package" => "CPU 封装",
            _ => $"{sensor.Name} 功耗"
        };
    }

    public static double? HottestTemperature(HardwareMetrics metrics)
    {
        var values = metrics.Sensors
            .Where(sensor => sensor.Type == "Temperature" && double.IsFinite(sensor.Value))
            .Select(sensor => sensor.Value)
            .ToList();
        if (metrics.CpuTemperatureCelsius is { } cpu && double.IsFinite(cpu)) values.Add(cpu);
        if (metrics.GpuTemperatureCelsius is { } gpu && double.IsFinite(gpu)) values.Add(gpu);
        return values.Count == 0 ? null : values.Max();
    }

    private static HardwareSensorReading? Find(
        IEnumerable<HardwareSensorReading> sensors,
        string name) => sensors.FirstOrDefault(sensor =>
            sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
}
