using System.Buffers.Binary;
using System.IO;
using System.Runtime.Intrinsics.X86;
using System.Text;
using HardwareTelemetry.Models;

namespace HardwareTelemetry.Services;

// Fixed user-mode CPUID instructions only. No serial leaf, MSRs, affinity changes,
// driver loads, WMI methods, privilege changes or thermal/power control operations.
public static class WindowsCpuCapabilitiesInterface
{
    public static CpuHardwareCapabilityCapture Capture() => CaptureData(X86Base.IsSupported, leaf =>
    {
        var value = X86Base.CpuId((int)leaf, 0);
        return new(leaf, unchecked((uint)value.Eax), unchecked((uint)value.Ebx),
            unchecked((uint)value.Ecx), unchecked((uint)value.Edx));
    });

    internal static CpuHardwareCapabilityCapture CaptureData(bool supported, Func<uint, CpuCpuidLeaf> query)
    {
        if (!supported) return new(null, [], [new("CPUID", "architecture", SensorInterfaceStatus.NoInterface,
            "The runtime does not expose the x86 CPUID instruction; no CPU features inferred.")]);
        var leaves = new List<CpuCpuidLeaf>();
        var probes = new List<SensorInterfaceProbe>();
        try
        {
            CpuCpuidLeaf Read(uint leaf)
            {
                if (leaf is not (0 or 1 or 6)) throw new ArgumentOutOfRangeException(nameof(leaf));
                var value = query(leaf);
                if (value.Leaf != leaf) throw new InvalidDataException("CPUID response leaf identity mismatch.");
                leaves.Add(value);
                return value;
            }
            var basic = Read(0);
            Span<byte> vendorBytes = stackalloc byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(vendorBytes, basic.Ebx);
            BinaryPrimitives.WriteUInt32LittleEndian(vendorBytes[4..], basic.Edx);
            BinaryPrimitives.WriteUInt32LittleEndian(vendorBytes[8..], basic.Ecx);
            foreach (var value in vendorBytes)
                if (value is < 0x20 or > 0x7e)
                    throw new InvalidDataException("CPUID vendor identity must contain twelve printable ASCII bytes.");
            var vendor = Encoding.ASCII.GetString(vendorBytes);
            int? family = null, model = null, stepping = null;
            bool? hypervisor = null, digitalThermal = null, packageThermal = null;
            if (basic.Eax >= 1)
            {
                var signature = Read(1);
                var baseFamily = (int)((signature.Eax >> 8) & 15);
                family = baseFamily + (baseFamily == 15 ? (int)((signature.Eax >> 20) & 255) : 0);
                model = (int)((signature.Eax >> 4) & 15) +
                    (baseFamily is 6 or 15 ? (int)((signature.Eax >> 16) & 15) * 16 : 0);
                stepping = (int)(signature.Eax & 15);
                hypervisor = (signature.Ecx & 0x80000000) != 0;
            }
            if (basic.Eax >= 6 && vendor == "GenuineIntel")
            {
                var thermal = Read(6);
                // Intel SDM: CPUID.06H:EAX bits 0 (DTS) and 6 (package thermal management).
                digitalThermal = (thermal.Eax & 1) != 0;
                packageThermal = (thermal.Eax & 0x40) != 0;
            }
            else probes.Add(new("CPUID", "thermal-capability", SensorInterfaceStatus.Unsupported,
                "Intel thermal feature decoding is unavailable for this vendor or advertised leaf range; no false flag inferred."));
            var provider = vendor == "GenuineIntel" && family == 6 && model is 0x8c or 0x8d
                ? "LibreHardwareMonitor 0.9.6 TigerLake read-MSR provider; live access still requires independent testing."
                : null;
            probes.Add(new("CPUID", "fixed-leaves", SensorInterfaceStatus.Readable,
                "CPU-reported capabilities only, on the current processor; no actual temperature, DRAM or system-power reading."));
            return new(new(vendor, basic.Eax, family, model, stepping, hypervisor, digitalThermal, packageThermal, provider), leaves, probes);
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidDataException)
        {
            probes.Add(new("CPUID", "fixed-leaves", exception is InvalidDataException
                ? SensorInterfaceStatus.InvalidPayload : SensorInterfaceStatus.NoInterface, exception.Message));
            return new(null, leaves, probes);
        }
    }
}
