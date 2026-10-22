using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class LegacySensorDiscoveryTests
{
    public static void Run()
    {
        var cases = new (int Result, uint? Count, SensorInterfaceStatus Status)[] {
            (0, 0, SensorInterfaceStatus.NoInterface),
            (0, 2, SensorInterfaceStatus.Readable),
            (0, null, SensorInterfaceStatus.InvalidPayload),
            (0, 513, SensorInterfaceStatus.InvalidPayload),
            (unchecked((int)0x80070490), null, SensorInterfaceStatus.NoInterface),
            (unchecked((int)0x80040154), null, SensorInterfaceStatus.NoInterface),
            (unchecked((int)0x80070005), 7, SensorInterfaceStatus.PermissionDenied),
            (unchecked((int)0x800704ec), 7, SensorInterfaceStatus.PermissionDenied),
            (unchecked((int)0x80004001), null, SensorInterfaceStatus.Unsupported),
            (unchecked((int)0x80004005), null, SensorInterfaceStatus.Failed),
            (1, null, SensorInterfaceStatus.NoInterface)
        };
        foreach (var test in cases)
        {
            var probe = WindowsLegacySensorDiscovery.Describe("Electrical", test.Result, test.Count);
            if (probe.Status != test.Status || probe.NativeError != (test.Result < 0 ? test.Result : null))
                throw new InvalidOperationException($"Legacy discovery confused enumeration/errors/readings: {test.Result:X8}/{test.Count}.");
            if (probe.Status == SensorInterfaceStatus.Readable && !probe.Detail.Contains("not a measured", StringComparison.Ordinal))
                throw new InvalidOperationException("Discovery count presented as a physical reading.");
        }
        Console.WriteLine("Passed legacy sensor discovery: empty category, bounded counts, permissions, policy, missing COM and no false measurement.");
    }
}
