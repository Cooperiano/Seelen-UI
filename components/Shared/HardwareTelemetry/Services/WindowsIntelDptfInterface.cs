using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

internal sealed record IntelDptfRawParticipant(string Id, bool? Active, object? Temperature, object? Power);

// Diagnostic only. Fixed property reads, never EsifExecutePrimitive, driver writes,
// privilege changes, service commands, or guesses about the participant's physical rail.
public static class WindowsIntelDptfInterface
{
    private const string ClassName = "EsifDeviceInformation";
    public static IntelDptfCapture Capture() => CaptureData(ReadMetadata, ReadParticipants);

    private static IntelDptfMetadata ReadMetadata()
    {
        using var schema = new ManagementClass(@"root\wmi", ClassName,
            new ObjectGetOptions { Timeout = TimeSpan.FromSeconds(3) });
        schema.Get();
        string Description(string name)
        {
            var property = schema.Properties[name];
            if (property.Type != CimType.UInt32) return "";
            foreach (QualifierData qualifier in property.Qualifiers)
                if (qualifier.Name.Equals("Description", StringComparison.OrdinalIgnoreCase))
                    return qualifier.Value as string ?? "";
            return "";
        }
        return new(Description("Temperature"), Description("Power"));
    }

    private static IReadOnlyList<IntelDptfRawParticipant> ReadParticipants()
    {
        using var query = new ManagementObjectSearcher(new ManagementScope(@"root\wmi"),
            new ObjectQuery("SELECT InstanceName, Active, Temperature, Power FROM " + ClassName),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false });
        using var rows = query.Get();
        var result = new List<IntelDptfRawParticipant>();
        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                if (result.Count >= 64) throw new InvalidDataException("Unexpected Intel participant count.");
                if (row["InstanceName"] is not string name || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("Intel participant has no identity.");
                result.Add(new(ReadOnlyNativeDevice.DeviceId(name), row["Active"] as bool?,
                    row["Temperature"], row["Power"]));
            }
        }
        return result;
    }

    internal static IntelDptfCapture CaptureData(Func<IntelDptfMetadata> metadataQuery,
        Func<IReadOnlyList<IntelDptfRawParticipant>> instanceQuery)
    {
        IntelDptfMetadata? metadata = null;
        var participants = new List<IntelDptfParticipant>();
        var probes = new List<SensorInterfaceProbe>();
        try
        {
            metadata = metadataQuery();
            probes.Add(new("Intel DPTF WMI", "schema", SensorInterfaceStatus.Readable,
                "Read-only class metadata; this is not a live sensor sample."));
            var rows = instanceQuery();
            if (rows.Count == 0) probes.Add(new("Intel DPTF WMI", "instances", SensorInterfaceStatus.NoInterface,
                "The class exists but returned no participant instances."));
            foreach (var row in rows)
            {
                double? ReadValue(string field, object? raw, string description, string expectedDescription,
                    Func<uint, double> convert)
                {
                    var endpoint = row.Id + "/" + field;
                    if (row.Active != true)
                    {
                        probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.Unsupported,
                            "Participant is inactive or active state is missing; no reading inferred."));
                        return null;
                    }
                    if (!description.Equals(expectedDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.Unsupported,
                            "Installed UInt32 schema does not confirm the expected physical unit."));
                        return null;
                    }
                    if (raw is not uint number || number is uint.MaxValue or 0x80000000)
                    {
                        probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.InvalidPayload,
                            "Missing/wrong-type/sentinel participant value; not converted to zero."));
                        return null;
                    }
                    if (field == "power" && number == 0)
                    {
                        probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.Unsupported,
                            "Raw zero retained; no per-field capability proves this is a measured zero rather than a default."));
                        return null;
                    }
                    var value = convert(number);
                    if (field == "temperature" && value is < -40 or > 150)
                    {
                        probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.InvalidPayload,
                            "Temperature outside the supported physical range."));
                        return null;
                    }
                    probes.Add(new("Intel DPTF WMI", endpoint, SensorInterfaceStatus.Readable,
                        "Installed schema unit verified; participant identity is not mapped to CPU/VRM/DC-in/system total."));
                    return value;
                }
                var temperature = ReadValue("temperature", row.Temperature, metadata.TemperatureDescription,
                    "Participant Temperature Value In 1/10 K", raw => raw / 10d - 273.15);
                var watts = ReadValue("power", row.Power, metadata.PowerDescription,
                    "Participant RAPL Power Value In .1 Watts", raw => raw / 10d);
                participants.Add(new(row.Id, row.Active, row.Temperature as uint?, row.Power as uint?, temperature, watts));
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or COMException or InvalidDataException)
        {
            var status = exception switch
            {
                UnauthorizedAccessException => SensorInterfaceStatus.PermissionDenied,
                ManagementException management when management.ErrorCode == ManagementStatus.AccessDenied => SensorInterfaceStatus.PermissionDenied,
                COMException com when com.HResult is unchecked((int)0x80070005) or unchecked((int)0x80041003) => SensorInterfaceStatus.PermissionDenied,
                ManagementException management when management.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace or ManagementStatus.NotFound => SensorInterfaceStatus.NoInterface,
                InvalidDataException => SensorInterfaceStatus.InvalidPayload,
                _ => SensorInterfaceStatus.Failed
            };
            probes.Add(new("Intel DPTF WMI", metadata is null ? "schema" : "instances", status,
                exception.Message, exception is ManagementException managementError
                    ? (int)managementError.ErrorCode : exception.HResult));
        }
        return new(metadata, participants, probes);
    }
}
