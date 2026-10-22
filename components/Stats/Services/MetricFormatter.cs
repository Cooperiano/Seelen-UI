namespace MenuBar.Services;

public static class MetricFormatter
{
    public static string Percent(double? value)
    {
        return value is null || !double.IsFinite(value.Value)
            ? "--"
            : $"{Math.Clamp(value.Value, 0, 100):0}%";
    }

    public static string Power(double? watts)
    {
        if (watts is null || !double.IsFinite(watts.Value) || watts.Value < 0.05)
        {
            return "--";
        }

        var clamped = Math.Max(0, watts.Value);
        return clamped < 10 ? $"{clamped:0.0}W" : $"{clamped:0}W";
    }

    public static string PercentTooltip(string name, double? value)
    {
        return value is null || !double.IsFinite(value.Value)
            ? $"{name} 数据暂不可用"
            : $"{name} 使用率 {Math.Clamp(value.Value, 0, 100):0.0}%";
    }

    public static string Temperature(double? celsius) =>
        celsius is null || !double.IsFinite(celsius.Value) ? "--" : $"{celsius.Value:0}°";

    public static string Frequency(double? mhz) =>
        mhz is null || !double.IsFinite(mhz.Value) ? "--" : $"{mhz.Value / 1000d:0.0}G";

    public static string MemoryMb(double? megabytes)
    {
        if (megabytes is null || !double.IsFinite(megabytes.Value))
        {
            return "--";
        }

        return megabytes.Value >= 1024
            ? $"{megabytes.Value / 1024d:0.0}G"
            : $"{megabytes.Value:0}M";
    }

    public static string Rate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null || !double.IsFinite(bytesPerSecond.Value) || bytesPerSecond < 0)
        {
            return "--";
        }

        return bytesPerSecond.Value switch
        {
            >= 1024 * 1024 => $"{bytesPerSecond.Value / 1024d / 1024d:0.0}M",
            >= 1024 => $"{bytesPerSecond.Value / 1024d:0}K",
            _ => $"{bytesPerSecond.Value:0}B"
        };
    }
}
