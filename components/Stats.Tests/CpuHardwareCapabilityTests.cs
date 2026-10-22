using HardwareTelemetry.Models;
using HardwareTelemetry.Services;

internal static class CpuHardwareCapabilityTests
{
    public static void Run()
    {
        var calls = new List<uint>();
        CpuCpuidLeaf Intel(uint leaf)
        {
            calls.Add(leaf);
            return leaf switch
            {
                0 => new(0, 0x1f, 0x756e6547, 0x6c65746e, 0x49656e69),
                1 => new(1, 0x000806d1, 0, 0x80000000, 0),
                6 => new(6, 0x41, 0, 0, 0),
                _ => throw new InvalidOperationException("CPUID leaf escaped fixed whitelist.")
            };
        }
        var capture = WindowsCpuCapabilitiesInterface.CaptureData(true, Intel);
        if (capture.Capabilities is not { Vendor: "GenuineIntel", Family: 6, Model: 141, Stepping: 1,
            DigitalThermalSensorDeclared: true, PackageThermalManagementDeclared: true, HypervisorPresent: true } ||
            capture.Capabilities.ReviewedCpuProvider is null || !calls.SequenceEqual(new uint[] { 0, 1, 6 }))
            throw new InvalidOperationException("Intel CPUID vendor/register-order/model/features failed.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf =>
        {
            var value = Intel(leaf);
            return leaf == 6 ? value with { Eax = 0 } : value;
        });
        if (capture.Capabilities is not { DigitalThermalSensorDeclared: false, PackageThermalManagementDeclared: false })
            throw new InvalidOperationException("Cleared thermal capability bits were not preserved as false.");
        calls.Clear();
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf =>
        {
            var value = Intel(leaf);
            return leaf == 0 ? value with { Eax = 5 } : value;
        });
        if (capture.Capabilities?.DigitalThermalSensorDeclared is not null || calls.Contains(6))
            throw new InvalidOperationException("Unadvertised CPUID leaf was queried or assumed supported.");
        calls.Clear();
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf => Intel(leaf) with { Eax = 0 });
        if (!calls.SequenceEqual(new uint[] { 0 }) || capture.Capabilities is not { Family: null,
            Model: null, Stepping: null, HypervisorPresent: null, DigitalThermalSensorDeclared: null })
            throw new InvalidOperationException("Leaf-zero-only metadata manufactured a CPU signature or features.");
        foreach (var signature in new[]
        {
            (Eax: 0x003a0f25u, Family: 18, Model: 162, Stepping: 5),
            (Eax: 0x003a0525u, Family: 5, Model: 2, Stepping: 5)
        })
        {
            capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf =>
            {
                var value = Intel(leaf);
                return leaf == 1 ? value with { Eax = signature.Eax, Ecx = 0 } : value;
            });
            if (capture.Capabilities?.Family != signature.Family || capture.Capabilities.Model != signature.Model ||
                capture.Capabilities.Stepping != signature.Stepping || capture.Capabilities.HypervisorPresent != false ||
                capture.Capabilities.ReviewedCpuProvider is not null)
                throw new InvalidOperationException("Extended family/model decoding or provider matching failed.");
        }
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf => leaf == 0
            ? new(0, 6, 0x68747541, 0x444d4163, 0x69746e65) : Intel(leaf));
        if (capture.Capabilities is not { Vendor: "AuthenticAMD", DigitalThermalSensorDeclared: null,
            PackageThermalManagementDeclared: null, ReviewedCpuProvider: null })
            throw new InvalidOperationException("Intel thermal semantics were applied to another vendor.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(false, _ => throw new InvalidOperationException("Unsupported instruction must not run."));
        if (capture.Capabilities is not null || capture.Leaves.Count != 0 || capture.Probes.Single().Status != SensorInterfaceStatus.NoInterface)
            throw new InvalidOperationException("Unsupported architecture manufactured metadata.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, _ => new(0, 6, 0, 0, 0));
        if (capture.Capabilities is not null || capture.Probes.Last().Status != SensorInterfaceStatus.InvalidPayload)
            throw new InvalidOperationException("Empty CPUID vendor was accepted.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf => Intel(leaf) with { Ebx = 0x756e65ff });
        if (capture.Capabilities is not null || capture.Probes.Last().Status != SensorInterfaceStatus.InvalidPayload)
            throw new InvalidOperationException("Non-ASCII vendor identity was accepted.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, _ => throw new PlatformNotSupportedException("Unavailable CPUID intrinsic."));
        if (capture.Capabilities is not null || capture.Probes.Last().Status != SensorInterfaceStatus.NoInterface)
            throw new InvalidOperationException("Runtime instruction failure did not remain unavailable.");
        capture = WindowsCpuCapabilitiesInterface.CaptureData(true, leaf => Intel(leaf) with { Leaf = 3 });
        if (capture.Capabilities is not null || capture.Probes.Last().Status != SensorInterfaceStatus.InvalidPayload)
            throw new InvalidOperationException("Mismatched leaf identity was accepted.");
        Console.WriteLine("Passed fixed CPUID leaf range, vendor/signature/thermal bits, non-Intel/unsupported and malformed metadata tests.");
    }
}
