using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using MenuBar.Models;
using HardwareTelemetry.Services;

namespace MenuBar.Services;

public sealed class SystemStatusService : ISystemStatusService
{
    private readonly IHardwareMetricsReader _hardwareMetrics = new HardwareMetricsReader();

    public void ConfigureMetrics(IReadOnlySet<string> enabledMetricIds)
    {
        _hardwareMetrics.ReadPlatformMetrics =
            enabledMetricIds.Contains(Monitoring.Contracts.MetricIds.Power) ||
            enabledMetricIds.Contains("cpu_temp") || enabledMetricIds.Contains("cpu_freq");
        _hardwareMetrics.ReadNvidiaExtendedMetrics =
            enabledMetricIds.Contains("gpu_temp") ||
            enabledMetricIds.Contains("gpu_power") ||
            enabledMetricIds.Contains("gpu_memory");
        _hardwareMetrics.ReadBluetoothMetrics = enabledMetricIds.Contains(Monitoring.Contracts.MetricIds.Bluetooth);
        _hardwareMetrics.ReadMemoryDetails = false;
        _hardwareMetrics.ReadSensorMetrics = enabledMetricIds.Contains(Monitoring.Contracts.MetricIds.Power);
    }

    public SystemSnapshot Capture(nint ownWindowHandle)
    {
        var foreground = GetForegroundWindow();
        var (application, title) = foreground == ownWindowHandle
            ? ("桌面", string.Empty)
            : ReadForegroundApplication(foreground);

        var (volume, muted) = AudioEndpoint.TryRead();
        var (battery, charging) = ReadPowerStatus();
        var hardware = _hardwareMetrics.Capture();

        return new SystemSnapshot(
            NetworkInterface.GetIsNetworkAvailable(),
            volume,
            muted,
            battery,
            charging,
            hardware.CpuPercent,
            hardware.GpuPercent,
            hardware.MemoryPercent,
            hardware.PowerWatts,
            SensorMetricSelector.PreferredPowerWatts(hardware),
            hardware.CpuTemperatureCelsius,
            hardware.CpuFrequencyMhz,
            hardware.GpuTemperatureCelsius,
            hardware.GpuPowerWatts,
            hardware.GpuMemoryUsedMb,
            hardware.GpuMemoryTotalMb,
            hardware.DiskUsedPercent,
            hardware.NetworkDownloadBytesPerSecond,
            hardware.NetworkUploadBytesPerSecond,
            hardware.BluetoothConnectedDeviceCount,
            DateTimeOffset.Now,
            application,
            title);
    }

    private static (string Application, string Title) ReadForegroundApplication(nint handle)
    {
        if (handle == nint.Zero)
        {
            return ("桌面", string.Empty);
        }

        var titleBuilder = new StringBuilder(512);
        _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
        var title = titleBuilder.ToString().Trim();

        _ = GetWindowThreadProcessId(handle, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var productName = process.MainModule?.FileVersionInfo.ProductName;
            var application = ActiveApplicationFormatter.Normalize(productName, process.ProcessName);
            return (application, title);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (string.IsNullOrWhiteSpace(title) ? "桌面" : title, title);
        }
    }

    private static (int? Percent, bool Charging) ReadPowerStatus()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return (null, false);
        }

        int? percent = status.BatteryLifePercent == byte.MaxValue ? null : status.BatteryLifePercent;
        var charging = status.ACLineStatus == 1;
        return (percent, charging);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
