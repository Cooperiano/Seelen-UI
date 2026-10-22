using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MenuBar.Services;

public static class ComputerInfoService
{
    public static ComputerInfo Capture()
    {
        var manufacturer = string.Empty;
        var model = string.Empty;
        ulong? memoryBytes = null;
        var graphics = new List<string>();
        var operatingSystem = RuntimeInformation.OSDescription;

        TryQuery("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem", item =>
        {
            manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
            model = item["Model"]?.ToString()?.Trim() ?? string.Empty;
            if (ulong.TryParse(item["TotalPhysicalMemory"]?.ToString(), out var value))
            {
                memoryBytes = value;
            }
        });
        TryQuery("SELECT Caption, Version FROM Win32_OperatingSystem", item =>
        {
            var caption = item["Caption"]?.ToString()?.Trim();
            var version = item["Version"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(caption))
            {
                operatingSystem = string.IsNullOrWhiteSpace(version) ? caption : $"{caption} · {version}";
            }
        });
        TryQuery("SELECT Name FROM Win32_VideoController", item =>
        {
            var name = item["Name"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name) &&
                !IsVirtualGraphicsAdapter(name) &&
                !graphics.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                graphics.Add(name);
            }
        });

        var processor = string.Empty;
        try
        {
            processor = Registry.LocalMachine
                .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")?
                .GetValue("ProcessorNameString")?.ToString()?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }

        var deviceModel = string.Join(" ", new[] { manufacturer, model }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ComputerInfo(
            Environment.MachineName,
            string.IsNullOrWhiteSpace(deviceModel) ? "Windows 电脑" : deviceModel,
            operatingSystem,
            string.IsNullOrWhiteSpace(processor) ? "处理器信息不可用" : processor,
            memoryBytes is null ? "内存信息不可用" : $"{memoryBytes.Value / 1024d / 1024d / 1024d:0.#} GB",
            graphics.Count == 0 ? "图形设备信息不可用" : string.Join(" · ", graphics),
            FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64)));
    }

    private static void TryQuery(string query, Action<ManagementBaseObject> apply)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementBaseObject item in searcher.Get())
            {
                apply(item);
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException)
        {
        }
    }

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays} 天 {uptime.Hours} 小时"
        : $"{uptime.Hours} 小时 {uptime.Minutes} 分钟";

    private static bool IsVirtualGraphicsAdapter(string name) =>
        name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase);
}

public sealed record ComputerInfo(
    string Name,
    string Model,
    string OperatingSystem,
    string Processor,
    string Memory,
    string Graphics,
    string Uptime);
