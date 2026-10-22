using System.Runtime.InteropServices;
using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class IntelDptfParserTests
{
    public static void Run()
    {
        var units = new IntelDptfMetadata("Participant Temperature Value In 1/10 K", "Participant RAPL Power Value In .1 Watts");
        var result = WindowsIntelDptfInterface.CaptureData(() => units,
            () => [new("fixture", true, 3132u, 125u)]);
        if (Math.Abs(result.Participants.Single().TemperatureCelsius!.Value - 40.05) > 0.000001 ||
            result.Participants.Single().ParticipantRaplWatts != 12.5)
            throw new InvalidOperationException("DPTF decikelvin/deciwatt unit conversion failed.");
        result = WindowsIntelDptfInterface.CaptureData(() => units,
            () => [new("valid-zero", true, 2732u, 0u), new("inactive", false, 3132u, 125u),
                new("bad-temperature", true, uint.MaxValue, 10u), new("bad-power", true, 3132u, 0x80000000u),
                new("wrong-types", true, "3132", "125"), new("no-temperature", true, null, 10u),
                new("unphysical", true, 0u, 10u), new("unknown-active", null, 3132u, 10u)]);
        if (result.Participants[0].ParticipantRaplWatts is not null || result.Participants[0].RawPower != 0 ||
            result.Participants[0].TemperatureCelsius is null || result.Participants[1].TemperatureCelsius is not null ||
            result.Participants[2].TemperatureCelsius is not null || result.Participants[2].ParticipantRaplWatts != 1 ||
            result.Participants[3].TemperatureCelsius is null || result.Participants[3].ParticipantRaplWatts is not null ||
            result.Participants[4].TemperatureCelsius is not null || result.Participants[4].ParticipantRaplWatts is not null ||
            result.Participants[5].TemperatureCelsius is not null || result.Participants[6].TemperatureCelsius is not null ||
            result.Participants[7].ParticipantRaplWatts is not null)
            throw new InvalidOperationException("DPTF invalid/inactive data hid valid siblings or invented zero.");
        result = WindowsIntelDptfInterface.CaptureData(() => new("degrees C", "Watts"),
            () => [new("fixture", true, 3132u, 125u)]);
        if (result.Participants.Single().TemperatureCelsius is not null || result.Participants.Single().ParticipantRaplWatts is not null)
            throw new InvalidOperationException("Unconfirmed DPTF schema units were guessed.");
        result = WindowsIntelDptfInterface.CaptureData(() => units, () => throw new UnauthorizedAccessException("fixture"));
        if (result.Metadata is null || result.Participants.Count != 0 || result.Probes.Last().Status != SensorInterfaceStatus.PermissionDenied)
            throw new InvalidOperationException("DPTF permission failure was converted into absence/readings.");
        result = WindowsIntelDptfInterface.CaptureData(() => throw new COMException("fixture", unchecked((int)0x80041003)),
            () => throw new InvalidOperationException("Instances must not be queried after schema failure."));
        if (result.Probes.Single().Status != SensorInterfaceStatus.PermissionDenied)
            throw new InvalidOperationException("WMI COM access-denied status lost.");
        result = WindowsIntelDptfInterface.CaptureData(() => units, () => []);
        if (result.Probes.Last().Status != SensorInterfaceStatus.NoInterface)
            throw new InvalidOperationException("Empty DPTF instances were counted as readings.");
        Console.WriteLine("Passed Intel DPTF installed-unit, inactive/sentinel/type, raw-zero retention and permission-boundary tests.");
    }
}
