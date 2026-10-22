using System.Globalization;

namespace Monitoring.Contracts;

public static class MetricIds
{
    public const string Cpu = "cpu";
    public const string Gpu = "gpu";
    public const string Ram = "ram";
    public const string Power = "power";
    public const string CpuTemperature = "cpu_temp";
    public const string CpuFrequency = "cpu_freq";
    public const string GpuTemperature = "gpu_temp";
    public const string GpuPower = "gpu_power";
    public const string GpuMemory = "gpu_memory";
    public const string Disk = "disk";
    public const string NetworkDownload = "network_down";
    public const string NetworkUpload = "network_up";
    public const string Battery = "battery";
    public const string Bluetooth = "bluetooth";
}

public sealed record StatsMetricDefinition(
    string Id,
    string Title,
    string Description,
    string Accent,
    bool DefaultEnabled);

public sealed record StatsMetricPreference(
    string Id,
    bool IsEnabled,
    string DisplayStyle = MetricDisplayStyles.Stacked,
    string? Accent = null);

public static class MetricColors
{
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#FF65D5FF", "#FF72E6AE", "#FFFFC568", "#FFFF8DA1", "#FFFFA66B",
        "#FF82B9FF", "#FF7FE0C3", "#FFB69CFF", "#FF9DD56A", "#FFFF6B6B",
        "#FFF5F5F7"
    ];

    public static string Normalize(string? value, string fallback)
    {
        if (TryParse(value, out var argb))
        {
            return $"#{argb:X8}";
        }

        return TryParse(fallback, out argb) ? $"#{argb:X8}" : Palette[0];
    }

    public static uint ToProtocolArgb(string? value) =>
        TryParse(value, out var argb) ? argb : 0xFFF5F5F7U;

    public static string Next(string? current)
    {
        var normalized = Normalize(current, Palette[0]);
        var index = Palette.ToList().FindIndex(color => string.Equals(color, normalized, StringComparison.Ordinal));
        return Palette[(index + 1 + Palette.Count) % Palette.Count];
    }

    private static bool TryParse(string? value, out uint argb)
    {
        argb = 0;
        return value is { Length: 9 } && value[0] == '#' &&
               uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out argb);
    }
}

public static class MetricDisplayStyles
{
    public const string Stacked = "stacked";
    public const string Inline = "inline";
    public const string ValueOnly = "value";
    public const string Chart = "chart";

    public static bool IsValid(string value) => value is Stacked or Inline or ValueOnly or Chart;

    public static byte ToProtocolValue(string value) => value switch
    {
        Inline => 1,
        ValueOnly => 2,
        Chart => 3,
        _ => 0
    };
}

public sealed record StatsApplicationSettings(
    IReadOnlyList<StatsMetricPreference> Metrics,
    int RefreshIntervalMilliseconds = 1000,
    int HistorySeconds = 60,
    bool LowPowerMode = true,
    IReadOnlyList<StatsAlertRule>? Alerts = null);

public sealed record StatsAlertRule(
    string MetricId,
    bool IsEnabled,
    double Threshold,
    string Comparison = "above",
    int SustainSeconds = 10);

public static class StatsAlertCatalog
{
    public static IReadOnlyList<StatsAlertRule> Defaults { get; } =
    [
        new(MetricIds.Cpu, false, 90),
        new(MetricIds.Gpu, false, 90),
        new(MetricIds.Ram, false, 90),
        new(MetricIds.CpuTemperature, false, 90),
        new(MetricIds.GpuTemperature, false, 85),
        new(MetricIds.Battery, false, 20, "below", 0)
    ];
}

public static class StatsMetricCatalog
{
    public static IReadOnlyList<StatsMetricDefinition> All { get; } =
    [
        new(MetricIds.Cpu, "CPU", "处理器使用率", "#FF65D5FF", true),
        new(MetricIds.Gpu, "GPU", "图形处理器使用率", "#FF72E6AE", true),
        new(MetricIds.Ram, "RAM", "物理内存使用率", "#FFFFC568", true),
        new(MetricIds.Power, "SENSOR", "默认显示 CPU 温度；详情含温度、电压、电流、功率、能量与风扇", "#FFFF8DA1", true),
        new(MetricIds.CpuTemperature, "CPU°", "ACPI 处理器温区", "#FFFFA66B", false),
        new(MetricIds.CpuFrequency, "CPU↗", "处理器当前频率", "#FF82B9FF", false),
        new(MetricIds.GpuTemperature, "GPU°", "NVIDIA GPU 温度", "#FF7FE0C3", false),
        new(MetricIds.GpuPower, "GPU W", "NVIDIA GPU 功耗", "#FFFF91B4", false),
        new(MetricIds.GpuMemory, "VRAM", "独立显存占用", "#FFB69CFF", false),
        new(MetricIds.Disk, "DISK", "系统磁盘占用", "#FF68C7E8", false),
        new(MetricIds.NetworkDownload, "NET↓", "网络下载速率", "#FF66D9C2", false),
        new(MetricIds.NetworkUpload, "NET↑", "网络上传速率", "#FF79B8FF", false),
        new(MetricIds.Battery, "BAT", "电池剩余电量", "#FF9DD56A", false),
        new(MetricIds.Bluetooth, "BT", "已连接蓝牙设备", "#FF6EA8FF", false)
    ];

    public static StatsMetricDefinition? Find(string id) =>
        All.FirstOrDefault(metric => string.Equals(metric.Id, id, StringComparison.Ordinal));

    public static List<StatsMetricPreference> Defaults() =>
        All.Select(metric => new StatsMetricPreference(
            metric.Id,
            metric.DefaultEnabled,
            MetricDisplayStyles.Stacked,
            metric.Accent)).ToList();
}

public static class SharedMetricsLayout
{
    public const string MapName = "Local\\Monitoring.Metrics.v7";
    public const uint Magic = 0x3154424D;
    public const uint Version = 7;
    public const long Capacity = 320;

    public const long CpuOffset = 16;
    public const long GpuOffset = 24;
    public const long MemoryOffset = 32;
    public const long PowerOffset = 40;
    public const long SensorOffset = PowerOffset;
    public const long CpuTemperatureOffset = 48;
    public const long CpuFrequencyOffset = 56;
    public const long GpuTemperatureOffset = 64;
    public const long GpuPowerOffset = 72;
    public const long GpuMemoryUsedOffset = 80;
    public const long GpuMemoryTotalOffset = 88;
    public const long BatteryOffset = 96;
    public const long DiskOffset = 104;
    public const long NetworkDownloadOffset = 112;
    public const long NetworkUploadOffset = 120;
    public const long BluetoothOffset = 128;
    public const long EnabledMaskOffset = 136;
    public const long OrderCountOffset = 140;
    public const long OrderOffset = 144;
    public const long StyleOffset = 200;
    public const long ColorOffset = 216;
}

public static class LegacySharedMetricsLayout
{
    public const string MapName = "Local\\Monitoring.Metrics.v6";
    public const uint Version = 6;
}

public static class MetricAnchorLayout
{
    public const string MapName = "Local\\Brimbar.MetricAnchors.v1";
    public const uint Magic = 0x31414D42;
    public const uint Version = 1;
    public const long Capacity = 256;
    public const long SequenceOffset = 8;
    public const long VisibleMaskOffset = 16;
    public const long RectanglesOffset = 24;
    public const int RectangleSize = 16;
}
