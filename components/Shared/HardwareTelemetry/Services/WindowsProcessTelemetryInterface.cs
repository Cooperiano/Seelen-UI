using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

/// <summary>Read-only OS process counters. Protected/disappeared processes may be absent.</summary>
public sealed class WindowsProcessTelemetryInterface
{
    public ProcessResourceCapture Capture()
    {
        var values = new List<ProcessResourceTelemetry>();
        try
        {
            using var query = new ManagementObjectSearcher("ROOT\\CIMV2",
                "SELECT Name, IDProcess, PercentProcessorTime, WorkingSet, WorkingSetPrivate " +
                "FROM Win32_PerfFormattedData_PerfProc_Process");
            using var results = query.Get();
            foreach (ManagementBaseObject item in results)
            using (item)
            {
                if (item["IDProcess"] is null || item["Name"] is null || item["PercentProcessorTime"] is null ||
                    item["WorkingSet"] is null || item["WorkingSetPrivate"] is null) continue;
                var pid = Convert.ToInt32(item["IDProcess"], CultureInfo.InvariantCulture);
                var name = item["Name"].ToString()!;
                var cpu = Convert.ToDouble(item["PercentProcessorTime"], CultureInfo.InvariantCulture);
                var workingSet = Convert.ToUInt64(item["WorkingSet"], CultureInfo.InvariantCulture);
                var privateSet = Convert.ToUInt64(item["WorkingSetPrivate"], CultureInfo.InvariantCulture);
                if (pid <= 0 || name == "_Total" || !double.IsFinite(cpu) || cpu < 0 || privateSet > workingSet) continue;
                values.Add(new(pid, name, Math.Clamp(cpu / Math.Max(1, Environment.ProcessorCount), 0, 100), workingSet, privateSet));
            }
            return new(values, [new("Windows Process Counters", "PerfProc_Process",
                values.Count > 0 ? SensorInterfaceStatus.Readable : SensorInterfaceStatus.NoInterface,
                $"{values.Count} process instances; CPU normalized to total logical capacity; working sets may share pages. Protected or exited processes can be absent.")]);
        }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return new([], [new("Windows Process Counters", "PerfProc_Process",
                exception is UnauthorizedAccessException or ManagementException { ErrorCode: ManagementStatus.AccessDenied }
                    ? SensorInterfaceStatus.PermissionDenied : SensorInterfaceStatus.Failed,
                exception.Message)]);
        }
    }
}
